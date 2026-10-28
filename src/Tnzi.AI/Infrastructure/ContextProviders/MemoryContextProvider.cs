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
    private readonly IAgentThreadInternalService? _threadService;
    private readonly Guid? _threadId;
    private readonly ILogger<MemoryContextProvider> _logger;

    /// <summary>
    /// 没有线程可记账时（直接构造、自定义管线）的节流基准：上次提取时消息里的用户轮数。
    /// 框架按请求新建实例，跨请求的节流靠线程元数据（<see cref="PendingTurnsMetadataKey"/>）。
    /// </summary>
    private int _lastAutoExtractTurn;

    /// <summary>
    /// <c>AgentThread.Metadata</c> 里记录「自上次自动提取以来已完成、尚未提取的轮数」的键。
    /// </summary>
    public const string PendingTurnsMetadataKey = "memoryAutoPersistPendingTurns";

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
    /// <param name="memoryStore">记忆存储</param>
    /// <param name="scope">当前请求的记忆范围（用户/Agent/会话隔离键）</param>
    /// <param name="logger">日志</param>
    /// <param name="chatClientFactory">可选 ChatClient 工厂，用于 LLM 提炼记忆</param>
    /// <param name="memoryOptions">可选记忆配置；为空时用默认值</param>
    /// <param name="memoryConsolidator">可选记忆合并器（ADD/UPDATE/DELETE/NOOP 增量决策）</param>
    /// <param name="executionContextAccessor">可选执行上下文访问器</param>
    /// <param name="agentBoundScope">
    /// 可选的 Agent-bound 记忆范围 - 绑定到当前 Agent（通过结构化 AgentId 列检索），
    /// 与当前用户无关，确保 headless 运行也能加载。为只读注入，不参与自动沉淀。
    /// </param>
    /// <param name="threadService">可选线程服务：自动沉淀的节流计数记在线程元数据里，跨请求有效</param>
    /// <param name="threadId">本轮所在线程（与 <paramref name="threadService"/> 一起给出才按线程节流）</param>
    public MemoryContextProvider(
        IMemoryStore memoryStore,
        MemoryScope scope,
        ILogger<MemoryContextProvider> logger,
        IChatClientFactory? chatClientFactory = null,
        MemoryOptions? memoryOptions = null,
        IMemoryConsolidator? memoryConsolidator = null,
        IAgentExecutionContextAccessor? executionContextAccessor = null,
        MemoryScope? agentBoundScope = null,
        IAgentThreadInternalService? threadService = null,
        Guid? threadId = null)
    {
        _memoryStore = Check.NotNull(memoryStore);
        _scope = Check.NotNull(scope);
        _logger = Check.NotNull(logger);
        _chatClientFactory = chatClientFactory;
        _memoryOptions = memoryOptions;
        _memoryConsolidator = memoryConsolidator;
        _executionContextAccessor = executionContextAccessor;
        _threadService = threadService;
        _threadId = threadId;
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
            var preview = lines[i].Truncate(80);
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
    /// <remarks>
    /// <para>
    /// 节流是<b>攒批</b>而不是抽样：每完成一轮记一笔，攒够 <see cref="MemoryOptions.MinTurnsBetweenExtractions"/> 轮
    /// 才调一次提炼，提炼的是这几轮的全部对话。只取最后一问一答会让两次提取之间的那几轮永远进不了记忆。
    /// 代价是对话停下时还没攒够的尾巴，要等这段对话继续才会提炼（要求每轮都提炼就把该值设为 1）。
    /// </para>
    /// <para>
    /// 计数记在线程元数据（<see cref="PendingTurnsMetadataKey"/>）里：框架按请求新建本实例，实例字段到下一轮就没了，
    /// 按实例计数的节流从来不起作用。没有线程可记账时（直接构造、自定义管线）退回按实例、按消息里的用户轮数计。
    /// </para>
    /// <para>
    /// 与 <c>save_memory</c> 互斥：本次运行里模型已经亲手存过记忆，就不再自动提炼，计数从这里重新开始。
    /// 判据是运行属性包里的标记（<see cref="ContextPropertyKeys.MemorySavedThisRun"/>）：交给本方法的历史是从纯文本重建的，
    /// 里面没有任何工具调用，按消息扫描工具调用在框架管线里永远扫不到。
    /// </para>
    /// </remarks>
    public async Task OnCompletedAsync(List<ChatMessage> messages, CancellationToken ct = default)
    {
        if (_memoryOptions is not { AutoPersist: true } || _chatClientFactory == null)
        {
            return;
        }

        var userTurnCount = CountUserMessages(messages);

        try
        {
            // 互斥：本轮已手动调用 save_memory，跳过自动沉淀
            if (SavedMemoryThisRun() || HasSaveMemoryToolCall(messages))
            {
                _logger.LogDebug("Skipping auto-persist for scope {Scope}: save_memory was called in conversation", _scope.Name);
                await MarkExtractedAsync(userTurnCount, ct);
                return;
            }

            // 节流：攒够轮数才提炼
            var pendingTurns = await CountPendingTurnsAsync(userTurnCount, ct);
            var minTurns = _memoryOptions.MinTurnsBetweenExtractions;
            if (pendingTurns < minTurns)
            {
                await SavePendingTurnsAsync(pendingTurns, ct);
                _logger.LogDebug("Skipping auto-persist for scope {Scope}: only {Turns} turns since last extraction (min: {Min})",
                    _scope.Name, pendingTurns, minTurns);
                return;
            }

            await AutoPersistAsync(messages, Math.Max(1, pendingTurns), ct);
            await MarkExtractedAsync(userTurnCount, ct);
        }
        catch (Exception ex)
        {
            // 自动沉淀失败不影响主流程
            _logger.LogWarning(ex, "Auto-persist failed for scope {Scope}", _scope.Name);
        }
    }

    /// <summary>本次运行里 save_memory 是否已经成功写入过记忆</summary>
    private bool SavedMemoryThisRun()
        => _executionContextAccessor != null
           && _executionContextAccessor.Properties.TryGetValue(ContextPropertyKeys.MemorySavedThisRun, out var saved)
           && saved is true;

    /// <summary>是否按线程记账（框架管线里总是；直接构造且未给线程时不是）</summary>
    private bool TracksTurnsOnThread => _threadService != null && _threadId.HasValue;

    /// <summary>自上次提取以来已完成、尚未提取的轮数（含刚完成的这一轮）</summary>
    private async Task<int> CountPendingTurnsAsync(int userTurnCount, CancellationToken ct)
    {
        if (!TracksTurnsOnThread)
        {
            return userTurnCount - Volatile.Read(ref _lastAutoExtractTurn);
        }

        var recordedJson = await _threadService!.GetMetadataValueAsync(_threadId!.Value, PendingTurnsMetadataKey, ct);
        var recorded = int.TryParse(recordedJson, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : 0;
        return recorded + 1;
    }

    private Task SavePendingTurnsAsync(int pendingTurns, CancellationToken ct)
        => TracksTurnsOnThread
            ? _threadService!.SetMetadataValueAsync(_threadId!.Value, PendingTurnsMetadataKey, pendingTurns.ToString(CultureInfo.InvariantCulture), ct)
            : Task.CompletedTask;

    /// <summary>记下一个提取点：之后的轮数从零开始数</summary>
    private async Task MarkExtractedAsync(int userTurnCount, CancellationToken ct)
    {
        if (TracksTurnsOnThread)
        {
            await _threadService!.SetMetadataValueAsync(_threadId!.Value, PendingTurnsMetadataKey, null, ct);
            return;
        }

        Volatile.Write(ref _lastAutoExtractTurn, userTurnCount);
    }

    /// <summary>
    /// 检查对话消息中是否包含 save_memory 工具调用（调用方交来带工具调用的完整消息时有效）
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
    /// 从完成的对话里取最近 <paramref name="turns"/> 轮（一轮 = 一条用户消息及其后的助手回复），拼成提炼输入。
    /// 系统消息（注入的记忆、检索结果）不在其中；窗口里没有任何助手回复时返回 null（没有可提炼的一问一答）。
    /// </summary>
    internal static string? BuildConversationSnippet(IReadOnlyList<ChatMessage> messages, int turns)
    {
        var dialogue = messages
            .Where(m => (m.Role == ChatRole.User || m.Role == ChatRole.Assistant) && !string.IsNullOrWhiteSpace(m.Text))
            .ToList();

        var start = dialogue.Count;
        var userTurnsSeen = 0;
        for (var i = dialogue.Count - 1; i >= 0 && userTurnsSeen < turns; i--)
        {
            if (dialogue[i].Role == ChatRole.User)
            {
                userTurnsSeen++;
                start = i;
            }
        }

        var window = dialogue.Skip(start).ToList();
        if (userTurnsSeen == 0 || !window.Any(m => m.Role == ChatRole.Assistant))
        {
            return null;
        }

        return string.Join("\n", window.Select(m => $"{(m.Role == ChatRole.User ? "User" : "Assistant")}: {m.Text}"));
    }

    /// <summary>
    /// 自动记忆沉淀 - 从最近 <paramref name="turns"/> 轮对话中提取持久记忆并追加
    /// </summary>
    private async Task AutoPersistAsync(List<ChatMessage> messages, int turns, CancellationToken ct)
    {
        var conversationSnippet = BuildConversationSnippet(messages, turns);
        if (conversationSnippet == null)
        {
            return;
        }

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
            var incremental = UsesIncrementalConsolidation;
            if (incremental)
            {
                await IncrementalPersistAsync(extracted, ct);
            }
            else
            {
                await _memoryStore.AppendAsync(_scope, extracted, ct);
            }

            _logger.LogDebug("Auto-persisted memory for scope {Scope}: {Length} chars", _scope.Name, extracted.Length);

            // 可选: 合并去重。增量合并已经逐条去重，整段重写只在没有增量合并时进行：
            // 两者同时开着，重写会把带 Importance / Category 的逐条记忆压成一整段文本，
            // 之后增量合并的 UPDATE / DELETE 命中的就是这一整段（一次「矛盾」判定即删光全部记忆）。
            if (_memoryOptions.AutoConsolidate && !incremental)
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
    /// 是否走增量合并（逐条 ADD / UPDATE / DELETE / NOOP）：开关打开且注册了合并器
    /// </summary>
    private bool UsesIncrementalConsolidation
        => _memoryOptions is { IncrementalConsolidate: true } && _memoryConsolidator != null;

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
                        // DELETE = 新记忆与旧记忆矛盾：旧的作废，新的才是现在的事实，必须写进去，
                        // 否则「搬到了温哥华」把「住在多伦多」删掉之后，用户的住址一条都不剩
                        await _memoryStore.DeleteEntryAsync(_scope.ToScopeKey(), result.TargetEntryId.Value, ct);
                        await _memoryStore.AppendAsync(_scope, memoryContent, importance, category, ct);
                        break;
                    case MemoryAction.Noop:
                        _logger.LogDebug("Memory consolidation decided NOOP for: {Memory}", memoryContent.FitToLength(50));
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

            // 整段重写会先删光这个范围的全部条目：结果不像一份合并后的记忆就保留原样，宁可不合并也不丢数据
            var rejection = ValidateConsolidation(consolidated, lineCount, response.FinishReason);
            if (rejection != null)
            {
                _logger.LogWarning("Discarded memory consolidation for scope {Scope} ({Reason}); existing entries kept",
                    _scope.Name, rejection);
                return;
            }

            await _memoryStore.WriteAsync(_scope, consolidated!, ct);
            _logger.LogDebug("Consolidated memory for scope {Scope}: {Before} lines -> {After} chars",
                _scope.Name, lineCount, consolidated!.Length);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Memory consolidation failed for scope {Scope}", _scope.Name);
        }
    }

    /// <summary>
    /// 合并结果至少保留输入行数的这个比例：低于它更像是模型丢了大半记忆，而不是去重。
    /// </summary>
    private const double MinConsolidationRetentionRatio = 0.2;

    /// <summary>
    /// 校验整段合并的结果；可以替换原有条目时返回 null，否则返回拒绝原因。
    /// </summary>
    /// <remarks>
    /// 提示词要求「每行一条要点」。不接受：空结果、被截断的输出、带开场白或说明文字的输出（有不是要点的行）、
    /// 比原来还多的行，以及只剩原来很小一部分的结果。
    /// </remarks>
    internal static string? ValidateConsolidation(string? consolidated, int originalLineCount, ChatFinishReason? finishReason)
    {
        if (string.IsNullOrWhiteSpace(consolidated))
        {
            return "empty result";
        }

        if (finishReason == ChatFinishReason.Length)
        {
            return "output was truncated";
        }

        var lines = consolidated.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
        {
            return "empty result";
        }

        if (lines.Any(line => !IsBulletLine(line)))
        {
            return "result contains lines that are not memory bullets";
        }

        if (lines.Length > originalLineCount)
        {
            return "result has more lines than the original";
        }

        var minimumLines = Math.Max(1, (int)Math.Ceiling(originalLineCount * MinConsolidationRetentionRatio));
        if (lines.Length < minimumLines)
        {
            return $"result keeps {lines.Length} of {originalLineCount} lines";
        }

        return null;
    }

    private static bool IsBulletLine(string line)
    {
        if (line.StartsWith('-') || line.StartsWith('*') || line.StartsWith('\u2022'))
        {
            return true;
        }

        // 编号列表：1. / 1)
        var digits = 0;
        while (digits < line.Length && char.IsDigit(line[digits]))
        {
            digits++;
        }

        return digits > 0 && digits < line.Length && (line[digits] == '.' || line[digits] == ')');
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
