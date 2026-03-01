namespace Tnzi.AI.Infrastructure;

/// <summary>
/// 数据库对话存储 — 桥接到现有 IAgentThreadInternalService，保持向后兼容
/// </summary>
public class DatabaseConversationStore : IConversationStore
{
    private readonly IAgentThreadInternalService _threadService;
    private readonly IRepository<AgentThread, Guid> _threadRepository;
    private readonly ILogger<DatabaseConversationStore> _logger;

    public DatabaseConversationStore(
        IAgentThreadInternalService threadService,
        IRepository<AgentThread, Guid> threadRepository,
        ILogger<DatabaseConversationStore> logger)
    {
        _threadService = Check.NotNull(threadService);
        _threadRepository = Check.NotNull(threadRepository);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public async Task<ConversationContext> GetOrCreateAsync(string conversationId, Guid? agentId = null, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(conversationId);

        if (!Guid.TryParse(conversationId, out var threadId))
        {
            throw new InvalidOperationException($"DatabaseConversationStore requires GUID conversation IDs, got: '{conversationId}'");
        }

        return await _threadService.GetOrCreateThreadAsync(threadId, agentId ?? Guid.Empty, ct);
    }

    /// <inheritdoc />
    public async Task SaveAsync(string conversationId, ConversationContext context, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(conversationId);
        Check.NotNull(context);

        if (!Guid.TryParse(conversationId, out var threadId))
        {
            throw new InvalidOperationException($"DatabaseConversationStore requires GUID conversation IDs, got: '{conversationId}'");
        }

        await _threadService.SaveThreadSerializedDataAsync(threadId, context, ct);
    }

    /// <inheritdoc />
    public async Task AppendMessageAsync(string conversationId, string role, string content, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(conversationId);
        Check.NotNullOrWhiteSpace(role);
        Check.NotNullOrWhiteSpace(content);

        if (!Guid.TryParse(conversationId, out var threadId))
        {
            throw new InvalidOperationException($"DatabaseConversationStore requires GUID conversation IDs, got: '{conversationId}'");
        }

        await _threadService.SaveMessageAsync(threadId, role, content, ct: ct);
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
            await _threadRepository.DeleteAsync(thread);
            _logger.LogDebug("Deleted conversation {ConversationId}", conversationId);
        }
    }
}
