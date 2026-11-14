using System.Windows;
using Jarvis.Core;

namespace Jarvis.Desktop;

public sealed class MessageBoxActionApproval : IActionApproval
{
    public Task<bool> ConfirmAsync(string title, string details, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = Application.Current?.MainWindow;
        var answer = owner is null
            ? MessageBox.Show($"JARVIS wants to perform this action:\n\n{details}\n\nContinue?", title, MessageBoxButton.YesNo, MessageBoxImage.Warning)
            : MessageBox.Show(owner, $"JARVIS wants to perform this action:\n\n{details}\n\nContinue?", title, MessageBoxButton.YesNo, MessageBoxImage.Warning);
        return Task.FromResult(answer == MessageBoxResult.Yes);
    }
}
