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

public interface IActionApproval
{
    Task<bool> ConfirmAsync(string title, string details, CancellationToken cancellationToken = default);
}

public sealed record MemoryStatus(ulong TotalBytes, ulong AvailableBytes)
{
    public ulong UsedBytes => TotalBytes >= AvailableBytes ? TotalBytes - AvailableBytes : 0;
}

public interface ISystemInfoProvider
{
    MemoryStatus GetMemoryStatus();
}

public sealed record LocalFileMatch(string FullPath, DateTime LastModified);
public sealed record LocalFileSearchResult(LocalFileMatch? Match, bool SearchLimitReached);

public interface ILocalFileSearch
{
    Task<LocalFileSearchResult> FindLatestPdfAsync(CancellationToken cancellationToken = default);
}

public interface IApplicationLauncher
{
    void Start(string applicationCommand);
}
