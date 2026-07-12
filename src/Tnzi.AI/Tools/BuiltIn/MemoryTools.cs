namespace Tnzi.AI.Tools.BuiltIn;

/// <summary>
/// 记忆工具 — 提供跨会话记忆的主动保存、搜索、更新和删除能力
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

        try
        {
            var scope = BuildScope();
            importance = Math.Clamp(importance, 0, 1);

            await _memoryStore.AppendAsync(scope, content, importance, category, ct);

            _logger.LogDebug("Memory saved via tool: {Content} (category={Category}, importance={Importance})",
                content[..Math.Min(50, content.Length)], category, importance);

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
        IsReadOnly = true, IsConcurrencySafe = true, SearchHint = "recall find remember search memory")]
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

        try
        {
            var scope = BuildScope();
            maxResults = Math.Clamp(maxResults, 1, 20);

            // Search local, project snapshot, and shared memories in parallel.
            var localScopeKey = scope.ToScopeKey();

            // 1. Local user/agent-scoped search
            var localTask = !string.IsNullOrEmpty(category)
                ? _memoryStore.SearchByCategoryAsync(localScopeKey, query, category, maxResults, ct)
                : _memoryStore.SearchAsync(scope, query, maxResults, ct);

            // 2. Project snapshot search (shared per project, read-only)
            var projectSnapshotScope = MemoryScopeResolver.ResolveProjectSnapshotScope(
                CurrentMemoryOptions.EnableProjectSnapshot,
                CurrentMemoryOptions.ProjectSnapshotScopePrefix,
                _executionContextAccessor?.CurrentRequest?.Metadata);
            var shouldSearchProject = !string.IsNullOrWhiteSpace(projectSnapshotScope)
                && !string.Equals(projectSnapshotScope, localScopeKey, StringComparison.OrdinalIgnoreCase);

            var projectTask = shouldSearchProject
                ? (!string.IsNullOrEmpty(category)
                    ? _memoryStore.SearchByCategoryAsync(projectSnapshotScope!, query, category, maxResults, ct)
                    : _memoryStore.SearchAsync(projectSnapshotScope!, query, maxResults, ct))
                : Task.FromResult<IReadOnlyList<MemorySearchResult>>([]);

            // 3. Shared-scope search (if configured)
            var sharedScope = CurrentMemoryOptions.SharedScope;
            var shouldSearchShared = !string.IsNullOrEmpty(sharedScope)
                && !string.Equals(sharedScope, localScopeKey, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(sharedScope, projectSnapshotScope, StringComparison.OrdinalIgnoreCase);

            var sharedTask = shouldSearchShared
                ? (!string.IsNullOrEmpty(category)
                    ? _memoryStore.SearchByCategoryAsync(sharedScope!, query, category, maxResults, ct)
                    : _memoryStore.SearchAsync(sharedScope!, query, maxResults, ct))
                : Task.FromResult<IReadOnlyList<MemorySearchResult>>([]);

            await Task.WhenAll(localTask, projectTask, sharedTask);

            var allResults = new List<MemorySearchResult>();
            allResults.AddRange(localTask.Result);

            if (shouldSearchProject)
            {
                foreach (var result in projectTask.Result)
                {
                    result.Source ??= projectSnapshotScope;
                }
                allResults.AddRange(projectTask.Result);
            }

            if (shouldSearchShared)
            {
                foreach (var result in sharedTask.Result)
                {
                    result.Source ??= sharedScope;
                }
                allResults.AddRange(sharedTask.Result);
            }

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

        try
        {
            var scope = BuildScope();
            await _memoryStore.UpdateEntryAsync(scope.ToScopeKey(), id, newContent, ct);

            _logger.LogDebug("Memory updated via tool: {EntryId}", id);
            return new { status = "updated", entry_id = id.ToString("N") };
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

        try
        {
            var scope = BuildScope();
            await _memoryStore.DeleteEntryAsync(scope.ToScopeKey(), id, ct);

            _logger.LogDebug("Memory deleted via tool: {EntryId}", id);
            return new { status = "deleted", entry_id = id.ToString("N") };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete memory via tool");
            return new { status = "error", message = $"Failed to delete memory: {ex.Message}" };
        }
    }

    private MemoryScope BuildScope()
    {
        return MemoryScopeResolver.BuildLocalScope(
            CurrentMemoryOptions.DefaultScope,
            CurrentMemoryOptions.EnableUserIsolation,
            CurrentMemoryOptions.EnableAgentIsolation,
            _currentUser?.Id,
            _executionContextAccessor?.CurrentRequest?.AgentId);
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
    /// Not meant to be comprehensive — just an additional safety layer.
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
