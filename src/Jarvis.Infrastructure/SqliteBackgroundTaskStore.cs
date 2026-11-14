using Jarvis.Core;
using Microsoft.Data.Sqlite;

namespace Jarvis.Infrastructure;

public sealed class SqliteBackgroundTaskStore(string databasePath) : IBackgroundTaskStore
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
            CREATE TABLE IF NOT EXISTS background_tasks (
              id TEXT PRIMARY KEY,
              kind TEXT NOT NULL,
              state TEXT NOT NULL,
              progress_percent INTEGER NOT NULL,
              stage TEXT NOT NULL,
              result TEXT NULL,
              created_at_utc TEXT NOT NULL,
              updated_at_utc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_background_tasks_queue ON background_tasks(state, created_at_utc);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddAsync(JarvisBackgroundTask task, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO background_tasks VALUES($id,$kind,$state,$progress,$stage,$result,$created,$updated);";
        AddParameters(command, task);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task<IReadOnlyList<JarvisBackgroundTask>> GetQueuedAsync(CancellationToken cancellationToken = default) =>
        QueryAsync("SELECT * FROM background_tasks WHERE state=$state ORDER BY created_at_utc LIMIT 20;", JarvisBackgroundTaskState.Queued, cancellationToken);

    public Task<IReadOnlyList<JarvisBackgroundTask>> GetRecentAsync(int limit = 20, CancellationToken cancellationToken = default) =>
        QueryAsync("SELECT * FROM background_tasks ORDER BY created_at_utc DESC LIMIT $limit;", null, cancellationToken, Math.Clamp(limit, 1, 100));

    public async Task<JarvisBackgroundTask?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        var items = await QueryAsync("SELECT * FROM background_tasks WHERE id=$id LIMIT 1;", null, cancellationToken, id: id);
        return items.FirstOrDefault();
    }

    public async Task UpdateAsync(JarvisBackgroundTask task, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE background_tasks SET state=$state,progress_percent=$progress,stage=$stage,result=$result,updated_at_utc=$updated WHERE id=$id;";
        command.Parameters.AddWithValue("$id", task.Id);
        command.Parameters.AddWithValue("$state", task.State.ToString());
        command.Parameters.AddWithValue("$progress", Math.Clamp(task.ProgressPercent, 0, 100));
        command.Parameters.AddWithValue("$stage", task.Stage[..Math.Min(task.Stage.Length, 200)]);
        command.Parameters.AddWithValue("$result", (object?)TrimResult(task.Result) ?? DBNull.Value);
        command.Parameters.AddWithValue("$updated", Stamp(task.UpdatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> MarkRunningInterruptedAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE background_tasks SET state=$interrupted,stage='Interrupted by app restart; not rerun automatically.',updated_at_utc=$now WHERE state=$running;";
        command.Parameters.AddWithValue("$interrupted", JarvisBackgroundTaskState.Interrupted.ToString());
        command.Parameters.AddWithValue("$running", JarvisBackgroundTaskState.Running.ToString());
        command.Parameters.AddWithValue("$now", Stamp(DateTimeOffset.UtcNow));
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<JarvisBackgroundTask>> QueryAsync(string sql, JarvisBackgroundTaskState? state,
        CancellationToken ct, int? limit = null, string? id = null)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        if (state is not null) command.Parameters.AddWithValue("$state", state.Value.ToString());
        if (limit is not null) command.Parameters.AddWithValue("$limit", limit.Value);
        if (id is not null) command.Parameters.AddWithValue("$id", id);
        var items = new List<JarvisBackgroundTask>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            items.Add(new JarvisBackgroundTask(reader.GetString(0), Enum.Parse<JarvisBackgroundTaskKind>(reader.GetString(1)),
                Enum.Parse<JarvisBackgroundTaskState>(reader.GetString(2)), reader.GetInt32(3), reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                DateTimeOffset.Parse(reader.GetString(6), System.Globalization.CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(reader.GetString(7), System.Globalization.CultureInfo.InvariantCulture)));
        return items;
    }

    private static void AddParameters(SqliteCommand command, JarvisBackgroundTask task)
    {
        command.Parameters.AddWithValue("$id", task.Id);
        command.Parameters.AddWithValue("$kind", task.Kind.ToString());
        command.Parameters.AddWithValue("$state", task.State.ToString());
        command.Parameters.AddWithValue("$progress", Math.Clamp(task.ProgressPercent, 0, 100));
        command.Parameters.AddWithValue("$stage", task.Stage);
        command.Parameters.AddWithValue("$result", (object?)TrimResult(task.Result) ?? DBNull.Value);
        command.Parameters.AddWithValue("$created", Stamp(task.CreatedAt));
        command.Parameters.AddWithValue("$updated", Stamp(task.UpdatedAt));
    }

    private static string? TrimResult(string? result) => result is null ? null : result.Length <= 8_000 ? result : result[^8_000..];
}
