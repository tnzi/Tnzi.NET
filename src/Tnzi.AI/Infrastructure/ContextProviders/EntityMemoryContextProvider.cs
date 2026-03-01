namespace Tnzi.AI.Infrastructure.ContextProviders;

/// <summary>
/// 实体记忆上下文提供器 — 从 IEntityMemoryStore 加载已知实体并注入上下文，
/// 在对话完成后从助手回复中提取新实体并持久化
/// </summary>
[ExperimentalApi(Reason = "Entity memory is in preview")]
public sealed class EntityMemoryContextProvider : IContextProvider
{
    private readonly IEntityMemoryStore _entityMemoryStore;
    private readonly EntityMemoryOptions _options;
    private readonly LlmEntityExtractor _extractor;
    private readonly ICurrentUser? _currentUser;
    private readonly ILogger<EntityMemoryContextProvider> _logger;

    public EntityMemoryContextProvider(
        IEntityMemoryStore entityMemoryStore,
        EntityMemoryOptions options,
        LlmEntityExtractor extractor,
        ILogger<EntityMemoryContextProvider> logger,
        ICurrentUser? currentUser = null)
    {
        _entityMemoryStore = Check.NotNull(entityMemoryStore);
        _options = Check.NotNull(options);
        _extractor = Check.NotNull(extractor);
        _logger = Check.NotNull(logger);
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public async Task<ContextInjection> GetContextAsync(List<ChatMessage> messages, CancellationToken ct = default)
    {
        try
        {
            var userId = _currentUser?.Id;
            var entities = await _entityMemoryStore.GetRelevantEntitiesAsync(userId, _options.MaxEntitiesPerContext, ct);

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
            // 从最后一条助手消息中提取实体
            var lastAssistantMessage = messages
                .LastOrDefault(m => m.Role == ChatRole.Assistant);

            var text = lastAssistantMessage?.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var extractedEntities = await _extractor.ExtractAsync(text, ct: ct);
            if (extractedEntities.Count == 0)
            {
                return;
            }

            // 设置用户 ID
            var userId = _currentUser?.Id;
            foreach (var entity in extractedEntities)
            {
                entity.UserId = userId;
            }

            await _entityMemoryStore.UpsertEntitiesAsync(extractedEntities, ct);
            _logger.LogDebug("Extracted and stored {Count} entities from assistant response", extractedEntities.Count);
        }
        catch (Exception ex)
        {
            // 实体提取失败不影响主流程
            _logger.LogWarning(ex, "Failed to extract entities from assistant response");
        }
    }
}
