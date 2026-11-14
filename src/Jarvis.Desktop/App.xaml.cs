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

namespace Jarvis.Desktop;

public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Jarvis");
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IConversationStore>(_ => new SqliteConversationStore(Path.Combine(appData, "jarvis.db")));
                services.AddSingleton<ISecretStore>(_ => new DpapiSecretStore(Path.Combine(appData, "secrets")));
                services.AddSingleton(new HttpClient { BaseAddress = new Uri("https://generativelanguage.googleapis.com/"), Timeout = TimeSpan.FromSeconds(45) });
                services.AddSingleton<IAiProvider>(sp => new GeminiProvider(sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<ISecretStore>()));
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
        await _host.StopAsync(TimeSpan.FromSeconds(3));
        _host.Dispose();
        base.OnExit(e);
    }
}
