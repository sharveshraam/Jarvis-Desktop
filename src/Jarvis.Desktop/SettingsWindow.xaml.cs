using System.Windows;
using Jarvis.Core;

namespace Jarvis.Desktop;

public partial class SettingsWindow : Window
{
    private readonly ISecretStore _secretStore;
    private readonly IUserProfileStore _profileStore;
    public string? NewApiKey { get; private set; }
    public UserProfile? NewProfile { get; private set; }

    public SettingsWindow(ISecretStore secretStore, IUserProfileStore profileStore)
    {
        InitializeComponent();
        _secretStore = secretStore;
        _profileStore = profileStore;
        Loaded += async (_, _) =>
        {
            try
            {
                var key = await _secretStore.GetAsync("gemini-api-key");
                ApiKeyBox.Password = string.Empty;
                ApiKeyBox.Tag = key is not null;
                var profile = await _profileStore.GetAsync();
                ProfileNameBox.Text = profile.DisplayName;
                ProfileContextBox.Text = profile.PersonalContext;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not read saved key", MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        NewApiKey = string.IsNullOrWhiteSpace(ApiKeyBox.Password) ? null : ApiKeyBox.Password.Trim();
        NewProfile = new UserProfile(ProfileNameBox.Text, ProfileContextBox.Text);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
