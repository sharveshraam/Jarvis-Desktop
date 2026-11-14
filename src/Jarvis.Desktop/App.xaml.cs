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
                services.AddSingleton(new HttpClient { BaseAddress = new Uri("https://generativelanguage.googleapis.com/"), Timeout = TimeSpan.FromSeconds(45) });
                services.AddSingleton<IAiProvider>(sp => new GeminiProvider(sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<ISecretStore>()));
                services.AddSingleton<IActionApproval, MessageBoxActionApproval>();
                services.AddSingleton<DesktopFileSystem>();
                services.AddSingleton<ISystemInfoProvider, WindowsSystemInfo>();
                services.AddSingleton<ILocalFileSearch, WindowsFileSearch>();
                services.AddSingleton<IApplicationLauncher, WindowsApplicationLauncher>();
                services.AddSingleton<LocalCommandRouter>();
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
            var window = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            InitializeTray(window);
            window.Show();
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
}
