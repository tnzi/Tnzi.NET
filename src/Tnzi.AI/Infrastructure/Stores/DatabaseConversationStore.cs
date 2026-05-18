namespace Tnzi.AI.Infrastructure.Stores;

/// <summary>
/// 数据库对话存储 — 桥接到现有 IAgentThreadInternalService，保持向后兼容
/// </summary>
/// <remarks>
/// 此实现要求 conversationId 为 GUID 格式字符串，因底层 AgentThread 实体使用 Guid 主键。
/// </remarks>
public class DatabaseConversationStore : IConversationStore
{
    private readonly IAgentThreadInternalService _threadService;
    private readonly IAgentThreadService _threadCrudService;
    private readonly IRepository<AgentThread, Guid> _threadRepository;
    private readonly ILogger<DatabaseConversationStore> _logger;

    public DatabaseConversationStore(
        IAgentThreadInternalService threadService,
        IAgentThreadService threadCrudService,
        IRepository<AgentThread, Guid> threadRepository,
        ILogger<DatabaseConversationStore> logger)
    {
        _threadService = Check.NotNull(threadService);
        _threadCrudService = Check.NotNull(threadCrudService);
        _threadRepository = Check.NotNull(threadRepository);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public async Task<ConversationContext> GetOrCreateAsync(string conversationId, Guid? agentId = null, CancellationToken ct = default)
    {
        var threadId = ParseThreadId(conversationId);
        var (context, _, _) = await _threadService.GetOrCreateThreadAsync(threadId, agentId, ct);
        return context;
    }

    /// <inheritdoc />
    public async Task SaveAsync(string conversationId, ConversationContext context, CancellationToken ct = default)
    {
        Check.NotNull(context);
        var threadId = ParseThreadId(conversationId);
        await _threadService.SaveThreadSerializedDataAsync(threadId, context, ct);
    }

    /// <inheritdoc />
    public async Task<string> AppendMessageAsync(string conversationId, string role, string content, string? messageId = null, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(role);
        Check.NotNullOrWhiteSpace(content);
        var threadId = ParseThreadId(conversationId);
        Guid? parsedId = null;
        if (!string.IsNullOrWhiteSpace(messageId) && Guid.TryParse(messageId, out var parsed))
        {
            parsedId = parsed;
        }
        var persistedId = await _threadService.SaveMessageAsync(threadId, role, content, messageId: parsedId, ct: ct);
        return persistedId.ToString();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConversationSummary>> ListAsync(int limit = 50, CancellationToken ct = default)
    {
        var threads = await _threadRepository.AsQueryable()
            .OrderByDescending(t => t.LastActivityTime)
            .Take(limit)
            .ToListAsync(ct);

        return threads.Select(t => new ConversationSummary
        {
            Id = t.Id.ToString(),
            Title = t.Title,
            LastActivity = t.LastActivityTime,
            MessageCount = 0 // 数据库实现不存储消息数量
        }).ToList();
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string conversationId, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(conversationId);

        if (!Guid.TryParse(conversationId, out var threadId))
        {
            return;
        }

        var thread = await _threadRepository.GetAsync(threadId, ct);
        if (thread != null)
        {
            await _threadCrudService.DeleteAsync(threadId);
            _logger.LogDebug("Deleted conversation {ConversationId}", conversationId);
        }
    }

    /// <summary>
    /// 将 conversationId 解析为 GUID（底层 AgentThread 使用 Guid 主键）
    /// </summary>
    private static Guid ParseThreadId(string conversationId)
    {
        Check.NotNullOrWhiteSpace(conversationId);
        if (!Guid.TryParse(conversationId, out var threadId))
        {
            throw new ArgumentException(
                $"Invalid conversation ID format: '{conversationId}'. This store implementation requires GUID format.",
                nameof(conversationId));
        }
        return threadId;
    }
}
