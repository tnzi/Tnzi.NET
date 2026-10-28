namespace Tnzi.AI.Tools.BuiltIn;

/// <summary>
/// 记忆工具 - 提供跨会话记忆的主动保存、搜索、更新和删除能力
/// </summary>
[AIToolGroup("memory", "Memory Tools", "Save, search, update and manage persistent memories across conversations")]
public partial class MemoryTools : IAIToolProvider
{
    private readonly IMemoryStore _memoryStore;
    private readonly ICurrentUser? _currentUser;
    private readonly IAgentExecutionContextAccessor? _executionContextAccessor;
    private readonly IOptionsMonitor<AIOptions> _aiOptions;
    private readonly ILogger<MemoryTools> _logger;

    private MemoryOptions CurrentMemoryOptions => _aiOptions.CurrentValue.ContextProviders.Memory;

    public MemoryTools(
        IMemoryStore memoryStore,
        ILogger<MemoryTools> logger,
        IOptionsMonitor<AIOptions> aiOptions,
        ICurrentUser? currentUser = null,
        IAgentExecutionContextAccessor? executionContextAccessor = null)
    {
        _memoryStore = Check.NotNull(memoryStore);
        _logger = Check.NotNull(logger);
        _aiOptions = Check.NotNull(aiOptions);
        _currentUser = currentUser;
        _executionContextAccessor = executionContextAccessor;
    }

    [AIFunction("save_memory",
        """
        Save important information to persistent memory for future conversations.
        Use this when the user explicitly asks you to remember something, or when you encounter critical facts worth preserving.
        DO NOT SAVE: Personal data (PII), conversation snippets, temporary context, or speculative information.
        Set importance (0-1): 1.0 for critical rules, 0.7 for useful facts, 0.3 for nice-to-know.
        """,
        SearchHint = "remember save persist memory")]
    public async Task<object> SaveMemoryAsync(
        [AIParameter("content", "The information to remember", true)]
        string content,
        [AIParameter("category", "Memory category (e.g. preference, fact, decision, pattern, instruction)", false)]
        string? category = null,
        [AIParameter("importance", "Importance score from 0.0 (trivial) to 1.0 (critical), default 0.7", false)]
        double importance = 0.7,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content))
            return new { status = "error", message = "Content cannot be empty" };

        // PII protection
        if (CurrentMemoryOptions.EnablePiiProtection && ContainsPiiPattern(content))
            return new { status = "rejected", message = "Cannot save content that appears to contain personal information (SIN, phone, email, bank account)" };

        // Category validation (only when ValidCategories is configured)
        if (category != null && CurrentMemoryOptions.ValidCategories.Count > 0 && !CurrentMemoryOptions.ValidCategories.Contains(category))
            return new { status = "error", message = $"Invalid category '{category}'. Valid categories: {string.Join(", ", CurrentMemoryOptions.ValidCategories)}" };

        if (!TryBuildScope(out var scope))
            return OwnerUnavailable();

        try
        {
            importance = Math.Clamp(importance, 0, 1);

            await _memoryStore.AppendAsync(scope, content, importance, category, ct);

            // 告诉轮后的自动沉淀：这一轮模型已经亲手存过记忆了（见 MemoryContextProvider 的互斥）
            if (_executionContextAccessor != null)
            {
                _executionContextAccessor.Properties[ContextPropertyKeys.MemorySavedThisRun] = true;
            }

            _logger.LogDebug("Memory saved via tool: {Content} (category={Category}, importance={Importance})",
                content.FitToLength(50), category, importance);

            return new { status = "saved", scope = scope.Name, importance = Math.Round(importance, 1), category = category ?? "none" };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save memory via tool");
            return new { status = "error", message = $"Failed to save memory: {ex.Message}" };
        }
    }

    [AIFunction("search_memory",
        """
        Search persistent memories by keyword or semantic query.
        Use this to recall information from previous conversations. Check memory BEFORE asking the user about known facts.
        Optional: filter by category for more precise retrieval.
        """,
        // 不标 IsConcurrencySafe：检索走请求作用域的 DbContext，同一批的两个调用并行会撞上 EF 的并发检测
        IsReadOnly = true, SearchHint = "recall find remember search memory")]
    public async Task<object> SearchMemoryAsync(
        [AIParameter("query", "Search query (keyword or natural language)", true)]
        string query,
        [AIParameter("category", "Filter by category (optional)", false)]
        string? category = null,
        [AIParameter("max_results", "Maximum number of results to return (default 5)", false)]
        int maxResults = 5,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new { results = Array.Empty<object>(), message = "Empty query" };

        if (!TryBuildScope(out var scope))
            return new { results = Array.Empty<object>(), count = 0, message = OwnerUnavailableMessage };

        try
        {
            maxResults = Math.Clamp(maxResults, 1, 20);

            // Search local, project snapshot, and shared memories.
            // 必须顺序执行：默认 IMemoryStore 是 DatabaseMemoryStore，三次检索共用同一个
            // 请求作用域的 DbContext，并发会抛 "A second operation was started on this
            // context instance"（同 SuggestionService 记录的坑）。
            var localScopeKey = scope.ToScopeKey();

            // 1. Local user/agent-scoped search
            var localResults = !string.IsNullOrEmpty(category)
                ? await _memoryStore.SearchByCategoryAsync(localScopeKey, query, category, maxResults, ct)
                : await _memoryStore.SearchAsync(scope, query, maxResults, ct);

            // 2. Project snapshot search (shared per project, read-only)
            var projectSnapshotScope = MemoryScopeResolver.ResolveProjectSnapshotScope(
                CurrentMemoryOptions.EnableProjectSnapshot,
                CurrentMemoryOptions.ProjectSnapshotScopePrefix,
                _executionContextAccessor?.CurrentRequest?.Metadata);
            var shouldSearchProject = !string.IsNullOrWhiteSpace(projectSnapshotScope)
                && !string.Equals(projectSnapshotScope, localScopeKey, StringComparison.OrdinalIgnoreCase);

            IReadOnlyList<MemorySearchResult> projectResults = [];
            if (shouldSearchProject)
            {
                projectResults = !string.IsNullOrEmpty(category)
                    ? await _memoryStore.SearchByCategoryAsync(projectSnapshotScope!, query, category, maxResults, ct)
                    : await _memoryStore.SearchAsync(projectSnapshotScope!, query, maxResults, ct);
            }

            // 3. Shared-scope search (if configured)
            var sharedScope = CurrentMemoryOptions.SharedScope;
            var shouldSearchShared = !string.IsNullOrEmpty(sharedScope)
                && !string.Equals(sharedScope, localScopeKey, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(sharedScope, projectSnapshotScope, StringComparison.OrdinalIgnoreCase);

            IReadOnlyList<MemorySearchResult> sharedResults = [];
            if (shouldSearchShared)
            {
                sharedResults = !string.IsNullOrEmpty(category)
                    ? await _memoryStore.SearchByCategoryAsync(sharedScope!, query, category, maxResults, ct)
                    : await _memoryStore.SearchAsync(sharedScope!, query, maxResults, ct);
            }

            var allResults = new List<MemorySearchResult>();
            allResults.AddRange(localResults);

            foreach (var result in projectResults)
            {
                result.Source ??= projectSnapshotScope;
            }
            allResults.AddRange(projectResults);

            foreach (var result in sharedResults)
            {
                result.Source ??= sharedScope;
            }
            allResults.AddRange(sharedResults);

            // Deduplicate by Id, sort by score, take top maxResults
            var results = allResults
                .GroupBy(GetDeduplicationKey)
                .Select(g => g.OrderByDescending(r => r.Score).First())
                .OrderByDescending(r => r.Score)
                .Take(maxResults)
                .ToList();

            if (results.Count == 0)
                return new { results = Array.Empty<object>(), count = 0, message = $"No memories found for: {query}" };

            return new
            {
                results = results.Select(r => new
                {
                    id = r.Id?.ToString("N"),
                    content = r.Content,
                    category = r.Category,
                    source = r.Source,
                    relevance = Math.Round(r.Score, 2)
                }),
                count = results.Count
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to search memory via tool");
            return new { results = Array.Empty<object>(), count = 0, message = $"Search failed: {ex.Message}" };
        }
    }

    [AIFunction("update_memory", "Update the content of an existing memory entry. Use search_memory first to find the ID.",
        SearchHint = "update modify memory")]
    public async Task<object> UpdateMemoryAsync(
        [AIParameter("entry_id", "The memory entry ID (from search_memory results)", true)]
        string entryId,
        [AIParameter("new_content", "The updated content to replace the existing memory", true)]
        string newContent,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(entryId, out var id))
            return new { status = "error", message = "Invalid entry ID format. Expected a GUID from search_memory results." };

        if (string.IsNullOrWhiteSpace(newContent))
            return new { status = "error", message = "New content cannot be empty" };

        if (CurrentMemoryOptions.EnablePiiProtection && ContainsPiiPattern(newContent))
            return new { status = "rejected", message = "Cannot save content that appears to contain personal information" };

        if (!TryBuildScope(out var scope))
            return OwnerUnavailable();

        try
        {
            await _memoryStore.UpdateEntryAsync(scope.ToScopeKey(), id, newContent, ct);

            _logger.LogDebug("Memory updated via tool: {EntryId}", id);
            return new { status = "updated", entry_id = id.ToString("N") };
        }
        catch (ResourceNotFoundException)
        {
            return EntryNotInWritableScope(id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update memory via tool");
            return new { status = "error", message = $"Failed to update memory: {ex.Message}" };
        }
    }

    [AIFunction("delete_memory", "Delete a specific memory entry by its ID. Use search_memory first to find the ID.",
        IsDestructive = true, SearchHint = "remove delete forget memory")]
    public async Task<object> DeleteMemoryAsync(
        [AIParameter("entry_id", "The memory entry ID (from search_memory results)", true)]
        string entryId,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(entryId, out var id))
            return new { status = "error", message = "Invalid entry ID format. Expected a GUID from search_memory results." };

        if (!TryBuildScope(out var scope))
            return OwnerUnavailable();

        try
        {
            await _memoryStore.DeleteEntryAsync(scope.ToScopeKey(), id, ct);

            _logger.LogDebug("Memory deleted via tool: {EntryId}", id);
            return new { status = "deleted", entry_id = id.ToString("N") };
        }
        catch (ResourceNotFoundException)
        {
            return EntryNotInWritableScope(id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete memory via tool");
            return new { status = "error", message = $"Failed to delete memory: {ex.Message}" };
        }
    }

    /// <summary>
    /// 条目不存在，或来自本工具改不了的 scope：search_memory 也返回共享 scope 与项目快照 scope 的条目，而更新 / 删除只作用于本地 scope。
    /// </summary>
    private static object EntryNotInWritableScope(Guid id) => new
    {
        status = "not_found",
        entry_id = id.ToString("N"),
        message = "No memory entry with this ID exists in the memory scope this tool can change. Shared and project memories returned by search_memory cannot be updated or deleted here.",
    };

    private const string OwnerUnavailableMessage = "Memory is not available because this conversation has no user identity.";

    private static object OwnerUnavailable() => new { status = "error", message = OwnerUnavailableMessage };

    /// <summary>
    /// 本次运行的本地记忆范围；开启用户隔离却解析不到用户时返回 false（调用方拒绝读写）。
    /// </summary>
    /// <remarks>
    /// 归属人取<b>运行发起人</b>（请求的 UserId），只在没有运行上下文时回退到登录用户：管理员续跑用户 X 的运行时，
    /// 作用域里的登录用户是管理员，按它取会把 X 的记忆写进管理员名下、把管理员的记忆读给 X。
    /// 开启隔离而谁都解析不出来时（如外部渠道的匿名会话），旧行为是落进所有无用户运行共用的 default 范围，
    /// 一个人说的「记住我的地址」会被另一个人检索到；隔离开着就不该有这个共享范围，所以拒绝。
    /// </remarks>
    private bool TryBuildScope(out MemoryScope scope)
    {
        var options = CurrentMemoryOptions;
        var userId = _executionContextAccessor?.CurrentRequest?.UserId ?? _currentUser?.Id;

        scope = MemoryScopeResolver.BuildLocalScope(
            options.DefaultScope,
            options.EnableUserIsolation,
            options.EnableAgentIsolation,
            userId,
            _executionContextAccessor?.CurrentRequest?.AgentId);

        if (options.EnableUserIsolation && userId == null)
        {
            _logger.LogWarning("Memory tool call refused: user isolation is enabled but the run has no user identity");
            return false;
        }

        return true;
    }

    private static string GetDeduplicationKey(MemorySearchResult result)
    {
        if (result.Id.HasValue)
        {
            return result.Id.Value.ToString("N");
        }

        return $"{result.Source ?? "memory"}::{result.Category ?? "general"}::{result.Content}";
    }

    /// <summary>
    /// Basic PII pattern detection to prevent accidental storage of personal data.
    /// Not meant to be comprehensive - just an additional safety layer.
    /// </summary>
    private static bool ContainsPiiPattern(string content)
    {
        // SIN format: 3-3-3 digits
        if (SsnPatternRegex().IsMatch(content))
            return true;

        // Bank account: 7+ consecutive digits
        if (LongNumberRegex().IsMatch(content))
            return true;

        // Email address
        if (EmailPatternRegex().IsMatch(content))
            return true;

        // North American phone: (xxx) xxx-xxxx or xxx-xxx-xxxx
        if (PhonePatternRegex().IsMatch(content))
            return true;

        return false;
    }

    [GeneratedRegex(@"\b\d{3}[-\s]\d{3}[-\s]\d{3}\b")]
    private static partial Regex SsnPatternRegex();

    [GeneratedRegex(@"\b\d{7,}\b")]
    private static partial Regex LongNumberRegex();

    [GeneratedRegex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b")]
    private static partial Regex EmailPatternRegex();

    [GeneratedRegex(@"(\+?1[-.\s]?)?\(?\d{3}\)?[-.\s]\d{3}[-.\s]\d{4}\b")]
    private static partial Regex PhonePatternRegex();
}
