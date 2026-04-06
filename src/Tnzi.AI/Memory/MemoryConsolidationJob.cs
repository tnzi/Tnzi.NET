namespace Tnzi.AI.Memory;

/// <summary>
/// 后台记忆整合任务 — 借鉴 Claude Code DreamTask。
/// 门控条件：距上次运行 > 24h 且新增条目 > 10 条。
/// 单次 LLM 批量整合（非 O(N*K) 遍历），通过 IMemoryConsolidator 执行。
/// </summary>
public class MemoryConsolidationJob
{
    private readonly IMemoryStore _memoryStore;
    private readonly IMemoryConsolidator? _memoryConsolidator;
    private readonly MemoryOptions _options;
    private readonly ILogger<MemoryConsolidationJob> _logger;

    private readonly object _gateLock = new();
    private DateTime _lastConsolidationTime = DateTime.MinValue;
    private int _entriesAtLastConsolidation;

    public MemoryConsolidationJob(
        IMemoryStore memoryStore,
        ILogger<MemoryConsolidationJob> logger,
        MemoryOptions options,
        IMemoryConsolidator? memoryConsolidator = null)
    {
        _memoryStore = Check.NotNull(memoryStore);
        _logger = Check.NotNull(logger);
        _options = Check.NotNull(options);
        _memoryConsolidator = memoryConsolidator;
    }

    /// <summary>
    /// 检查门控条件并执行整合
    /// </summary>
    public async Task<bool> TryConsolidateAsync(string scope, CancellationToken ct = default)
    {
        Check.NotNullOrWhiteSpace(scope);

        if (_memoryConsolidator == null)
        {
            _logger.LogDebug("Memory consolidation skipped: consolidator not available");
            return false;
        }

        // Gate 1: 时间间隔（24 小时）
        DateTime lastTime;
        int lastCount;
        lock (_gateLock) { lastTime = _lastConsolidationTime; lastCount = _entriesAtLastConsolidation; }

        var timeSinceLast = DateTime.UtcNow - lastTime;
        if (timeSinceLast < TimeSpan.FromHours(24))
        {
            _logger.LogDebug(
                "Memory consolidation skipped: {Hours:F1}h since last run (min 24h)",
                timeSinceLast.TotalHours);
            return false;
        }

        // Gate 2: 新增条目数（读取当前条目数并与上次比较）
        var currentMemory = await _memoryStore.ReadAsync(scope, ct);
        var entries = SplitEntries(currentMemory);
        var currentCount = entries.Length;
        var newEntries = currentCount - lastCount;

        if (newEntries < 10)
        {
            _logger.LogDebug(
                "Memory consolidation skipped: only {NewEntries} new entries (min 10)",
                newEntries);
            return false;
        }

        try
        {
            _logger.LogInformation(
                "Starting memory consolidation for scope {Scope}: {Count} entries, {New} new",
                scope, currentCount, newEntries);

            var consolidated = 0;
            var maxCalls = _options.MaxConsolidationCallsPerPersist;

            foreach (var entry in entries)
            {
                if (consolidated >= maxCalls)
                    break;

                var existingResults = await _memoryStore.SearchAsync(
                    scope, entry, _options.ConsolidateSearchTopK, ct);

                if (existingResults.Count == 0)
                    continue;

                var decision = await _memoryConsolidator.ConsolidateAsync(entry, existingResults, ct);

                switch (decision.Action)
                {
                    case MemoryAction.Update when decision.TargetEntryId.HasValue && decision.UpdatedContent != null:
                        await _memoryStore.UpdateEntryAsync(scope, decision.TargetEntryId.Value, decision.UpdatedContent, ct);
                        break;
                    case MemoryAction.Delete when decision.TargetEntryId.HasValue:
                        await _memoryStore.DeleteEntryAsync(scope, decision.TargetEntryId.Value, ct);
                        break;
                    // Add / Noop — 不做额外操作（条目已存在）
                }

                consolidated++;
            }

            lock (_gateLock)
            {
                _lastConsolidationTime = DateTime.UtcNow;
                _entriesAtLastConsolidation = currentCount;
            }

            _logger.LogInformation(
                "Memory consolidation completed for scope {Scope}: processed {Count} entries",
                scope, consolidated);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Memory consolidation failed for scope {Scope}", scope);
            return false;
        }
    }

    /// <summary>
    /// 重置门控状态（用于测试）
    /// </summary>
    public void ResetGates()
    {
        _lastConsolidationTime = DateTime.MinValue;
        _entriesAtLastConsolidation = 0;
    }

    private static string[] SplitEntries(string? memory)
    {
        if (string.IsNullOrWhiteSpace(memory)) return [];
        return memory.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
