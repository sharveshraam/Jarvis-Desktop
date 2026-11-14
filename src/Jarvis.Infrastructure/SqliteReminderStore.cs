using Jarvis.Core;
using Microsoft.Data.Sqlite;

namespace Jarvis.Infrastructure;

public sealed class SqliteReminderStore(string databasePath) : IReminderStore
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(databasePath) }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var builder = new SqliteConnectionStringBuilder(_connectionString);
        Directory.CreateDirectory(Path.GetDirectoryName(builder.DataSource)!);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS reminders (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                due_at_utc TEXT NOT NULL,
                notified INTEGER NOT NULL DEFAULT 0,
                canceled INTEGER NOT NULL DEFAULT 0
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        command.CommandText = "PRAGMA table_info(reminders);";
        var hasCanceled = false;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                if (reader.GetString(1).Equals("canceled", StringComparison.OrdinalIgnoreCase)) hasCanceled = true;
        if (!hasCanceled)
        {
            command.CommandText = "ALTER TABLE reminders ADD COLUMN canceled INTEGER NOT NULL DEFAULT 0;";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        command.CommandText = "CREATE INDEX IF NOT EXISTS ix_reminders_due ON reminders(notified, canceled, due_at_utc);";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddAsync(JarvisReminder reminder, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO reminders (id, title, due_at_utc, notified) VALUES ($id, $title, $due, 0);";
        command.Parameters.AddWithValue("$id", reminder.Id);
        command.Parameters.AddWithValue("$title", reminder.Title);
        command.Parameters.AddWithValue("$due", reminder.DueAt.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task<IReadOnlyList<JarvisReminder>> GetUpcomingAsync(int limit = 20, CancellationToken cancellationToken = default) =>
        ReadAsync("SELECT id, title, due_at_utc, notified FROM reminders WHERE notified = 0 AND canceled = 0 ORDER BY due_at_utc LIMIT $limit;", limit, null, cancellationToken);

    public Task<IReadOnlyList<JarvisReminder>> GetDueAsync(DateTimeOffset now, CancellationToken cancellationToken = default) =>
        ReadAsync("SELECT id, title, due_at_utc, notified FROM reminders WHERE notified = 0 AND canceled = 0 AND due_at_utc <= $now ORDER BY due_at_utc;", null, now, cancellationToken);

    public async Task MarkNotifiedAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE reminders SET notified = 1 WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> CancelAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE reminders SET canceled = 1 WHERE id = $id AND notified = 0 AND canceled = 0;";
        command.Parameters.AddWithValue("$id", id);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private async Task<IReadOnlyList<JarvisReminder>> ReadAsync(string sql, int? limit, DateTimeOffset? now, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (limit is not null) command.Parameters.AddWithValue("$limit", Math.Clamp(limit.Value, 1, 100));
        if (now is not null) command.Parameters.AddWithValue("$now", now.Value.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        var results = new List<JarvisReminder>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(new JarvisReminder(reader.GetString(0), reader.GetString(1), DateTimeOffset.Parse(reader.GetString(2), System.Globalization.CultureInfo.InvariantCulture), reader.GetInt32(3) != 0));
        return results;
    }
}
