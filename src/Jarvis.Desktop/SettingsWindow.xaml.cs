using System.Windows;
using Jarvis.Core;

namespace Jarvis.Desktop;

public partial class SettingsWindow : Window
{
    private readonly ISecretStore _secretStore;
    public string? NewApiKey { get; private set; }

    public SettingsWindow(ISecretStore secretStore)
    {
        InitializeComponent();
        _secretStore = secretStore;
        Loaded += async (_, _) =>
        {
            try
            {
                var key = await _secretStore.GetAsync("gemini-api-key");
                ApiKeyBox.Password = string.Empty;
                ApiKeyBox.Tag = key is not null;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not read saved key", MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        NewApiKey = string.IsNullOrWhiteSpace(ApiKeyBox.Password) ? null : ApiKeyBox.Password.Trim();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
