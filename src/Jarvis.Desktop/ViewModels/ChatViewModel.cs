using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Jarvis.Core;
using Jarvis.Windows;

namespace Jarvis.Desktop.ViewModels;

public sealed class ChatViewModel : INotifyPropertyChanged
{
    private readonly IAiProvider _ai;
    private readonly IConversationStore _store;
    private readonly LocalCommandRouter _local;
    private string _input = string.Empty;
    private string _status = "Local commands ready";
    private JarvisMode _mode = JarvisMode.Personal;
    private bool _isBusy;
    private CancellationTokenSource? _activeRequest;

    public ChatViewModel(IAiProvider ai, IConversationStore store, LocalCommandRouter local)
    {
        _ai = ai;
        _store = store;
        _local = local;
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
    public JarvisMode Mode { get => _mode; set => Set(ref _mode, value); }
    public bool IsBusy { get => _isBusy; private set { if (Set(ref _isBusy, value)) { ((AsyncRelayCommand)SendCommand).Refresh(); ((RelayCommand)CancelCommand).Refresh(); } } }

    public async Task SendAsync()
    {
        var text = Input.Trim();
        if (text.Length == 0 || IsBusy) return;
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
            string answer;
            if (localResult.Handled)
            {
                answer = localResult.Response ?? "Done.";
                Status = "Completed locally";
            }
            else
            {
                Status = "Sending this conversation to Gemini…";
                var history = Messages.Where(m => m.Id != user.Id).TakeLast(12).ToArray();
                var responseTimer = Stopwatch.StartNew();
                var partial = new StringBuilder();
                ChatMessage? streamedMessage = null;
                await foreach (var chunk in _ai.StreamAsync(new ChatRequest(history, text, Mode), _activeRequest.Token))
                {
                    if (streamedMessage is null)
                    {
                        streamedMessage = NewMessage(ChatRole.Assistant, string.Empty);
                        Messages.Add(streamedMessage);
                        Status = $"Gemini · first text in {responseTimer.Elapsed.TotalSeconds:0.0}s";
                    }
                    partial.Append(chunk);
                    streamedMessage = streamedMessage with { Content = partial.ToString() };
                    Messages[^1] = streamedMessage;
                }

                answer = partial.ToString().Trim();
                if (answer.Length == 0) throw new InvalidDataException("Gemini returned no text.");
                Status = $"Gemini · {responseTimer.Elapsed.TotalSeconds:0.0}s";
                streamedMessage ??= NewMessage(ChatRole.Assistant, answer);
                streamedMessage = streamedMessage with { Content = answer };
                if (Messages.Count == 0 || Messages[^1].Id != streamedMessage.Id) Messages.Add(streamedMessage);
                await _store.AddAsync(streamedMessage);
                return;
            }

            var assistant = NewMessage(ChatRole.Assistant, answer);
            Messages.Add(assistant);
            await _store.AddAsync(assistant);
        }
        catch (OperationCanceledException)
        {
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
            _activeRequest?.Dispose();
            _activeRequest = null;
            IsBusy = false;
        }
    }

    public void Cancel() => _activeRequest?.Cancel();

    private async Task LoadAsync()
    {
        try
        {
            var history = await _store.GetRecentAsync(100);
            await Application.Current.Dispatcher.InvokeAsync(() => { foreach (var message in history) Messages.Add(message); });
        }
        catch (Exception ex) { Status = $"History unavailable: {ex.Message}"; }
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
