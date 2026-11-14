namespace Jarvis.Core;

public enum ChatRole { User, Assistant }

public sealed record ChatMessage(string Id, ChatRole Role, string Content, DateTimeOffset CreatedAt);

public enum JarvisMode { Personal, Study, Development, Research, Focus, System, Creator }

public sealed record ChatRequest(IReadOnlyList<ChatMessage> History, string UserText, JarvisMode Mode);

public sealed record ChatResponse(string Text, string Model, TimeSpan Elapsed);

public interface IAiProvider
{
    IAsyncEnumerable<string> StreamAsync(ChatRequest request, CancellationToken cancellationToken = default);
}

public interface IConversationStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChatMessage>> GetRecentAsync(int limit, CancellationToken cancellationToken = default);
    Task AddAsync(ChatMessage message, CancellationToken cancellationToken = default);
}

public interface ISecretStore
{
    Task<string?> GetAsync(string name, CancellationToken cancellationToken = default);
    Task SetAsync(string name, string value, CancellationToken cancellationToken = default);
    Task DeleteAsync(string name, CancellationToken cancellationToken = default);
}
