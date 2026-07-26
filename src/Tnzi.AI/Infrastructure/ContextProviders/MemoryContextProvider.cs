namespace Tnzi.AI.Infrastructure.ContextProviders;

/// <summary>
/// 记忆上下文提供器 - 从 IMemoryStore 读取记忆并注入为 System 消息，
/// 在对话完成后可选自动沉淀记忆
/// </summary>
public sealed class MemoryContextProvider : IContextProvider
{
    private readonly IMemoryStore _memoryStore;
    private readonly MemoryScope _scope;
    private readonly MemoryScope? _agentBoundScope;
    private readonly IChatClientFactory? _chatClientFactory;
    private readonly MemoryOptions? _memoryOptions;
    private readonly IMemoryConsolidator? _memoryConsolidator;
    private readonly IAgentExecutionContextAccessor? _executionContextAccessor;
    private readonly ILogger<MemoryContextProvider> _logger;
    private int _lastAutoExtractTurn;

    /// <summary>
    /// 初始化（兼容旧签名）
    /// </summary>
    public MemoryContextProvider(IMemoryStore memoryStore, string scope, ILogger<MemoryContextProvider> logger)
        : this(memoryStore, new MemoryScope(scope), logger, null, null, null)
    {
    }

    /// <summary>
    /// 初始化（含 MemoryScope + 可选自动沉淀 + 可选 Agent-bound 范围）
    /// </summary>
    /// <param name="agentBoundScope">
    /// 可选的 Agent-bound 记忆范围 - 绑定到当前 Agent（通过结构化 AgentId 列检索），
    /// 与当前用户无关，确保 headless 运行也能加载。为只读注入，不参与自动沉淀。
    /// </param>
    public MemoryContextProvider(
        IMemoryStore memoryStore,
        MemoryScope scope,
        ILogger<MemoryContextProvider> logger,
        IChatClientFactory? chatClientFactory = null,
        MemoryOptions? memoryOptions = null,
        IMemoryConsolidator? memoryConsolidator = null,
        IAgentExecutionContextAccessor? executionContextAccessor = null,
        MemoryScope? agentBoundScope = null)
    {
        _memoryStore = Check.NotNull(memoryStore);
        _scope = Check.NotNull(scope);
        _logger = Check.NotNull(logger);
        _chatClientFactory = chatClientFactory;
        _memoryOptions = memoryOptions;
        _memoryConsolidator = memoryConsolidator;
        _executionContextAccessor = executionContextAccessor;
        // 仅当与本地 scope 键不同才作为独立段加载，避免重复（例如本地 scope 已含相同 agent 维度时）。
        _agentBoundScope = agentBoundScope is { AgentBound: true }
            && !string.Equals(agentBoundScope.ToScopeKey(), scope.ToScopeKey(), StringComparison.OrdinalIgnoreCase)
            ? agentBoundScope
            : null;
    }

    /// <inheritdoc />
    public async Task<ContextInjection> GetContextAsync(List<ChatMessage> messages, CancellationToken ct = default)
    {
        try
        {
            var segments = await LoadMemorySegmentsAsync(ct);
            if (segments.Count == 0)
            {
                return ContextInjection.Empty;
            }

            // 两阶段注入：少量记忆全量注入，大量记忆切换为检索式注入
            var threshold = _memoryOptions?.RetrievalModeThreshold ?? 20;
            var entryCount = segments.Sum(segment => CountMemoryEntries(segment.Content));

            string contextText;
            if (entryCount <= threshold)
            {
                // 全量注入（原有行为）
                contextText = BuildFullContextText(segments);
            }
            else
            {
                // 检索式注入：索引 + SearchAsync top-K
                contextText = await BuildRetrievalContextAsync(segments, messages, entryCount, ct);
            }

            var contextMessage = new ChatMessage(ChatRole.System,
                $"## Persistent Memory\nThe following is your persistent memory for scope '{_scope.Name}':\n\n{contextText}");

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

    /// <summary>
    /// 统计记忆条目数（按非空行计数）
    /// </summary>
    public static int CountMemoryEntries(string memoryContent)
    {
        if (string.IsNullOrWhiteSpace(memoryContent)) return 0;
        return memoryContent.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
    }

    /// <summary>
    /// 构建检索式上下文（精简索引 + SearchAsync top-K 全文）
    /// </summary>
    private async Task<string> BuildRetrievalContextAsync(
        IReadOnlyList<ScopedMemorySegment> segments, List<ChatMessage> messages, int entryCount, CancellationToken ct)
    {
        var topK = _memoryOptions?.RetrievalTopK ?? 8;
        var fullMemory = BuildFullContextText(segments);

        // 1. 构建精简索引（每条记忆的前 80 字符，带编号）
        var index = BuildMemoryIndex(fullMemory);

        // 2. 提取用户最新消息作为查询
        var userQuery = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text;
        if (string.IsNullOrWhiteSpace(userQuery))
        {
            // 没有用户消息时退回全量注入
            return fullMemory;
        }

        // 3. 在本地/项目/共享记忆上分别检索并合并最相关条目
        var searchResults = await SearchAcrossScopesAsync(segments, userQuery, topK, ct);

        if (searchResults.Count == 0)
        {
            // 搜索无结果时注入索引（让 Agent 知道有哪些记忆可用）
            _logger.LogDebug("Retrieval mode: no search results, injecting index only ({Count} entries)", entryCount);
            return $"[Memory index ({entryCount} entries - use search_memory for full content)]\n{index}";
        }

        // 4. 组合：索引 + 相关记忆全文
        var includeLabels = segments.Count > 1;
        var relevant = string.Join("\n", searchResults.Select(r =>
            includeLabels && !string.IsNullOrWhiteSpace(r.Source)
                ? $"[{r.Source}] {r.Content}"
                : r.Content));
        _logger.LogDebug("Retrieval mode: injecting index + {TopK} relevant entries (of {Total} total)",
            searchResults.Count, entryCount);

        return $"[Memory index ({entryCount} entries)]\n{index}\n\n[Most relevant memories for current context]\n{relevant}";
    }

    /// <summary>
    /// 格式化单条记忆条目，附加元数据注解和新鲜度警告
    /// </summary>
    /// <remarks>
    /// 格式: [category, N days ago, importance: X.X] content
    /// 超过 1 天的条目追加新鲜度警告提示
    /// </remarks>
    public static string FormatMemoryEntry(MemoryEntry entry)
    {
        Check.NotNull(entry);

        var category = string.IsNullOrWhiteSpace(entry.Category) ? "general" : entry.Category;
        var daysAgo = (int)(DateTime.UtcNow - entry.CreationTime).TotalDays;
        var importance = entry.Importance.ToString("F1", CultureInfo.InvariantCulture);

        var formatted = $"[{category}, {daysAgo} days ago, importance: {importance}] {entry.Content}";

        if (daysAgo > 0)
        {
            formatted += $" (Note: this memory is {daysAgo} days old - verify against current state before relying on it)";
        }

        return formatted;
    }

    /// <summary>
    /// 构建精简记忆索引（每行取前 80 字符，带序号）
    /// </summary>
    public static string BuildMemoryIndex(string memoryContent)
    {
        var lines = memoryContent.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var sb = new StringBuilder();
        for (var i = 0; i < lines.Length; i++)
        {
            var preview = lines[i].Length > 80 ? lines[i][..80] + "..." : lines[i];
            sb.AppendLine($"  {i + 1}. {preview}");
        }
        return sb.ToString().TrimEnd();
    }

    private async Task<List<ScopedMemorySegment>> LoadMemorySegmentsAsync(CancellationToken ct)
    {
        // 四段记忆（本地、Agent-bound、项目快照、共享）必须顺序读取：
        // IMemoryStore 的缺省实现 DatabaseMemoryStore 走 EF 仓储，同一个 scoped DbContext
        // 不允许并发查询（会抛 "A second operation was started on this context"），
        // 而 GetContextAsync 的 catch 会把它当成"加载失败"静默吞掉 → 记忆永不注入。
        var localMemory = await _memoryStore.ReadAsync(_scope, ct);

        // Agent-bound：绑定到当前 Agent 的记忆（结构化 AgentId 列检索），headless-safe。
        var agentBoundMemory = _agentBoundScope != null
            ? await _memoryStore.ReadAsync(_agentBoundScope, ct)
            : null;

        var projectScope = MemoryScopeResolver.ResolveProjectSnapshotScope(
            _memoryOptions?.EnableProjectSnapshot == true,
            _memoryOptions?.ProjectSnapshotScopePrefix,
            _executionContextAccessor?.CurrentRequest?.Metadata);

        var sharedScope = _memoryOptions?.SharedScope;
        var shouldLoadShared = !string.IsNullOrWhiteSpace(sharedScope)
            && !string.Equals(sharedScope, projectScope, StringComparison.OrdinalIgnoreCase);

        var projectMemory = !string.IsNullOrWhiteSpace(projectScope)
            ? await _memoryStore.ReadAsync(projectScope, ct)
            : null;

        var sharedMemory = shouldLoadShared
            ? await _memoryStore.ReadAsync(sharedScope!, ct)
            : null;

        var segments = new List<ScopedMemorySegment>(4);

        if (!string.IsNullOrWhiteSpace(localMemory))
        {
            segments.Add(new ScopedMemorySegment("Local Memory", _scope.ToScopeKey(), _scope, localMemory));
            _logger.LogDebug("Loaded local memory for scope {Scope}, length: {Length}", _scope.Name, localMemory.Length);
        }

        if (!string.IsNullOrWhiteSpace(agentBoundMemory))
        {
            segments.Add(new ScopedMemorySegment("Agent Memory", _agentBoundScope!.ToScopeKey(), _agentBoundScope, agentBoundMemory));
            _logger.LogDebug("Loaded agent-bound memory for scope {Scope}, length: {Length}", _agentBoundScope.ToScopeKey(), agentBoundMemory.Length);
        }

        if (!string.IsNullOrWhiteSpace(projectMemory))
        {
            segments.Add(new ScopedMemorySegment("Project Snapshot", projectScope!, null, projectMemory));
            _logger.LogDebug("Loaded project snapshot memory for scope {Scope}, length: {Length}", projectScope, projectMemory.Length);
        }

        if (!string.IsNullOrWhiteSpace(sharedMemory))
        {
            segments.Add(new ScopedMemorySegment("Shared Memory", sharedScope!, null, sharedMemory));
            _logger.LogDebug("Loaded shared memory for scope {Scope}, length: {Length}", sharedScope, sharedMemory.Length);
        }

        return segments;
    }

    private static string BuildFullContextText(IReadOnlyList<ScopedMemorySegment> segments)
    {
        if (segments.Count == 1)
        {
            return segments[0].Content;
        }

        var builder = new StringBuilder();
        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            builder.AppendLine($"### {segment.Label}");
            builder.AppendLine(segment.Content);

            if (index < segments.Count - 1)
            {
                builder.AppendLine();
            }
        }

        return builder.ToString().TrimEnd();
    }

    private async Task<IReadOnlyList<MemorySearchResult>> SearchAcrossScopesAsync(
        IReadOnlyList<ScopedMemorySegment> segments,
        string query,
        int topK,
        CancellationToken ct)
    {
        var allResults = new List<MemorySearchResult>();

        foreach (var segment in segments)
        {
            IReadOnlyList<MemorySearchResult> scopeResults = segment.LocalScope != null
                ? await _memoryStore.SearchAsync(segment.LocalScope, query, topK, ct)
                : await _memoryStore.SearchAsync(segment.ScopeKey, query, topK, ct);

            foreach (var result in scopeResults)
            {
                result.Source ??= segment.Label;
            }

            allResults.AddRange(scopeResults);
        }

        return allResults
            .GroupBy(GetSearchResultKey)
            .Select(group => group.OrderByDescending(result => result.Score).First())
            .OrderByDescending(result => result.Score)
            .Take(topK)
            .ToList();
    }

    private static string GetSearchResultKey(MemorySearchResult result)
    {
        if (result.Id.HasValue)
        {
            return result.Id.Value.ToString("N");
        }

        return $"{result.Source ?? "memory"}::{result.Category ?? "general"}::{result.Content}";
    }

    /// <inheritdoc />
    public async Task OnCompletedAsync(List<ChatMessage> messages, CancellationToken ct = default)
    {
        if (_memoryOptions is not { AutoPersist: true } || _chatClientFactory == null)
        {
            return;
        }

        // 互斥：如果对话中已手动调用 save_memory，跳过自动沉淀
        if (HasSaveMemoryToolCall(messages))
        {
            _logger.LogDebug("Skipping auto-persist for scope {Scope}: save_memory was called in conversation", _scope.Name);
            return;
        }

        // 节流：检查自上次自动提取以来的用户消息轮数
        var userTurnCount = CountUserMessages(messages);
        var minTurns = _memoryOptions.MinTurnsBetweenExtractions;
        var turnsSinceLastExtract = userTurnCount - Volatile.Read(ref _lastAutoExtractTurn);
        if (turnsSinceLastExtract < minTurns)
        {
            _logger.LogDebug("Skipping auto-persist for scope {Scope}: only {Turns} turns since last extraction (min: {Min})",
                _scope.Name, turnsSinceLastExtract, minTurns);
            return;
        }

        try
        {
            await AutoPersistAsync(messages, ct);
            Volatile.Write(ref _lastAutoExtractTurn, userTurnCount);
        }
        catch (Exception ex)
        {
            // 自动沉淀失败不影响主流程
            _logger.LogWarning(ex, "Auto-persist failed for scope {Scope}", _scope.Name);
        }
    }

    /// <summary>
    /// 检查对话消息中是否包含 save_memory 工具调用
    /// </summary>
    private static bool HasSaveMemoryToolCall(List<ChatMessage> messages)
    {
        foreach (var message in messages)
        {
            foreach (var content in message.Contents)
            {
                if (content is FunctionCallContent fc && fc.Name == "save_memory")
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// 统计用户消息轮数
    /// </summary>
    private static int CountUserMessages(List<ChatMessage> messages)
    {
        return messages.Count(m => m.Role == ChatRole.User);
    }

    /// <summary>
    /// 自动记忆沉淀 - 从对话中提取持久记忆并追加
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
            // Auxiliary sub-run: uses IChatClientFactory.GetChatClient() which returns the default
            // provider/model. This is intentional - AutoPersist is a lightweight utility call that
            // does not need the parent agent's model. Prompt cache sharing is achieved through
            // IChatClientFactory reuse (same provider pipeline and connection pooling).
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
    /// 增量持久化 - 逐条与已有记忆比对，决定 ADD/UPDATE/DELETE/NOOP
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
                NumberStyles.Float,
                CultureInfo.InvariantCulture, out var imp) ? imp : 0.5;
            var content = match.Groups[3].Value;
            return (content, category, Math.Clamp(importance, 0, 1));
        }
        return (cleaned, null, 0.5);
    }

    /// <summary>
    /// 尝试合并去重 - 当条目数超过阈值时触发
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
        ONLY save information that cannot be derived from the code, git history, or project files.
        DO NOT save: code patterns, file paths, project structure, debugging solutions, git history summaries.
        Return each memory as a concise bullet point, one per line.
        If nothing is worth remembering, return "NONE".
        """;

    private const string IncrementalAutoPersistPrompt = """
        Extract any durable, actionable information from the following conversation that should be remembered across sessions.
        Focus on: user preferences, confirmed facts, key decisions, recurring patterns.
        Exclude: one-time requests, transient information, sensitive data.
        ONLY save information that cannot be derived from the code, git history, or project files.
        DO NOT save: code patterns, file paths, project structure, debugging solutions, git history summaries.
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

    private sealed record ScopedMemorySegment(
        string Label,
        string ScopeKey,
        MemoryScope? LocalScope,
        string Content);
}
