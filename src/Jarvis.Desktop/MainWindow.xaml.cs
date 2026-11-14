using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Jarvis.Core;
using Jarvis.Desktop.ViewModels;
using Jarvis.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Jarvis.Desktop;

public partial class MainWindow : Window
{
    public MainWindow(ChatViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Messages.CollectionChanged += (_, _) => ChatScroll.ScrollToEnd();
    }

    private void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            ((ChatViewModel)DataContext).SendCommand.Execute(null);
        }
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        var settings = new SettingsWindow(App.Current.Services().GetRequiredService<ISecretStore>()) { Owner = this };
        settings.ShowDialog();
        if (settings.NewApiKey is { Length: > 0 } key)
        {
            await App.Current.Services().GetRequiredService<ISecretStore>().SetAsync("gemini-api-key", key);
            ((ChatViewModel)DataContext).Status = "Gemini key saved with Windows protection";
        }
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
