using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Jarvis.Core;
using Jarvis.Windows;

namespace Jarvis.Desktop.ViewModels;

public sealed class ChatViewModel : INotifyPropertyChanged
{
    private readonly IAiProvider _ai;
    private readonly IAgentToolExecutor _agentTools;
    private readonly IConversationStore _store;
    private readonly LocalCommandRouter _local;
    private readonly IUserProfileStore _profileStore;
    private readonly IUserPreferencesStore _preferencesStore;
    private readonly ISpeechOutputService _speechOutput;
    private readonly IPerformanceTelemetry _performance;
    private string _input = string.Empty;
    private string _status = "Local commands ready";
    private JarvisMode _mode = JarvisMode.Personal;
    private bool _loadingPreferences = true;
    private bool _modeChangedDuringLoad;
    private readonly SemaphoreSlim _preferenceWriteLock = new(1, 1);
    private bool _isBusy;
    private CancellationTokenSource? _activeRequest;
    private CancellationTokenSource? _speechRequest;

    public ChatViewModel(IAiProvider ai, IAgentToolExecutor agentTools, IConversationStore store, LocalCommandRouter local, IUserProfileStore profileStore, IUserPreferencesStore preferencesStore, ISpeechOutputService speechOutput, IPerformanceTelemetry performance)
    {
        _ai = ai;
        _agentTools = agentTools;
        _store = store;
        _local = local;
        _profileStore = profileStore;
        _preferencesStore = preferencesStore;
        _speechOutput = speechOutput;
        _performance = performance;
        Messages.CollectionChanged += (_, _) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEmpty)));
        SendCommand = new AsyncRelayCommand(SendAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(Input));
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        _ = LoadAsync();
    }

    public ObservableCollection<ChatMessage> Messages { get; } = [];
    public bool IsEmpty => Messages.Count == 0;
    public event PropertyChangedEventHandler? PropertyChanged;
    public ICommand SendCommand { get; }
    public ICommand CancelCommand { get; }

    public string Input { get => _input; set { if (Set(ref _input, value)) ((AsyncRelayCommand)SendCommand).Refresh(); } }
    public string Status { get => _status; set => Set(ref _status, value); }
    public JarvisMode Mode
    {
        get => _mode;
        set
        {
            if (!Set(ref _mode, value)) return;
            if (_loadingPreferences) _modeChangedDuringLoad = true;
            else _ = PersistSelectedModeAsync();
        }
    }
    public bool IsBusy { get => _isBusy; private set { if (Set(ref _isBusy, value)) { ((AsyncRelayCommand)SendCommand).Refresh(); ((RelayCommand)CancelCommand).Refresh(); } } }

    public async Task SendAsync()
    {
        var text = Input.Trim();
        if (text.Length == 0 || IsBusy) return;
        var requestTimer = Stopwatch.StartNew();
        long? firstOutputMs = null;
        long modelDurationMs = 0;
        long toolDurationMs = 0;
        long speechDurationMs = 0;
        var modelCallCount = 0;
        var toolCallCountMetric = 0;
        var executionPath = "local";
        var modelId = string.Empty;
        var succeeded = false;
        var canceled = false;
        Input = string.Empty;
        var user = NewMessage(ChatRole.User, text);
        Messages.Add(user);
        await _store.AddAsync(user);
        IsBusy = true;
        _activeRequest = new CancellationTokenSource(TimeSpan.FromSeconds(50));
        try
        {
            Status = "Checking local commands…";
            var localResult = await _local.TryRunAsync(text, _activeRequest.Token);
            if (localResult.Handled) firstOutputMs = requestTimer.ElapsedMilliseconds;
            string answer;
            if (localResult.Handled)
            {
                answer = localResult.Response ?? "Done.";
                succeeded = localResult.Succeeded;
                Status = "Completed locally";
            }
            else
            {
                executionPath = "gemini";
                Status = "Sending this conversation to Gemini…";
                var history = Messages.Where(m => m.Id != user.Id).TakeLast(12).ToArray();
                var userProfile = await _profileStore.GetAsync(_activeRequest.Token);
                modelId = (await _preferencesStore.GetAsync(_activeRequest.Token)).GeminiModel;
                var responseTimer = Stopwatch.StartNew();
                var partial = new StringBuilder();
                ChatMessage? streamedMessage = null;
                var toolHistory = new List<AiToolRound>();
                var toolCallCount = 0;
                var gotFinalText = false;
                for (var turn = 0; turn < 3; turn++)
                {
                    var functionCalls = new List<AiFunctionCall>();
                    modelCallCount++;
                    var modelTurnTimer = Stopwatch.StartNew();
                    try
                    {
                        await foreach (var item in _ai.StreamAsync(
                            new ChatRequest(history, text, Mode, userProfile, toolHistory), _activeRequest.Token))
                        {
                            if (firstOutputMs is null && (!string.IsNullOrEmpty(item.Text) || item.FunctionCall is not null))
                                firstOutputMs = requestTimer.ElapsedMilliseconds;
                            if (!string.IsNullOrEmpty(item.Text))
                            {
                                partial.Append(item.Text);
                                if (streamedMessage is null)
                                {
                                    streamedMessage = NewMessage(ChatRole.Assistant, string.Empty);
                                    Messages.Add(streamedMessage);
                                    Status = $"Gemini · first text in {responseTimer.Elapsed.TotalSeconds:0.0}s";
                                }
                                streamedMessage = streamedMessage with { Content = partial.ToString() };
                                Messages[^1] = streamedMessage;
                            }
                            if (item.FunctionCall is not null) functionCalls.Add(item.FunctionCall);
                        }
                    }
                    finally { modelTurnTimer.Stop(); modelDurationMs += modelTurnTimer.ElapsedMilliseconds; }

                    if (functionCalls.Count == 0)
                    {
                        gotFinalText = true;
                        break;
                    }

                    var toolResults = new List<AiToolResult>(functionCalls.Count);
                    foreach (var functionCall in functionCalls)
                    {
                        _activeRequest.Token.ThrowIfCancellationRequested();
                        JsonElement toolResponse;
                        if (toolCallCount >= 6)
                            toolResponse = System.Text.Json.JsonSerializer.SerializeToElement(new { success = false, result = "JARVIS reached the six-tool limit for this request; this action was not run." });
                        else
                        {
                            Status = $"Running local tool: {functionCall.Name}…";
                            var toolTimer = Stopwatch.StartNew();
                            toolResponse = await _agentTools.ExecuteAsync(functionCall, _activeRequest.Token);
                            toolTimer.Stop();
                            toolDurationMs += toolTimer.ElapsedMilliseconds;
                            toolCallCount++;
                            toolCallCountMetric++;
                        }
                        toolResults.Add(new AiToolResult(functionCall.Name, toolResponse, functionCall.Id));
                    }
                    toolHistory.Add(new AiToolRound(functionCalls[0].ModelContent, toolResults));
                }

                succeeded = gotFinalText;
                if (!gotFinalText)
                {
                    partial.AppendLine().Append("I stopped after the three-step tool limit. Ask me to continue with the next part.");
                    if (streamedMessage is null)
                    {
                        streamedMessage = NewMessage(ChatRole.Assistant, partial.ToString());
                        Messages.Add(streamedMessage);
                    }
                    else
                    {
                        streamedMessage = streamedMessage with { Content = partial.ToString() };
                        Messages[^1] = streamedMessage;
                    }
                }

                answer = partial.ToString().Trim();
                if (answer.Length == 0) throw new InvalidDataException("Gemini returned no text.");
                Status = $"Gemini · {responseTimer.Elapsed.TotalSeconds:0.0}s";
                streamedMessage ??= NewMessage(ChatRole.Assistant, answer);
                streamedMessage = streamedMessage with { Content = answer };
                if (Messages.Count == 0 || Messages[^1].Id != streamedMessage.Id) Messages.Add(streamedMessage);
                await _store.AddAsync(streamedMessage);
                speechDurationMs += await SpeakIfEnabledAsync(answer);
                return;
            }

            var assistant = NewMessage(ChatRole.Assistant, answer);
            Messages.Add(assistant);
            await _store.AddAsync(assistant);
            speechDurationMs += await SpeakIfEnabledAsync(answer);
        }
        catch (OperationCanceledException)
        {
            canceled = true;
            Status = "Request cancelled";
            var assistant = NewMessage(ChatRole.Assistant, "Stopped. No further action was taken.");
            Messages.Add(assistant);
            await _store.AddAsync(assistant);
        }
        catch (Exception ex)
        {
            Status = "Request failed";
            var assistant = NewMessage(ChatRole.Assistant, ex.Message);
            Messages.Add(assistant);
            await _store.AddAsync(assistant);
        }
        finally
        {
            requestTimer.Stop();
            try
            {
                await _performance.RecordAsync(new JarvisPerformanceSample(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow,
                    executionPath, modelId, requestTimer.ElapsedMilliseconds, firstOutputMs, modelDurationMs, toolDurationMs,
                    0, speechDurationMs, modelCallCount, toolCallCountMetric, succeeded, canceled));
            }
            catch (Exception ex) { Debug.WriteLine($"Performance telemetry was unavailable: {ex.Message}"); }
            _activeRequest?.Dispose();
            _activeRequest = null;
            IsBusy = false;
        }
    }

    public void Cancel()
    {
        _activeRequest?.Cancel();
        _speechRequest?.Cancel();
    }

    private async Task<long> SpeakIfEnabledAsync(string text)
    {
        UserPreferences preferences;
        try { preferences = await _preferencesStore.GetAsync(); }
        catch { return 0; }
        if (!preferences.SpeakResponses) return 0;

        var speechRequest = new CancellationTokenSource();
        _speechRequest?.Cancel();
        _speechRequest?.Dispose();
        _speechRequest = speechRequest;
        var timer = Stopwatch.StartNew();
        Status = "Speaking response…";
        try { await _speechOutput.SpeakAsync(text, speechRequest.Token); }
        catch (OperationCanceledException) { Status = "Speech stopped"; }
        catch (Exception ex) { Status = $"Speech unavailable: {ex.Message}"; }
        finally
        {
            if (ReferenceEquals(_speechRequest, speechRequest)) _speechRequest = null;
            speechRequest.Dispose();
            timer.Stop();
        }
        return timer.ElapsedMilliseconds;
    }

    private async Task LoadAsync()
    {
        try
        {
            var preferences = await _preferencesStore.GetAsync();
            if (!_modeChangedDuringLoad)
                Set(ref _mode, Enum.IsDefined(preferences.SelectedMode) ? preferences.SelectedMode : JarvisMode.Personal, nameof(Mode));
            _loadingPreferences = false;
            if (_modeChangedDuringLoad) await PersistSelectedModeAsync();

            var history = await _store.GetRecentAsync(100);
            await Application.Current.Dispatcher.InvokeAsync(() => { foreach (var message in history) Messages.Add(message); });
        }
        catch (Exception ex)
        {
            _loadingPreferences = false;
            Status = $"History or preferences unavailable: {ex.Message}";
        }
    }

    private async Task PersistSelectedModeAsync()
    {
        await _preferenceWriteLock.WaitAsync();
        try
        {
            var preferences = await _preferencesStore.GetAsync();
            await _preferencesStore.SaveAsync(preferences with { SelectedMode = Mode });
        }
        catch (Exception ex) { Status = $"Mode could not be saved: {ex.Message}"; }
        finally { _preferenceWriteLock.Release(); }
    }

    private static ChatMessage NewMessage(ChatRole role, string text) => new(Guid.NewGuid().ToString("N"), role, text, DateTimeOffset.Now);

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        return true;
    }
}

public sealed class AsyncRelayCommand(Func<Task> execute, Func<bool> canExecute) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute();
    public async void Execute(object? parameter) => await execute();
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => execute();
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
