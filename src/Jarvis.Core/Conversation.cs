namespace Jarvis.Core;

public enum ChatRole { User, Assistant }

public sealed record ChatMessage(string Id, ChatRole Role, string Content, DateTimeOffset CreatedAt);

public enum JarvisMode { Personal, Study, Development, Research, Focus, System, Creator }

public sealed record ChatRequest(IReadOnlyList<ChatMessage> History, string UserText, JarvisMode Mode, UserProfile? UserProfile = null);

public sealed record UserProfile(string DisplayName, string PersonalContext)
{
    public static UserProfile Default { get; } = new(
        "Ram",
        "ECE student at Amrita Vishwa Vidyapeetham, Amritapuri. Interested in electronics, embedded systems, software development, and AI. For learning, prefers university-level depth with clear explanations, derivations, and practical examples. Treat these as editable preferences, not confirmed facts.");
}

public interface IUserProfileStore
{
    Task<UserProfile> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(UserProfile profile, CancellationToken cancellationToken = default);
}

public sealed record UserPreferences(string GlobalShortcut, bool RunAtStartup)
{
    public static UserPreferences Default { get; } = new("Ctrl+Alt+J", false);
}

public interface IUserPreferencesStore
{
    Task<UserPreferences> GetAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(UserPreferences preferences, CancellationToken cancellationToken = default);
}

public interface IStartupRegistration
{
    void SetEnabled(bool enabled);
}

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

public sealed record JarvisReminder(string Id, string Title, DateTimeOffset DueAt, bool Notified);

public interface IReminderStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task AddAsync(JarvisReminder reminder, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<JarvisReminder>> GetUpcomingAsync(int limit = 20, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<JarvisReminder>> GetDueAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
    Task MarkNotifiedAsync(string id, CancellationToken cancellationToken = default);
}
