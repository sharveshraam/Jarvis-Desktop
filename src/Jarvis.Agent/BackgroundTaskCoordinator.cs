using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading.Channels;
using Jarvis.Core;
using Microsoft.Extensions.Hosting;

namespace Jarvis.Agent;

/// <summary>Serial, durable runner for explicitly approved project build and test jobs.</summary>
public sealed class BackgroundTaskCoordinator(
    IBackgroundTaskStore store,
    IUserPreferencesStore preferences,
    IActionApproval approval) : BackgroundService, IProjectTaskCoordinator
{
    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new(StringComparer.Ordinal);
    public event Action<JarvisBackgroundTask>? TaskFinished;

    public async Task<JarvisBackgroundTask> QueueAsync(JarvisBackgroundTaskKind kind, CancellationToken cancellationToken = default)
    {
        var prefs = await preferences.GetAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(prefs.ProjectWorkspacePath) || !Directory.Exists(prefs.ProjectWorkspacePath))
            throw new InvalidOperationException("Choose an existing project workspace in Settings first.");
        var title = kind == JarvisBackgroundTaskKind.BuildProject ? "Build project?" : "Run project tests?";
        var command = kind == JarvisBackgroundTaskKind.BuildProject ? "dotnet build --no-restore" : "dotnet test --no-restore";
        if (!await approval.ConfirmAsync(title, $"JARVIS will run `{command}` in this project folder. The process can execute project build/test targets and scripts.\n\n{prefs.ProjectWorkspacePath}", cancellationToken))
            throw new OperationCanceledException("Project task was not approved.", cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var task = new JarvisBackgroundTask(Guid.NewGuid().ToString("N")[..8], kind, JarvisBackgroundTaskState.Queued,
            0, "Waiting in queue", null, now, now);
        await store.AddAsync(task, cancellationToken);
        await _queue.Writer.WriteAsync(task.Id, cancellationToken);
        return task;
    }

    public Task<IReadOnlyList<JarvisBackgroundTask>> GetRecentAsync(int limit = 20, CancellationToken cancellationToken = default) =>
        store.GetRecentAsync(limit, cancellationToken);

    public async Task<bool> CancelAsync(string id, CancellationToken cancellationToken = default)
    {
        var task = await store.GetAsync(id, cancellationToken);
        if (task is null)
        {
            var matches = (await store.GetRecentAsync(100, cancellationToken))
                .Where(t => t.Id.StartsWith(id, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
            if (matches.Length != 1) return false;
            task = matches[0];
        }
        if (task is null || task.State is not (JarvisBackgroundTaskState.Queued or JarvisBackgroundTaskState.Running)) return false;
        if (_running.TryGetValue(id, out var taskCancellation))
        {
            try { taskCancellation.Cancel(); } catch (ObjectDisposedException) { return false; }
        }
        else
        {
            var canceled = task with { State = JarvisBackgroundTaskState.Canceled, Stage = "Canceled before starting", UpdatedAt = DateTimeOffset.UtcNow };
            await store.UpdateAsync(canceled, cancellationToken);
            TaskFinished?.Invoke(canceled);
        }
        return true;
    }

    public async Task RecoverQueuedAsync(CancellationToken cancellationToken = default)
    {
        foreach (var task in await store.GetQueuedAsync(cancellationToken))
            await _queue.Writer.WriteAsync(task.Id, cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var id in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            var task = await store.GetAsync(id, stoppingToken);
            if (task is null || task.State != JarvisBackgroundTaskState.Queued) continue;
            await RunOneAsync(task, stoppingToken);
        }
    }

    private async Task RunOneAsync(JarvisBackgroundTask task, CancellationToken stoppingToken)
    {
        var prefs = await preferences.GetAsync(stoppingToken);
        var workspace = prefs.ProjectWorkspacePath;
        if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace))
        {
            await FinishAsync(task, JarvisBackgroundTaskState.Failed, "Project workspace is missing. Select it again in Settings.", stoppingToken);
            return;
        }
        var solution = Directory.EnumerateFiles(workspace, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault()
            ?? Directory.EnumerateFiles(workspace, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (solution is null)
        {
            await FinishAsync(task, JarvisBackgroundTaskState.Failed, "No solution or project file was found at the workspace root.", stoppingToken);
            return;
        }

        var verb = task.Kind == JarvisBackgroundTaskKind.BuildProject ? "build" : "test";
        var current = task with { State = JarvisBackgroundTaskState.Running, ProgressPercent = 0, Stage = $"Starting dotnet {verb}", UpdatedAt = DateTimeOffset.UtcNow };
        await store.UpdateAsync(current, stoppingToken);
        var start = new ProcessStartInfo(ResolveDotNetHost())
        {
            WorkingDirectory = workspace,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(verb);
        start.ArgumentList.Add(solution);
        start.ArgumentList.Add("--no-restore");
        start.ArgumentList.Add("--verbosity");
        start.ArgumentList.Add("minimal");
        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        var output = new Queue<string>();
        var writeLock = new SemaphoreSlim(1, 1);
        using var taskCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _running[task.Id] = taskCancellation;
        Task stdout = Task.CompletedTask, stderr = Task.CompletedTask;
        try
        {
            var ct = taskCancellation.Token;
            if (!process.Start()) throw new InvalidOperationException("Could not start the .NET command-line tool.");
            stdout = CaptureAsync(process.StandardOutput, output, writeLock, current);
            stderr = CaptureAsync(process.StandardError, output, writeLock, current);
            await process.WaitForExitAsync(ct);
            await Task.WhenAll(stdout, stderr);
            var summary = string.Join(Environment.NewLine, output);
            await FinishAsync(current with { Stage = "Finished", Result = summary }, process.ExitCode == 0 ? JarvisBackgroundTaskState.Succeeded : JarvisBackgroundTaskState.Failed,
                $"dotnet {verb} exited with code {process.ExitCode}.\n{summary}", CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            try { await process.WaitForExitAsync(); await Task.WhenAll(stdout, stderr); } catch { }
            var shuttingDown = stoppingToken.IsCancellationRequested;
            await FinishAsync(current, shuttingDown ? JarvisBackgroundTaskState.Interrupted : JarvisBackgroundTaskState.Canceled,
                shuttingDown ? "App is closing; this process was stopped and will not be replayed automatically." : "Canceled by user.", CancellationToken.None);
        }
        catch (Exception ex)
        {
            TryKill(process);
            await FinishAsync(current, JarvisBackgroundTaskState.Failed, ex.Message, CancellationToken.None);
        }
        finally
        {
            _running.TryRemove(task.Id, out _);
            taskCancellation.Dispose();
            writeLock.Dispose();
        }
    }

    private async Task CaptureAsync(StreamReader reader, Queue<string> output, SemaphoreSlim writeLock, JarvisBackgroundTask task)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            if (line.Length == 0) continue;
            await writeLock.WaitAsync();
            try
            {
                output.Enqueue(line);
                while (output.Count > 45) output.Dequeue();
                var updated = task with { Stage = line, UpdatedAt = DateTimeOffset.UtcNow,
                    Result = string.Join(Environment.NewLine, output) };
                await store.UpdateAsync(updated, CancellationToken.None);
            }
            finally { writeLock.Release(); }
        }
    }

    private async Task FinishAsync(JarvisBackgroundTask task, JarvisBackgroundTaskState state, string result, CancellationToken ct)
    {
        var final = task with { State = state, ProgressPercent = state == JarvisBackgroundTaskState.Succeeded ? 100 : task.ProgressPercent,
            Stage = state.ToString(), Result = result, UpdatedAt = DateTimeOffset.UtcNow };
        await store.UpdateAsync(final, ct);
        TaskFinished?.Invoke(final);
    }

    private static void TryKill(Process process) { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } }

    private static string ResolveDotNetHost()
    {
        var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(root))
        {
            var candidate = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (File.Exists(candidate)) return candidate;
        }
        return OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
    }
}
