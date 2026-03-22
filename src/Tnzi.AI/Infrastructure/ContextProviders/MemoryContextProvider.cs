namespace Tnzi.AI.Infrastructure.ContextProviders;

/// <summary>
/// 记忆上下文提供器 — 从 IMemoryStore 读取记忆并注入为 System 消息，
/// 在对话完成后可选自动沉淀记忆
/// </summary>
public sealed class MemoryContextProvider : IContextProvider
{
    private readonly IMemoryStore _memoryStore;
    private readonly MemoryScope _scope;
    private readonly IChatClientFactory? _chatClientFactory;
    private readonly MemoryOptions? _memoryOptions;
    private readonly IMemoryConsolidator? _memoryConsolidator;
    private readonly ILogger<MemoryContextProvider> _logger;
    /// <summary>
    /// 初始化（兼容旧签名）
    /// </summary>
    public MemoryContextProvider(IMemoryStore memoryStore, string scope, ILogger<MemoryContextProvider> logger)
        : this(memoryStore, new MemoryScope(scope), logger, null, null, null)
    {
    }

    /// <summary>
    /// 初始化（含 MemoryScope + 可选自动沉淀）
    /// </summary>
    public MemoryContextProvider(
        IMemoryStore memoryStore,
        MemoryScope scope,
        ILogger<MemoryContextProvider> logger,
        IChatClientFactory? chatClientFactory = null,
        MemoryOptions? memoryOptions = null,
        IMemoryConsolidator? memoryConsolidator = null)
    {
        _memoryStore = Check.NotNull(memoryStore);
        _scope = Check.NotNull(scope);
        _logger = Check.NotNull(logger);
        _chatClientFactory = chatClientFactory;
        _memoryOptions = memoryOptions;
        _memoryConsolidator = memoryConsolidator;
    }

    /// <inheritdoc />
    public async Task<ContextInjection> GetContextAsync(List<ChatMessage> messages, CancellationToken ct = default)
    {
        try
        {
            var parts = new List<string>(2);

            // 1. 读取用户级记忆（带 UserId 隔离的 scope）
            var userMemory = await _memoryStore.ReadAsync(_scope, ct);
            if (!string.IsNullOrWhiteSpace(userMemory))
            {
                parts.Add(userMemory);
                _logger.LogDebug("Loaded user memory for scope {Scope}, length: {Length}", _scope.Name, userMemory.Length);
            }

            // 2. 读取全局共享记忆（不带 UserId，所有用户可见）
            var sharedScope = _memoryOptions?.SharedScope;
            if (!string.IsNullOrEmpty(sharedScope))
            {
                var sharedMemory = await _memoryStore.ReadAsync(sharedScope, ct);
                if (!string.IsNullOrWhiteSpace(sharedMemory))
                {
                    parts.Add(sharedMemory);
                    _logger.LogDebug("Loaded shared memory for scope {SharedScope}, length: {Length}", sharedScope, sharedMemory.Length);
                }
            }

            if (parts.Count == 0)
            {
                return ContextInjection.Empty;
            }

            var combined = string.Join("\n", parts);
            var contextMessage = new ChatMessage(ChatRole.System,
                $"## Persistent Memory\nThe following is your persistent memory for scope '{_scope.Name}':\n\n{combined}");

            return new ContextInjection
            {
                Messages = [contextMessage]
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load memory context for scope {Scope}", _scope.Name);
            return ContextInjection.Empty;
        }
    }

    /// <inheritdoc />
    public async Task OnCompletedAsync(List<ChatMessage> messages, CancellationToken ct = default)
    {
        if (_memoryOptions is not { AutoPersist: true } || _chatClientFactory == null)
        {
            return;
        }

        try
        {
            await AutoPersistAsync(messages, ct);
        }
        catch (Exception ex)
        {
            // 自动沉淀失败不影响主流程
            _logger.LogWarning(ex, "Auto-persist failed for scope {Scope}", _scope.Name);
        }
    }

    /// <summary>
    /// 自动记忆沉淀 — 从对话中提取持久记忆并追加
    /// </summary>
    private async Task AutoPersistAsync(List<ChatMessage> messages, CancellationToken ct)
    {
        // 提取最后一轮 User+Assistant 消息
        var lastUser = messages.LastOrDefault(m => m.Role == ChatRole.User);
        var lastAssistant = messages.LastOrDefault(m => m.Role == ChatRole.Assistant);

        if (lastUser == null || lastAssistant == null)
        {
            return;
        }

        var conversationSnippet = $"User: {lastUser.Text}\nAssistant: {lastAssistant.Text}";

        // 用 LLM 提炼持久记忆
        var prompt = _memoryOptions!.AutoPersistPrompt
            ?? (_memoryOptions.IncrementalConsolidate && _memoryConsolidator != null
                ? IncrementalAutoPersistPrompt
                : DefaultAutoPersistPrompt);

        try
        {
            var chatClient = _chatClientFactory!.GetChatClient();
            var extractMessages = new List<ChatMessage>
            {
                new(ChatRole.System, prompt),
                new(ChatRole.User, conversationSnippet)
            };

            var response = await chatClient.GetResponseAsync(extractMessages,
                new ChatOptions { MaxOutputTokens = _memoryOptions.AutoPersistMaxTokens }, ct);

            var extracted = response.Text?.Trim();
            if (string.IsNullOrWhiteSpace(extracted) || extracted.Equals("NONE", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // 增量合并或直接追加
            if (_memoryOptions!.IncrementalConsolidate && _memoryConsolidator != null)
            {
                await IncrementalPersistAsync(extracted, ct);
            }
            else
            {
                await _memoryStore.AppendAsync(_scope, extracted, ct);
            }

            _logger.LogDebug("Auto-persisted memory for scope {Scope}: {Length} chars", _scope.Name, extracted.Length);

            // 可选: 合并去重
            if (_memoryOptions.AutoConsolidate)
            {
                await TryConsolidateAsync(chatClient, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Auto-persist LLM call failed for scope {Scope}", _scope.Name);
        }
    }

    /// <summary>
    /// 增量持久化 — 逐条与已有记忆比对，决定 ADD/UPDATE/DELETE/NOOP
    /// </summary>
    private async Task IncrementalPersistAsync(string extractedMemories, CancellationToken ct)
    {
        var lines = extractedMemories.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var maxCalls = _memoryOptions!.MaxConsolidationCallsPerPersist;
        var consolidationCount = 0;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || (line.StartsWith('-') && line.Length < 3))
                continue;

            var (memoryContent, category, importance) = ParseMemoryLine(line);
            if (string.IsNullOrWhiteSpace(memoryContent))
                continue;

            if (consolidationCount >= maxCalls)
            {
                await _memoryStore.AppendAsync(_scope, memoryContent, importance, category, ct);
                continue;
            }

            try
            {
                var existing = await _memoryStore.SearchAsync(_scope, memoryContent,
                    _memoryOptions.ConsolidateSearchTopK, ct);

                if (existing.Count == 0)
                {
                    await _memoryStore.AppendAsync(_scope, memoryContent, importance, category, ct);
                    continue;
                }

                consolidationCount++;
                var result = await _memoryConsolidator!.ConsolidateAsync(memoryContent, existing, ct);

                switch (result.Action)
                {
                    case MemoryAction.Add:
                        await _memoryStore.AppendAsync(_scope, memoryContent, importance, category, ct);
                        break;
                    case MemoryAction.Update when result.TargetEntryId.HasValue && result.UpdatedContent != null:
                        await _memoryStore.UpdateEntryAsync(_scope.ToScopeKey(), result.TargetEntryId.Value, result.UpdatedContent, ct);
                        break;
                    case MemoryAction.Delete when result.TargetEntryId.HasValue:
                        await _memoryStore.DeleteEntryAsync(_scope.ToScopeKey(), result.TargetEntryId.Value, ct);
                        break;
                    case MemoryAction.Noop:
                        _logger.LogDebug("Memory consolidation decided NOOP for: {Memory}", memoryContent[..Math.Min(50, memoryContent.Length)]);
                        break;
                    default:
                        await _memoryStore.AppendAsync(_scope, memoryContent, importance, category, ct);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Incremental consolidation failed for memory line, falling back to append");
                await _memoryStore.AppendAsync(_scope, memoryContent, importance, category, ct);
            }
        }
    }

    private static readonly Regex BracketFormatRegex = new(
        @"^\[(?:category=(\w+)\s+)?(?:importance=([\d.]+)\s*)?\]\s*(.+)$",
        RegexOptions.Compiled);

    private static (string content, string? category, double importance) ParseMemoryLine(string line)
    {
        var cleaned = line.TrimStart('-', ' ');
        var match = BracketFormatRegex.Match(cleaned);
        if (match.Success)
        {
            var category = match.Groups[1].Success ? match.Groups[1].Value : null;
            var importance = match.Groups[2].Success && double.TryParse(match.Groups[2].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var imp) ? imp : 0.5;
            var content = match.Groups[3].Value;
            return (content, category, Math.Clamp(importance, 0, 1));
        }
        return (cleaned, null, 0.5);
    }

    /// <summary>
    /// 尝试合并去重 — 当条目数超过阈值时触发
    /// </summary>
    private async Task TryConsolidateAsync(IChatClient chatClient, CancellationToken ct)
    {
        var currentContent = await _memoryStore.ReadAsync(_scope, ct);
        if (string.IsNullOrWhiteSpace(currentContent))
        {
            return;
        }

        var lineCount = currentContent.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        if (lineCount < _memoryOptions!.ConsolidateThreshold)
        {
            return;
        }

        try
        {
            var consolidateMessages = new List<ChatMessage>
            {
                new(ChatRole.System, ConsolidatePrompt),
                new(ChatRole.User, currentContent)
            };

            var response = await chatClient.GetResponseAsync(consolidateMessages, cancellationToken: ct);
            var consolidated = response.Text?.Trim();

            if (!string.IsNullOrWhiteSpace(consolidated))
            {
                await _memoryStore.WriteAsync(_scope, consolidated, ct);
                _logger.LogDebug("Consolidated memory for scope {Scope}: {Before} lines → {After} chars",
                    _scope.Name, lineCount, consolidated.Length);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Memory consolidation failed for scope {Scope}", _scope.Name);
        }
    }

    private const string DefaultAutoPersistPrompt = """
        Extract any durable, actionable information from the following conversation that should be remembered across sessions.
        Focus on: user preferences, confirmed facts, key decisions, recurring patterns.
        Exclude: one-time requests, transient information, sensitive data.
        Return each memory as a concise bullet point, one per line.
        If nothing is worth remembering, return "NONE".
        """;

    private const string IncrementalAutoPersistPrompt = """
        Extract any durable, actionable information from the following conversation that should be remembered across sessions.
        Focus on: user preferences, confirmed facts, key decisions, recurring patterns.
        Exclude: one-time requests, transient information, sensitive data.
        Return each memory on its own line in this exact format:
        [category=<type> importance=<0.0-1.0>] <content>

        Valid categories: preference, fact, decision, pattern, instruction
        importance: 0.0 (trivial) to 1.0 (critical)

        Examples:
        [category=preference importance=0.8] User prefers dark theme
        [category=fact importance=0.9] User is the CTO of Acme Corp
        [category=decision importance=0.7] Team chose PostgreSQL for the new service

        If nothing is worth remembering, return "NONE".
        """;

    private const string ConsolidatePrompt = """
        Consolidate the following memory entries by:
        1. Removing duplicates (keep the most recent/complete version)
        2. Merging related entries
        3. Removing outdated or contradictory information (keep the latest)
        4. Keeping entries concise and actionable
        Return the consolidated memory as bullet points, one per line.
        """;
}
