using Jarvis.Core;
using Jarvis.Infrastructure;
using Jarvis.Windows;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Jarvis.Tests;

public sealed class PerformanceTelemetryTests
{
    [Fact]
    public async Task StoresContentFreeTimingsAndCalculatesLatencyPercentiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "jarvis-perf-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var telemetry = new SqlitePerformanceTelemetry(Path.Combine(root, "jarvis.db")); await telemetry.InitializeAsync();
            var now = DateTimeOffset.UtcNow;
            for (var ms = 100; ms <= 400; ms += 100)
                await telemetry.RecordAsync(new JarvisPerformanceSample(Guid.NewGuid().ToString("N"), now.AddMilliseconds(ms), "local", "", ms, ms / 2, 0, 0, 0, 0, 0, 0, true, false));
            await telemetry.RecordAsync(new JarvisPerformanceSample("cloud-1", now.AddSeconds(1), "gemini", "test-model", 600, 200, 500, 80, 0, 50, 1, 1, true, false));
            await telemetry.RecordAsync(new JarvisPerformanceSample("cloud-2", now.AddSeconds(2), "gemini", "test-model", 900, 400, 700, 100, 0, 80, 2, 1, false, false));
            await telemetry.RecordAsync(new JarvisPerformanceSample("speech-1", now.AddSeconds(3), "speech", "", 120, 120, 0, 0, 120, 0, 0, 0, true, false));
            await telemetry.RecordAsync(new JarvisPerformanceSample("speech-2", now.AddSeconds(4), "speech", "", 220, 220, 0, 0, 220, 0, 0, 0, true, false));

            var d = await telemetry.GetDiagnosticsAsync();
            Assert.Equal(8, d.SampleCount);
            Assert.Equal(4, d.LocalSampleCount);
            Assert.Equal(2, d.GeminiSampleCount);
            Assert.Equal(200, d.LocalP50Ms);
            Assert.Equal(400, d.LocalP90Ms);
            Assert.Equal(600, d.GeminiP50Ms);
            Assert.Equal(900, d.GeminiP90Ms);
            Assert.Equal(200, d.GeminiFirstOutputP50Ms);
            Assert.Equal(1, d.FailedCount);
            Assert.Equal(120, d.SpeechInputP50Ms);
            Assert.Equal(220, d.SpeechInputP90Ms);
            Assert.Equal(3, d.ModelCallCount);
            Assert.Equal(2, d.ToolCallCount);
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DiagnosticsCommandIsLocalAndDoesNotNeedGemini()
    {
        var root = Path.Combine(Path.GetTempPath(), "jarvis-perf-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var telemetry = new SqlitePerformanceTelemetry(Path.Combine(root, "jarvis.db")); await telemetry.InitializeAsync();
            var router = new LocalCommandRouter(performance: telemetry);
            var result = await router.TryRunAsync("show JARVIS diagnostics");
            Assert.True(result.Handled);
            Assert.Contains("No performance samples yet", result.Response);
        }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
}
