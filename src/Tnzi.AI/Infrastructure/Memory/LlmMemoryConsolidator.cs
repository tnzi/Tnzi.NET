namespace Tnzi.AI.Infrastructure.Memory;

/// <summary>
/// LLM 驱动的记忆合并器 - 通过语义比对决定 ADD/UPDATE/DELETE/NOOP
/// </summary>
/// <remarks>
/// 所有失败（LLM 超时、速率限制、网络错误、空响应、无效 JSON）均降级为 Add；
/// UPDATE / DELETE 没有点名一条候选记忆时同样降级为 Add。此类不会向调用方抛出异常。
/// </remarks>
public class LlmMemoryConsolidator : IMemoryConsolidator
{
    private readonly IChatClientFactory _chatClientFactory;
    private readonly ILogger<LlmMemoryConsolidator> _logger;

    public LlmMemoryConsolidator(IChatClientFactory chatClientFactory, ILogger<LlmMemoryConsolidator> logger)
    {
        _chatClientFactory = Check.NotNull(chatClientFactory);
        _logger = Check.NotNull(logger);
    }

    public async Task<MemoryConsolidationResult> ConsolidateAsync(
        string newMemory, IReadOnlyList<MemorySearchResult> existingMemories, CancellationToken ct = default)
    {
        try
        {
            var chatClient = _chatClientFactory.GetChatClient();
            var existingText = string.Join("\n", existingMemories.Select((m, i) =>
                $"[{i}] (id={m.Id}) {m.Content}"));

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, ConsolidationPrompt),
                new(ChatRole.User, $"NEW MEMORY:\n{newMemory}\n\nEXISTING MEMORIES:\n{existingText}")
            };

            // 超时保护：LLM 无响应时降级为 Add，避免阻塞上下文持久化流程
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));

            var response = await chatClient.GetResponseAsync(messages,
                new ChatOptions { MaxOutputTokens = 300 }, timeoutCts.Token);

            return ParseResponse(response.Text, existingMemories);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Memory consolidation failed, falling back to Add");
            return new MemoryConsolidationResult(MemoryAction.Add);
        }
    }

    private MemoryConsolidationResult ParseResponse(string? responseText, IReadOnlyList<MemorySearchResult> existing)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return new MemoryConsolidationResult(MemoryAction.Add);

        try
        {
            var json = responseText.Trim();
            if (json.StartsWith("```"))
            {
                var startIdx = json.IndexOf('{');
                var endIdx = json.LastIndexOf('}');
                if (startIdx >= 0 && endIdx > startIdx)
                    json = json[startIdx..(endIdx + 1)];
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var actionStr = root.TryGetProperty("action", out var actionProp)
                ? actionProp.GetString()?.ToLowerInvariant()
                : null;

            var action = actionStr switch
            {
                "add" => MemoryAction.Add,
                "update" => MemoryAction.Update,
                "delete" => MemoryAction.Delete,
                "noop" => MemoryAction.Noop,
                _ => MemoryAction.Add
            };

            var content = root.TryGetProperty("content", out var contentProp)
                ? contentProp.GetString()
                : null;

            Guid? targetId = null;
            if (root.TryGetProperty("targetId", out var targetProp)
                && targetProp.ValueKind == JsonValueKind.String
                && Guid.TryParse(targetProp.GetString(), out var parsed)
                && existing.Any(m => m.Id == parsed))
            {
                targetId = parsed;
            }

            // UPDATE / DELETE 必须点名候选里的某一条：没点名、点名的不是候选（模型编造的 id），
            // 都不替它猜一条去改写或删除，按新增处理：多存一条可以再合并，删错的记忆找不回来
            if (action is MemoryAction.Update or MemoryAction.Delete && targetId == null)
            {
                _logger.LogDebug("Consolidation answered {Action} without a known target, falling back to Add", action);
                return new MemoryConsolidationResult(MemoryAction.Add);
            }

            return new MemoryConsolidationResult(action, content, targetId);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse consolidation response, falling back to Add");
            return new MemoryConsolidationResult(MemoryAction.Add);
        }
    }

    private const string ConsolidationPrompt = """
        You are a memory consolidation agent. Compare a NEW memory against EXISTING memories.
        Decide ONE action:
        - ADD: New memory contains novel information not in existing memories
        - UPDATE: New memory updates/supersedes an existing memory (return the merged content)
        - DELETE: New memory contradicts an existing memory; the existing one is removed and the new memory is stored
        - NOOP: New memory is redundant (already covered by existing memories)

        Return JSON only: {"action": "add|update|delete|noop", "content": "updated content if action=update, else null", "targetId": "id of existing memory to update/delete, else null"}
        """;
}
