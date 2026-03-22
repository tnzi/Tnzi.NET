namespace Tnzi.AI.Infrastructure.ContextProviders;

/// <summary>
/// 实体记忆上下文提供器 — 从 IEntityMemoryStore 加载已知实体并注入上下文，
/// 在对话完成后从助手回复中提取新实体并持久化
/// </summary>
public sealed class EntityMemoryContextProvider : IContextProvider
{
    private readonly IEntityMemoryStore _entityMemoryStore;
    private readonly EntityMemoryOptions _options;
    private readonly LlmEntityExtractor _extractor;
    private readonly ICurrentUser? _currentUser;
    private readonly IAgentExecutionContextAccessor? _executionContextAccessor;
    private readonly Guid? _defaultAgentId;
    private readonly ILogger<EntityMemoryContextProvider> _logger;

    public EntityMemoryContextProvider(
        IEntityMemoryStore entityMemoryStore,
        EntityMemoryOptions options,
        LlmEntityExtractor extractor,
        ILogger<EntityMemoryContextProvider> logger,
        ICurrentUser? currentUser = null,
        Guid? agentId = null,
        IAgentExecutionContextAccessor? executionContextAccessor = null)
    {
        _entityMemoryStore = Check.NotNull(entityMemoryStore);
        _options = Check.NotNull(options);
        _extractor = Check.NotNull(extractor);
        _logger = Check.NotNull(logger);
        _currentUser = currentUser;
        _defaultAgentId = agentId;
        _executionContextAccessor = executionContextAccessor;
    }

    /// <inheritdoc />
    public async Task<ContextInjection> GetContextAsync(List<ChatMessage> messages, CancellationToken ct = default)
    {
        try
        {
            var userId = ResolveUserId();
            var agentId = ResolveAgentId();
            var entities = await _entityMemoryStore.GetRelevantEntitiesAsync(userId, agentId, _options.MaxEntitiesPerContext, ct);

            if (entities.Count == 0)
            {
                return ContextInjection.Empty;
            }

            var entityLines = new StringBuilder();
            entityLines.AppendLine("## Known Entities");
            entityLines.AppendLine("The following named entities are known from previous conversations:");
            entityLines.AppendLine();

            foreach (var entity in entities)
            {
                var propsText = entity.Properties.Count > 0
                    ? string.Join(", ", entity.Properties.Select(p => $"{p.Key}: {p.Value}"))
                    : string.Empty;

                entityLines.AppendLine(string.IsNullOrEmpty(propsText)
                    ? $"- {entity.EntityName} ({entity.EntityType})"
                    : $"- {entity.EntityName} ({entity.EntityType}): {propsText}");
            }

            _logger.LogDebug("Injecting {Count} entity memories into context", entities.Count);

            var contextMessage = new ChatMessage(ChatRole.System, entityLines.ToString());

            return new ContextInjection
            {
                Messages = [contextMessage]
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load entity memory context");
            return ContextInjection.Empty;
        }
    }

    /// <inheritdoc />
    public async Task OnCompletedAsync(List<ChatMessage> messages, CancellationToken ct = default)
    {
        try
        {
            // 从最后一轮 User+Assistant 消息中提取实体
            var lastUser = messages.LastOrDefault(m => m.Role == ChatRole.User);
            var lastAssistant = messages.LastOrDefault(m => m.Role == ChatRole.Assistant);

            var textParts = new List<string>(2);
            if (!string.IsNullOrWhiteSpace(lastUser?.Text))
                textParts.Add(lastUser.Text);
            if (!string.IsNullOrWhiteSpace(lastAssistant?.Text))
                textParts.Add(lastAssistant.Text);

            if (textParts.Count == 0)
            {
                return;
            }

            var text = string.Join("\n", textParts);

            var extractedEntities = await _extractor.ExtractAsync(text, ct: ct);
            if (extractedEntities.Count == 0)
            {
                return;
            }

            // 设置用户 ID 和 Agent ID
            var userId = ResolveUserId();
            var agentId = ResolveAgentId();
            foreach (var entity in extractedEntities)
            {
                entity.UserId = userId;
                entity.AgentId = agentId;
            }

            await _entityMemoryStore.UpsertEntitiesAsync(extractedEntities, ct);
            _logger.LogDebug("Extracted and stored {Count} entities from conversation", extractedEntities.Count);
        }
        catch (Exception ex)
        {
            // 实体提取失败不影响主流程
            _logger.LogWarning(ex, "Failed to extract entities from assistant response");
        }
    }

    private Guid? ResolveUserId()
    {
        return _currentUser?.Id ?? _executionContextAccessor?.CurrentRequest?.UserId;
    }

    private Guid? ResolveAgentId()
    {
        return _executionContextAccessor?.CurrentRequest?.AgentId ?? _defaultAgentId;
    }
}
