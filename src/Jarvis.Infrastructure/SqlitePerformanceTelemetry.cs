using Jarvis.Core;
using Microsoft.Data.Sqlite;

namespace Jarvis.Infrastructure;

/// <summary>Stores timing and counts only; never accepts prompts, transcripts, API keys, or tool payloads.</summary>
public sealed class SqlitePerformanceTelemetry(string databasePath) : IPerformanceTelemetry
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(databasePath) }.ToString();
    private static string Stamp(DateTimeOffset value) => value.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var builder = new SqliteConnectionStringBuilder(_connectionString);
        Directory.CreateDirectory(Path.GetDirectoryName(builder.DataSource)!);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS performance_samples (
              id TEXT PRIMARY KEY,
              completed_at_utc TEXT NOT NULL,
              execution_path TEXT NOT NULL,
              model_id TEXT NOT NULL,
              total_ms INTEGER NOT NULL,
              first_output_ms INTEGER NULL,
              model_ms INTEGER NOT NULL,
              tool_ms INTEGER NOT NULL,
              speech_input_ms INTEGER NOT NULL,
              speech_ms INTEGER NOT NULL,
              model_calls INTEGER NOT NULL,
              tool_calls INTEGER NOT NULL,
              succeeded INTEGER NOT NULL,
              canceled INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_performance_completed ON performance_samples(completed_at_utc DESC);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RecordAsync(JarvisPerformanceSample sample, CancellationToken cancellationToken = default)
    {
        if (sample.ExecutionPath is not ("local" or "gemini" or "speech")) throw new ArgumentException("Execution path must be local, gemini, or speech.", nameof(sample));
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO performance_samples VALUES($id,$completed,$path,$model,$total,$first,$modelMs,$toolMs,$speechInputMs,$speechMs,$modelCalls,$toolCalls,$success,$canceled);
            DELETE FROM performance_samples WHERE id NOT IN (SELECT id FROM performance_samples ORDER BY completed_at_utc DESC LIMIT 5000);
            """;
        command.Parameters.AddWithValue("$id", sample.Id);
        command.Parameters.AddWithValue("$completed", Stamp(sample.CompletedAt));
        command.Parameters.AddWithValue("$path", sample.ExecutionPath);
        command.Parameters.AddWithValue("$model", SanitizeModelId(sample.ModelId));
        command.Parameters.AddWithValue("$total", Math.Max(0, sample.TotalDurationMs));
        command.Parameters.AddWithValue("$first", (object?)sample.FirstOutputMs ?? DBNull.Value);
        command.Parameters.AddWithValue("$modelMs", Math.Max(0, sample.ModelDurationMs));
        command.Parameters.AddWithValue("$toolMs", Math.Max(0, sample.ToolDurationMs));
        command.Parameters.AddWithValue("$speechMs", Math.Max(0, sample.SpeechDurationMs));
        command.Parameters.AddWithValue("$speechInputMs", Math.Max(0, sample.SpeechInputDurationMs));
        command.Parameters.AddWithValue("$modelCalls", Math.Max(0, sample.ModelCallCount));
        command.Parameters.AddWithValue("$toolCalls", Math.Max(0, sample.ToolCallCount));
        command.Parameters.AddWithValue("$success", sample.Succeeded ? 1 : 0);
        command.Parameters.AddWithValue("$canceled", sample.Canceled ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<JarvisPerformanceDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        var all = new List<JarvisPerformanceSample>();
        await using (var connection = new SqliteConnection(_connectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT id,completed_at_utc,execution_path,model_id,total_ms,first_output_ms,model_ms,tool_ms,speech_input_ms,speech_ms,model_calls,tool_calls,succeeded,canceled FROM performance_samples ORDER BY completed_at_utc DESC LIMIT 1000;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                all.Add(new JarvisPerformanceSample(reader.GetString(0), DateTimeOffset.Parse(reader.GetString(1), System.Globalization.CultureInfo.InvariantCulture),
                    reader.GetString(2), reader.GetString(3), reader.GetInt64(4), reader.IsDBNull(5) ? null : reader.GetInt64(5),
                    reader.GetInt64(6), reader.GetInt64(7), reader.GetInt64(8), reader.GetInt64(9), reader.GetInt32(10), reader.GetInt32(11), reader.GetInt32(12) != 0, reader.GetInt32(13) != 0));
        }
        var local = all.Where(s => s.ExecutionPath == "local").ToArray();
        var gemini = all.Where(s => s.ExecutionPath == "gemini").ToArray();
        var voice = all.Where(s => s.ExecutionPath == "speech").ToArray();
        var first = gemini.Where(s => s.FirstOutputMs.HasValue).Select(s => s.FirstOutputMs!.Value).ToArray();
        var speechInput = voice.Select(s => s.SpeechInputDurationMs).Where(ms => ms > 0).ToArray();
        var speech = all.Where(s => s.SpeechDurationMs > 0).Select(s => s.SpeechDurationMs).ToArray();
        return new JarvisPerformanceDiagnostics(all.Count, local.Length, gemini.Length,
            Percentile(local.Select(s => s.TotalDurationMs), .50), Percentile(local.Select(s => s.TotalDurationMs), .90), Percentile(local.Select(s => s.TotalDurationMs), .95),
            Percentile(gemini.Select(s => s.TotalDurationMs), .50), Percentile(gemini.Select(s => s.TotalDurationMs), .90), Percentile(gemini.Select(s => s.TotalDurationMs), .95),
            Percentile(first, .50), Percentile(first, .90), Percentile(gemini.Select(s => s.ModelDurationMs), .50), Percentile(gemini.Select(s => s.ToolDurationMs), .50),
            Percentile(speechInput, .50), Percentile(speechInput, .90), Percentile(speech, .50), Percentile(speech, .90),
            all.Count(s => !s.Succeeded && !s.Canceled), all.Count(s => s.Canceled),
            all.Sum(s => s.ModelCallCount), all.Sum(s => s.ToolCallCount), all.Count == 0 ? null : all[0].CompletedAt);
    }

    private static long? Percentile(IEnumerable<long> values, double percentile)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length == 0 ? null : sorted[Math.Max(0, (int)Math.Ceiling(percentile * sorted.Length) - 1)];
    }

    private static string SanitizeModelId(string model) => model.Length <= 100 && model.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-') ? model : "unknown";
}
