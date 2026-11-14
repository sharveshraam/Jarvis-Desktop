using System.Diagnostics;
using Jarvis.Core;

namespace Jarvis.Windows;

public sealed class WindowsLocalFileOpener : ILocalFileOpener
{
    public void Open(string fullPath)
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = Path.GetFullPath(fullPath),
            UseShellExecute = true
        });
        if (process is null) throw new InvalidOperationException("Windows did not open the selected file.");
        process.Dispose();
    }
}
