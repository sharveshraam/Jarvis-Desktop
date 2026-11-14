using Jarvis.Core;
using Microsoft.Data.Sqlite;

namespace Jarvis.Infrastructure;

public sealed class SqliteConversationStore(string databasePath) : IConversationStore
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS messages (
                id TEXT PRIMARY KEY,
                role TEXT NOT NULL CHECK (role IN ('User', 'Assistant')),
                content TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_messages_created_at ON messages(created_at);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChatMessage>> GetRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, role, content, created_at FROM messages ORDER BY created_at DESC LIMIT $limit";
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 100));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var messages = new List<ChatMessage>();
        while (await reader.ReadAsync(cancellationToken))
        {
            messages.Add(new ChatMessage(
                reader.GetString(0),
                Enum.Parse<ChatRole>(reader.GetString(1)),
                reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(3))));
        }
        messages.Reverse();
        return messages;
    }

    public async Task AddAsync(ChatMessage message, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO messages(id, role, content, created_at) VALUES($id, $role, $content, $created)";
        command.Parameters.AddWithValue("$id", message.Id);
        command.Parameters.AddWithValue("$role", message.Role.ToString());
        command.Parameters.AddWithValue("$content", message.Content);
        command.Parameters.AddWithValue("$created", message.CreatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
