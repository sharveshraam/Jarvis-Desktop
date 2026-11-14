using System.Windows;
using Jarvis.Core;

namespace Jarvis.Desktop;

public partial class SettingsWindow : Window
{
    private readonly ISecretStore _secretStore;
    private readonly IUserProfileStore _profileStore;
    private readonly IUserPreferencesStore _preferencesStore;
    public string? NewApiKey { get; private set; }
    public UserProfile? NewProfile { get; private set; }
    public UserPreferences? NewPreferences { get; private set; }

    public SettingsWindow(ISecretStore secretStore, IUserProfileStore profileStore, IUserPreferencesStore preferencesStore)
    {
        InitializeComponent();
        _secretStore = secretStore;
        _profileStore = profileStore;
        _preferencesStore = preferencesStore;
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
                var preferences = await _preferencesStore.GetAsync();
                GlobalShortcutBox.Text = preferences.GlobalShortcut;
                RunAtStartupCheckBox.IsChecked = preferences.RunAtStartup;
                SpeakResponsesCheckBox.IsChecked = preferences.SpeakResponses;
                WorkspacePathBox.Text = preferences.ProjectWorkspacePath ?? string.Empty;
                GeminiModelBox.Text = preferences.GeminiModel;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not read saved key", MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        NewApiKey = string.IsNullOrWhiteSpace(ApiKeyBox.Password) ? null : ApiKeyBox.Password.Trim();
        NewProfile = new UserProfile(ProfileNameBox.Text, ProfileContextBox.Text);
        NewPreferences = new UserPreferences(GlobalShortcutBox.Text, RunAtStartupCheckBox.IsChecked == true, SpeakResponsesCheckBox.IsChecked == true, WorkspacePathBox.Text, GeminiModelBox.Text);
        DialogResult = true;
    }

    private void BrowseWorkspace_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose the project workspace", Multiselect = false };
        if (dialog.ShowDialog(this) == true) WorkspacePathBox.Text = dialog.FolderName;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
