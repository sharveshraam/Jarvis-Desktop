using System.Diagnostics;
using Jarvis.Core;

namespace Jarvis.Windows;

public sealed class WindowsApplicationLauncher : IApplicationLauncher
{
    public void Start(string applicationCommand)
    {
        Process.Start(new ProcessStartInfo(applicationCommand) { UseShellExecute = true });
    }
}
