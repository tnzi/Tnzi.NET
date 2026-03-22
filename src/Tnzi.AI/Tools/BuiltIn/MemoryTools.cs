namespace Tnzi.AI.Tools.BuiltIn;

/// <summary>
/// 记忆工具 — 提供跨会话记忆的主动保存、搜索、更新和删除能力
/// </summary>
[AIToolGroup("memory", "Memory Tools", "Save, search, update and manage persistent memories across conversations")]
public partial class MemoryTools : IAIToolProvider
{
    private readonly IMemoryStore _memoryStore;
    private readonly ICurrentUser? _currentUser;
    private readonly MemoryOptions _memoryOptions;
    private readonly ILogger<MemoryTools> _logger;

    public MemoryTools(
        IMemoryStore memoryStore,
        ILogger<MemoryTools> logger,
        IOptions<AIOptions> aiOptions,
        ICurrentUser? currentUser = null)
    {
        _memoryStore = Check.NotNull(memoryStore);
        _logger = Check.NotNull(logger);
        _memoryOptions = Check.NotNull(aiOptions).Value.ContextProviders.Memory;
        _currentUser = currentUser;
    }

    [AIFunction("save_memory",
        """
        Save important information to persistent memory for future conversations.
        Use this when the user explicitly asks you to remember something, or when you encounter critical facts worth preserving.
        DO NOT SAVE: Personal data (PII), conversation snippets, temporary context, or speculative information.
        Set importance (0-1): 1.0 for critical rules, 0.7 for useful facts, 0.3 for nice-to-know.
        """)]
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
        if (_memoryOptions.EnablePiiProtection && ContainsPiiPattern(content))
            return new { status = "rejected", message = "Cannot save content that appears to contain personal information (SIN, phone, email, bank account)" };

        // Category validation (only when ValidCategories is configured)
        if (category != null && _memoryOptions.ValidCategories.Count > 0 && !_memoryOptions.ValidCategories.Contains(category))
            return new { status = "error", message = $"Invalid category '{category}'. Valid categories: {string.Join(", ", _memoryOptions.ValidCategories)}" };

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
        """)]
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

            // Search both user-scoped and shared-scope memories
            var allResults = new List<MemorySearchResult>();

            // 1. User-scoped search
            if (!string.IsNullOrEmpty(category))
                allResults.AddRange(await _memoryStore.SearchByCategoryAsync(scope.ToScopeKey(), query, category, maxResults, ct));
            else
                allResults.AddRange(await _memoryStore.SearchAsync(scope, query, maxResults, ct));

            // 2. Shared-scope search (if configured)
            var sharedScope = _memoryOptions.SharedScope;
            if (!string.IsNullOrEmpty(sharedScope) && sharedScope != scope.ToScopeKey())
            {
                var sharedResults = !string.IsNullOrEmpty(category)
                    ? await _memoryStore.SearchByCategoryAsync(sharedScope, query, category, maxResults, ct)
                    : await _memoryStore.SearchAsync(sharedScope, query, maxResults, ct);
                allResults.AddRange(sharedResults);
            }

            // Deduplicate by Id, sort by score, take top maxResults
            var results = allResults
                .GroupBy(r => r.Id)
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

    [AIFunction("update_memory", "Update the content of an existing memory entry. Use search_memory first to find the ID.")]
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

        if (_memoryOptions.EnablePiiProtection && ContainsPiiPattern(newContent))
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

    [AIFunction("delete_memory", "Delete a specific memory entry by its ID. Use search_memory first to find the ID.")]
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
        var userId = _memoryOptions.EnableUserIsolation ? _currentUser?.Id : null;
        return new MemoryScope(_memoryOptions.DefaultScope, userId);
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
