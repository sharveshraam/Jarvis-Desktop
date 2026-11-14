using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Jarvis.Core;

namespace Jarvis.Windows;

public sealed record LocalCommandResult(bool Handled, string? Response = null);

public sealed partial class LocalCommandRouter
{
    private static readonly IReadOnlyDictionary<string, string> Applications = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["notepad"] = "notepad.exe",
        ["calculator"] = "calc.exe",
        ["calc"] = "calc.exe",
        ["paint"] = "mspaint.exe",
        ["file explorer"] = "explorer.exe",
        ["explorer"] = "explorer.exe"
    };

    public async Task<LocalCommandResult> TryRunAsync(string text, CancellationToken cancellationToken = default)
    {
        var normalized = text.Trim();
        var now = DateTimeOffset.Now;
        if (Regex.IsMatch(normalized, @"^(what(?:'s| is) the )?time\??$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return new(true, $"It is {now.ToString("h:mm tt", CultureInfo.CurrentCulture)}.");
        if (Regex.IsMatch(normalized, @"^(what(?:'s| is) the )?date\??$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return new(true, $"Today is {now.ToString("dddd, d MMMM yyyy", CultureInfo.CurrentCulture)}.");

        var calculation = CalculationPattern().Match(normalized);
        if (calculation.Success && TryCalculate(calculation.Groups[1].Value, out var answer))
            return new(true, answer.ToString("G", CultureInfo.InvariantCulture));

        var launch = LaunchPattern().Match(normalized);
        if (launch.Success)
        {
            var name = launch.Groups[1].Value.Trim().TrimEnd('.', '!');
            if (!Applications.TryGetValue(name, out var executable))
                return new(true, $"I can open a limited set of apps directly right now. '{name}' is not in the launch allowlist yet.");
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
                await Task.Yield();
                return new(true, $"Opened {DisplayName(name)}.");
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return new(true, $"Windows could not open {DisplayName(name)}: {ex.Message}");
            }
        }

        return new(false);
    }

    private static string DisplayName(string name) => name.ToLowerInvariant() switch
    {
        "calc" or "calculator" => "Calculator",
        "file explorer" or "explorer" => "File Explorer",
        _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name)
    };

    private static bool TryCalculate(string expression, out double result)
    {
        result = 0;
        var match = ArithmeticPattern().Match(expression.Replace("×", "*", StringComparison.Ordinal).Replace("÷", "/", StringComparison.Ordinal));
        if (!match.Success || !double.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var left) ||
            !double.TryParse(match.Groups[3].Value, CultureInfo.InvariantCulture, out var right)) return false;
        result = match.Groups[2].Value switch
        {
            "+" => left + right,
            "-" => left - right,
            "*" => left * right,
            "/" when right != 0 => left / right,
            _ => double.NaN
        };
        return double.IsFinite(result);
    }

    [GeneratedRegex(@"^(?:open|launch|start)\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LaunchPattern();

    [GeneratedRegex(@"^(?:calculate|what is)\s+(.+?)\s*\??$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CalculationPattern();

    [GeneratedRegex(@"^(-?\d+(?:\.\d+)?)\s*([+\-*/])\s*(-?\d+(?:\.\d+)?)$")]
    private static partial Regex ArithmeticPattern();
}
