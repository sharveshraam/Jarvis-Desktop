using System.Diagnostics;
using Microsoft.Win32;
using Jarvis.Core;

namespace Jarvis.Windows;

public sealed class WindowsStartupRegistration : IStartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "JarvisDesktop";

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
            ?? throw new InvalidOperationException("Windows could not open the current-user startup settings.");
        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            throw new InvalidOperationException("JARVIS could not locate its executable for Windows startup.");
        key.SetValue(ValueName, $"\"{executable}\"", RegistryValueKind.String);
    }
}
