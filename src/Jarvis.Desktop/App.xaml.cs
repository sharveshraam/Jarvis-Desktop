using System.Net.Http;
using System.IO;
using System.Windows;
using Jarvis.AI;
using Jarvis.Core;
using Jarvis.Desktop.ViewModels;
using Jarvis.Infrastructure;
using Jarvis.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Forms = System.Windows.Forms;

namespace Jarvis.Desktop;

public partial class App : Application
{
    private readonly IHost _host;
    private Forms.NotifyIcon? _trayIcon;
    private System.Windows.Threading.DispatcherTimer? _reminderTimer;
    private bool _checkingReminders;
    public bool IsExiting { get; private set; }

    public App()
    {
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jarvis");
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IConversationStore>(_ => new SqliteConversationStore(Path.Combine(appData, "jarvis.db")));
                services.AddSingleton<ISecretStore>(_ => new DpapiSecretStore(Path.Combine(appData, "secrets")));
                services.AddSingleton<IUserProfileStore>(_ => new JsonUserProfileStore(Path.Combine(appData, "profile.json")));
                services.AddSingleton<IUserPreferencesStore>(_ => new JsonUserPreferencesStore(Path.Combine(appData, "preferences.json")));
                services.AddSingleton<IStartupRegistration, WindowsStartupRegistration>();
                services.AddSingleton<ISpeechInputService, WindowsSpeechInputService>();
                services.AddSingleton<ISpeechOutputService, WindowsSpeechOutputService>();
                services.AddSingleton<IReminderStore>(_ => new SqliteReminderStore(Path.Combine(appData, "jarvis.db")));
                services.AddSingleton<IMemoryStore>(_ => new SqliteMemoryStore(Path.Combine(appData, "jarvis.db")));
                services.AddSingleton(new HttpClient { BaseAddress = new Uri("https://generativelanguage.googleapis.com/"), Timeout = TimeSpan.FromSeconds(45) });
                services.AddSingleton<IAiProvider>(sp => new GeminiProvider(sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<ISecretStore>()));
                services.AddSingleton<IActionApproval, MessageBoxActionApproval>();
                services.AddSingleton<DesktopFileSystem>();
                services.AddSingleton<ISystemInfoProvider, WindowsSystemInfo>();
                services.AddSingleton<ILocalFileSearch, WindowsFileSearch>();
                services.AddSingleton<IApplicationLauncher, WindowsApplicationLauncher>();
                services.AddSingleton<LocalCommandRouter>();
                services.AddSingleton<ProjectWorkspaceTools>();
                services.AddSingleton<IAgentToolExecutor>(sp => new LocalAgentToolExecutor(
                    sp.GetRequiredService<LocalCommandRouter>(),
                    sp.GetRequiredService<IActionApproval>(),
                    sp.GetRequiredService<DesktopFileSystem>(),
                    sp.GetRequiredService<ProjectWorkspaceTools>()));
                services.AddSingleton<ChatViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        try
        {
            await _host.StartAsync();
            await _host.Services.GetRequiredService<IConversationStore>().InitializeAsync();
            await _host.Services.GetRequiredService<IReminderStore>().InitializeAsync();
            await _host.Services.GetRequiredService<IMemoryStore>().InitializeAsync();
            var window = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            InitializeTray(window);
            window.Show();
            var preferences = await _host.Services.GetRequiredService<IUserPreferencesStore>().GetAsync();
            var shortcutError = window.ConfigureGlobalShortcut(preferences.GlobalShortcut);
            if (shortcutError is not null) ((ChatViewModel)window.DataContext).Status = shortcutError;
            _reminderTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _reminderTimer.Tick += ReminderTimer_Tick;
            _reminderTimer.Start();
            base.OnStartup(e);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"JARVIS could not start. {ex.Message}", "JARVIS startup error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _reminderTimer?.Stop();
        await _host.StopAsync(TimeSpan.FromSeconds(3));
        _host.Dispose();
        base.OnExit(e);
    }

    public void ExitFromTray()
    {
        IsExiting = true;
        Shutdown();
    }

    private void InitializeTray(Window window)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open JARVIS", null, (_, _) => ShowMainWindow(window));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => ExitFromTray());
        _trayIcon = new Forms.NotifyIcon
        {
            Text = "JARVIS",
            Icon = System.Drawing.SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow(window);
    }

    private static void ShowMainWindow(Window window)
    {
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    private async void ReminderTimer_Tick(object? sender, EventArgs e)
    {
        if (_checkingReminders) return;
        _checkingReminders = true;
        try
        {
            var store = _host.Services.GetRequiredService<IReminderStore>();
            var due = await store.GetDueAsync(DateTimeOffset.UtcNow);
            if (due.Count == 0) return;
            var summary = string.Join("; ", due.Take(3).Select(reminder => reminder.Title));
            if (due.Count > 3) summary += $"; and {due.Count - 3} more";
            _trayIcon?.ShowBalloonTip(8_000, "JARVIS reminder", summary, Forms.ToolTipIcon.Info);
            foreach (var reminder in due) await store.MarkNotifiedAsync(reminder.Id);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Reminder notification failed: {ex.Message}"); }
        finally { _checkingReminders = false; }
    }
}
