namespace Tnzi.AI.Coder.Memory;

/// <summary>
/// 持久记忆工具组 — 读写持久化记忆
/// </summary>
[AIToolGroup("memory", "Persistent Memory", "Read/write persistent memory")]
public class MemoryTools : IAIToolProvider
{
    // Singleton tool wrapper resolves IMemoryStore (potentially Scoped, e.g. DatabaseMemoryStore)
    // and ICurrentUser (Scoped) per tool call via IServiceScopeFactory. This avoids captive
    // dependency when this module is loaded alongside AIModule's DatabaseMemoryStore.
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration? _configuration;
    private readonly ILogger<MemoryTools> _logger;

    public MemoryTools(
        IServiceScopeFactory scopeFactory,
        ILogger<MemoryTools> logger,
        IConfiguration? configuration = null)
    {
        _scopeFactory = Check.NotNull(scopeFactory);
        _logger = Check.NotNull(logger);
        _configuration = configuration;
    }

    /// <summary>
    /// 读取记忆
    /// </summary>
    [AIFunction("memory_read", "Read memory content for a scope", IsReadOnly = true, IsConcurrencySafe = true)]
    public async Task<object> ReadMemoryAsync(
        [AIParameter("scope", "Memory scope (e.g. 'default', 'project-notes')", false)] string scope = "default")
    {
        try
        {
            using var diScope = _scopeFactory.CreateScope();
            var memoryStore = diScope.ServiceProvider.GetRequiredService<IMemoryStore>();
            var memoryScope = BuildScope(scope, diScope.ServiceProvider.GetService<ICurrentUser>());
            var content = await memoryStore.ReadAsync(memoryScope);

            if (content == null)
            {
                _logger.LogDebug("Memory scope '{Scope}' is empty", scope);
                return new { content = (string?)null, scope, exists = false };
            }

            _logger.LogDebug("Read memory scope '{Scope}': {Length} chars", scope, content.Length);

            return new
            {
                content,
                scope,
                exists = true,
                length = content.Length
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read memory scope '{Scope}'", scope);
            return new { error = $"Failed to read memory: {ex.Message}" };
        }
    }

    /// <summary>
    /// 写入记忆（覆盖）
    /// </summary>
    [AIFunction("memory_write", "Write (overwrite) memory content for a scope", SearchHint = "write memory persist")]
    public async Task<object> WriteMemoryAsync(
        [AIParameter("content", "Content to write")] string content,
        [AIParameter("scope", "Memory scope", false)] string scope = "default")
    {
        try
        {
            using var diScope = _scopeFactory.CreateScope();
            var memoryStore = diScope.ServiceProvider.GetRequiredService<IMemoryStore>();
            var memoryScope = BuildScope(scope, diScope.ServiceProvider.GetService<ICurrentUser>());
            await memoryStore.WriteAsync(memoryScope, content);

            _logger.LogDebug("Wrote memory scope '{Scope}': {Length} chars", scope, content.Length);

            return new
            {
                success = true,
                scope,
                length = content.Length
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to write memory scope '{Scope}'", scope);
            return new { error = $"Failed to write memory: {ex.Message}" };
        }
    }

    /// <summary>
    /// 追加记忆条目
    /// </summary>
    [AIFunction("memory_append", "Append an entry to memory", SearchHint = "append memory add")]
    public async Task<object> AppendMemoryAsync(
        [AIParameter("entry", "Entry to append")] string entry,
        [AIParameter("scope", "Memory scope", false)] string scope = "default")
    {
        try
        {
            using var diScope = _scopeFactory.CreateScope();
            var memoryStore = diScope.ServiceProvider.GetRequiredService<IMemoryStore>();
            var memoryScope = BuildScope(scope, diScope.ServiceProvider.GetService<ICurrentUser>());
            await memoryStore.AppendAsync(memoryScope, entry);

            _logger.LogDebug("Appended to memory scope '{Scope}': {Length} chars", scope, entry.Length);

            return new
            {
                success = true,
                scope,
                appended_length = entry.Length
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to append to memory scope '{Scope}'", scope);
            return new { error = $"Failed to append to memory: {ex.Message}" };
        }
    }

    /// <summary>
    /// 推理/思考工具 — 记录推理过程但不执行任何操作
    /// </summary>
    /// <remarks>
    /// 受 OpenHands 启发。显式推理步骤可显著提升多步任务规划质量。
    /// Agent 使用此工具在行动前进行结构化思考（分析 bug、评估方案、规划步骤）。
    /// </remarks>
    [AIFunction("think", "Log reasoning without taking action — use for planning, debugging analysis, tradeoff evaluation before acting", IsReadOnly = true, IsConcurrencySafe = true)]
    public Task<object> ThinkAsync(
        [AIParameter("thought", "The reasoning to record (analysis, plan, evaluation)")] string thought)
    {
        if (string.IsNullOrWhiteSpace(thought))
        {
            return Task.FromResult<object>(new { error = "Thought cannot be empty" });
        }

        _logger.LogDebug("[THINK] {Thought}", thought.Length > 500 ? thought[..500] + "..." : thought);

        return Task.FromResult<object>(new
        {
            logged = true,
            length = thought.Length
        });
    }

    /// <summary>
    /// 搜索记忆
    /// </summary>
    [AIFunction("memory_search", "Search memory by keyword", IsReadOnly = true, IsConcurrencySafe = true)]
    public async Task<object> SearchMemoryAsync(
        [AIParameter("query", "Search query")] string query,
        [AIParameter("scope", "Memory scope to search", false)] string scope = "default",
        [AIParameter("max_results", "Maximum number of results", false)] int maxResults = 10)
    {
        try
        {
            using var diScope = _scopeFactory.CreateScope();
            var memoryStore = diScope.ServiceProvider.GetRequiredService<IMemoryStore>();
            var memoryScope = BuildScope(scope, diScope.ServiceProvider.GetService<ICurrentUser>());
            var results = await memoryStore.SearchAsync(memoryScope, query, maxResults);

            _logger.LogDebug("Searched memory scope '{Scope}' for '{Query}': {Count} results",
                scope, query, results.Count);

            return new
            {
                results = results.Select(r => new
                {
                    content = r.Content,
                    source = r.Source,
                    score = r.Score
                }),
                count = results.Count,
                scope,
                query
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to search memory scope '{Scope}'", scope);
            return new { error = $"Failed to search memory: {ex.Message}" };
        }
    }

    /// <summary>
    /// 构建带用户隔离的 MemoryScope（currentUser 由调用方从 per-call DI scope 解析传入）
    /// </summary>
    private MemoryScope BuildScope(string scope, ICurrentUser? currentUser)
    {
        var enableUserIsolation = false;
        if (bool.TryParse(_configuration?["AI:ContextProviders:Memory:EnableUserIsolation"], out var parsed))
        {
            enableUserIsolation = parsed;
        }
        return new MemoryScope(scope, enableUserIsolation ? currentUser?.Id : null);
    }
}
