using Jarvis.Core;
using Microsoft.Data.Sqlite;

namespace Jarvis.Infrastructure;

/// <summary>Local, user-directed memory stored alongside JARVIS's other SQLite data.</summary>
public sealed class SqliteMemoryStore(string databasePath) : IMemoryStore
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
            CREATE TABLE IF NOT EXISTS memories (
                id TEXT PRIMARY KEY,
                content TEXT NOT NULL UNIQUE COLLATE NOCASE,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_memories_updated ON memories(updated_at_utc DESC);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddAsync(string content, CancellationToken cancellationToken = default)
    {
        content = content.Trim();
        if (content.Length is < 1 or > 2000) throw new ArgumentException("A memory must be between 1 and 2,000 characters.", nameof(content));
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO memories(id, content, created_at_utc, updated_at_utc)
            VALUES($id, $content, $now, $now)
            ON CONFLICT(content) DO UPDATE SET updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$content", content);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<JarvisMemory>> SearchAsync(string? query = null, int limit = 30, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, content, created_at_utc, updated_at_utc FROM memories WHERE $query IS NULL OR content LIKE $pattern ESCAPE '\\' ORDER BY updated_at_utc DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$query", string.IsNullOrWhiteSpace(query) ? DBNull.Value : query.Trim());
        command.Parameters.AddWithValue("$pattern", string.IsNullOrWhiteSpace(query) ? DBNull.Value : $"%{EscapeLike(query.Trim())}%");
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 100));
        var items = new List<JarvisMemory>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new JarvisMemory(reader.GetString(0), reader.GetString(1), DateTimeOffset.Parse(reader.GetString(2), System.Globalization.CultureInfo.InvariantCulture), DateTimeOffset.Parse(reader.GetString(3), System.Globalization.CultureInfo.InvariantCulture)));
        return items;
    }

    public async Task<int> DeleteMatchingAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("Enter a detail to forget.", nameof(query));
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM memories WHERE content LIKE $pattern ESCAPE '\\';";
        command.Parameters.AddWithValue("$pattern", $"%{EscapeLike(query.Trim())}%");
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string EscapeLike(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
}
