using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Jarvis.Core;
using Jarvis.Desktop.ViewModels;
using Jarvis.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Jarvis.Desktop;

public partial class MainWindow : Window
{
    private const int HotkeyId = 0x4A4152;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;
    private HwndSource? _source;
    private bool _hotkeyRegistered;
    private int _registeredHotkeyId;
    private string? _registeredHotkey;
    private CancellationTokenSource? _voiceRequest;

    public MainWindow(ChatViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Messages.CollectionChanged += (_, _) => ChatScroll.ScrollToEnd();
        SourceInitialized += (_, _) =>
        {
            _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _source?.AddHook(WindowHook);
        };
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (Application.Current is App { IsExiting: true }) return;
        e.Cancel = true;
        Hide();
    }

    public string? ConfigureGlobalShortcut(string binding)
    {
        if (_hotkeyRegistered && string.Equals(binding, _registeredHotkey, StringComparison.OrdinalIgnoreCase)) return null;
        var hwnd = new WindowInteropHelper(this).Handle;

        var parts = binding.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        uint modifiers = ModNoRepeat;
        var keyTokens = new List<string>();
        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": case "control": modifiers |= ModControl; break;
                case "alt": modifiers |= ModAlt; break;
                case "shift": modifiers |= ModShift; break;
                case "win": case "windows": modifiers |= ModWin; break;
                default: keyTokens.Add(part); break;
            }
        }
        if (keyTokens.Count != 1 || (modifiers & (ModControl | ModAlt | ModShift | ModWin)) == 0)
            return "Use one key plus at least one modifier, for example Ctrl+Alt+J.";

        var keyName = keyTokens[0].ToUpperInvariant();
        uint virtualKey;
        if (keyName.Length == 1 && keyName[0] is >= 'A' and <= 'Z' or >= '0' and <= '9')
            virtualKey = keyName[0];
        else if (keyName == "SPACE") virtualKey = 0x20;
        else if (keyName.Length >= 2 && keyName[0] == 'F' && int.TryParse(keyName[1..], out var functionKey) && functionKey is >= 1 and <= 12)
            virtualKey = (uint)(0x6F + functionKey);
        else return "Choose a letter, number, Space, or F1–F12 as the shortcut key.";

        var candidateId = !_hotkeyRegistered ? HotkeyId : _registeredHotkeyId == HotkeyId ? HotkeyId + 1 : HotkeyId;
        if (!RegisterHotKey(hwnd, candidateId, modifiers, virtualKey))
            return "Windows could not register that shortcut. It may already be in use; choose another one.";
        if (_hotkeyRegistered) UnregisterHotKey(hwnd, _registeredHotkeyId);
        _registeredHotkeyId = candidateId;
        _registeredHotkey = binding;
        _hotkeyRegistered = true;
        return null;
    }

    private nint WindowHook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == _registeredHotkeyId)
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            InputBox.Focus();
            handled = true;
        }
        return nint.Zero;
    }

    protected override void OnClosed(EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (_hotkeyRegistered) UnregisterHotKey(hwnd, _registeredHotkeyId);
        _source?.RemoveHook(WindowHook);
        base.OnClosed(e);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    private void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            ((ChatViewModel)DataContext).SendCommand.Execute(null);
        }
    }

    private async void Voice_Click(object sender, RoutedEventArgs e)
    {
        if (_voiceRequest is not null)
        {
            _voiceRequest.Cancel();
            return;
        }
        var request = new CancellationTokenSource();
        var listenTimer = Stopwatch.StartNew();
        var recognized = false;
        var canceled = false;
        _voiceRequest = request;
        var viewModel = (ChatViewModel)DataContext;
        VoiceButton.Content = "■";
        viewModel.Status = "Listening on this PC…";
        try
        {
            var transcript = await App.Current.Services().GetRequiredService<ISpeechInputService>().ListenOnceAsync(request.Token);
            if (string.IsNullOrWhiteSpace(transcript)) viewModel.Status = "No speech recognized";
            else
            {
                recognized = true;
                viewModel.Input = string.IsNullOrWhiteSpace(viewModel.Input)
                    ? transcript
                    : $"{viewModel.Input.TrimEnd()} {transcript}";
                viewModel.Status = "Speech added to the input box. Review it before sending.";
                InputBox.Focus();
            }
        }
        catch (OperationCanceledException) { canceled = true; viewModel.Status = "Listening stopped"; }
        catch (Exception ex) { viewModel.Status = $"Microphone unavailable: {ex.Message}"; }
        finally
        {
            listenTimer.Stop();
            try
            {
                await App.Current.Services().GetRequiredService<IPerformanceTelemetry>().RecordAsync(
                    new JarvisPerformanceSample(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, "speech", string.Empty,
                        listenTimer.ElapsedMilliseconds, recognized ? listenTimer.ElapsedMilliseconds : null, 0, 0,
                        listenTimer.ElapsedMilliseconds, 0, 0, 0, recognized, canceled));
            }
            catch (Exception ex) { Debug.WriteLine($"Speech telemetry was unavailable: {ex.Message}"); }
            if (ReferenceEquals(_voiceRequest, request)) _voiceRequest = null;
            request.Dispose();
            VoiceButton.Content = "🎙";
        }
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        var services = App.Current.Services();
        var settings = new SettingsWindow(
            services.GetRequiredService<ISecretStore>(),
            services.GetRequiredService<IUserProfileStore>(),
            services.GetRequiredService<IUserPreferencesStore>()) { Owner = this };
        if (settings.ShowDialog() != true) return;
        try
        {
            if (settings.NewApiKey is { Length: > 0 } key)
                await services.GetRequiredService<ISecretStore>().SetAsync("gemini-api-key", key);
            if (settings.NewProfile is { } profile)
                await services.GetRequiredService<IUserProfileStore>().SaveAsync(profile);
            if (settings.NewPreferences is { } preferences)
            {
                await services.GetRequiredService<IUserPreferencesStore>().SaveAsync(preferences);
                services.GetRequiredService<IStartupRegistration>().SetEnabled(preferences.RunAtStartup);
                var shortcutError = ConfigureGlobalShortcut(preferences.GlobalShortcut);
                ((ChatViewModel)DataContext).Status = shortcutError ?? "Settings saved";
            }
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not save settings", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}

public sealed class MessageBrushConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        value is Jarvis.Core.ChatRole.User ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 65, 76)) : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(32, 39, 49));
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
}

public sealed class MessageAlignmentConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        value is Jarvis.Core.ChatRole.User ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
}

internal static class ServiceProviderAccess
{
    public static IServiceProvider Services(this Application app) => ((App)app).Services;
}
