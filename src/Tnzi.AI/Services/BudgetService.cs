namespace Tnzi.AI.Services;

/// <summary>
/// USD 成本预算管理服务实现 - 基于 UsageLog 聚合 + 内存缓存
/// </summary>
public class BudgetService : ApplicationService, IBudgetService
{
    private readonly IRepository<UsageLog, Guid> _usageLogRepository;
    private readonly IRepository<Agent, Guid> _agentRepository;
    private readonly IOptionsMonitor<AIOptions> _aiOptions;

    /// <summary>
    /// 进程级内存缓存：key = "budget:{tenantId}:{userId}" → (spend, expiry)。
    /// 使用静态字典实现跨请求共享（BudgetService 是 Scoped），通过 TTL 控制刷新。
    /// 超出 MaxCacheEntries 时清除已过期条目，仍超出则清除最旧条目。
    /// 注意：USD 预算检查为 "best effort" 级别（非原子性），并发请求可能短暂超支。
    /// 通过较短的缓存 TTL（默认 60s）和 WarningThreshold 时主动失效缓存来缩小竞态窗口。
    /// </summary>
    private static readonly ConcurrentDictionary<string, (decimal Spend, DateTime Expiry)> SpendCache = new();

    /// <summary>
    /// 清除所有内存缓存条目。仅供测试使用。
    /// </summary>
    public static void ClearCacheForTesting() => SpendCache.Clear();

    /// <summary>缓存最大条目数，超出时触发淘汰</summary>
    private const int MaxCacheEntries = 500;

    public BudgetService(
        IServiceProvider serviceProvider,
        IRepository<UsageLog, Guid> usageLogRepository,
        IRepository<Agent, Guid> agentRepository,
        IOptionsMonitor<AIOptions> aiOptions)
        : base(serviceProvider)
    {
        _usageLogRepository = Check.NotNull(usageLogRepository);
        _agentRepository = Check.NotNull(agentRepository);
        _aiOptions = Check.NotNull(aiOptions);
    }

    public async Task<BudgetCheckResult> CheckBudgetAsync(Guid? userId, Guid? tenantId, Guid? agentId, CancellationToken ct = default)
    {
        var options = _aiOptions.CurrentValue.Budget;
        if (!options.Enabled)
        {
            return new BudgetCheckResult { IsAllowed = true, Status = BudgetStatus.WithinBudget };
        }

        // 获取当月预算上限
        var budgetLimit = options.DefaultMonthlyBudgetUsd;

        // 如果有 Agent 级预算覆盖，先检查 Agent 预算
        if (agentId.HasValue)
        {
            var agentBudgetResult = await CheckAgentBudgetAsync(agentId.Value, options, ct);
            if (agentBudgetResult != null && !agentBudgetResult.IsAllowed)
            {
                return agentBudgetResult;
            }
        }

        // 检查总预算（按租户或用户维度）
        var currentSpend = await GetCurrentPeriodSpendAsync(userId, tenantId, options, ct);

        var result = EvaluateBudget(currentSpend, budgetLimit, options.WarningThreshold);

        // 当接近预算上限时，主动失效缓存以减小 TOCTOU 竞态窗口，
        // 确保下次请求从 DB 读取最新数据
        if (result.Status == BudgetStatus.WarningThreshold)
        {
            var cacheKey = BuildCacheKey(userId, tenantId);
            SpendCache.TryRemove(cacheKey, out _);
        }

        return result;
    }

    public Task UpdateSpendAsync(Guid? userId, Guid? tenantId, Guid? agentId, decimal costUsd, CancellationToken ct = default)
    {
        // 主动失效缓存，下次 CheckBudget 时重新聚合
        var cacheKey = BuildCacheKey(userId, tenantId);
        SpendCache.TryRemove(cacheKey, out _);

        // Agent 级缓存也失效
        if (agentId.HasValue)
        {
            var agentCacheKey = $"budget:agent:{agentId.Value}";
            SpendCache.TryRemove(agentCacheKey, out _);
        }

        return Task.CompletedTask;
    }

    public async Task<BudgetSummaryDto> GetSummaryAsync(Guid? tenantId, DateTime startTime, DateTime endTime, CancellationToken ct = default)
    {
        var options = _aiOptions.CurrentValue.Budget;

        var q = _usageLogRepository
            .Where(l => l.CreationTime >= startTime && l.CreationTime <= endTime && l.EstimatedCostUsd != null);

        if (tenantId.HasValue)
        {
            q = q.Where(l => l.TenantId == tenantId.Value);
        }

        // 总花费
        var totalSpend = await q
            .SumAsync(l => l.EstimatedCostUsd ?? 0m, ct);

        // 按 Agent 分组
        var agentSpends = await q
            .Where(l => l.AgentId != null)
            .GroupBy(l => l.AgentId!.Value)
            .Select(g => new
            {
                AgentId = g.Key,
                SpendUsd = g.Sum(l => l.EstimatedCostUsd ?? 0m),
                RequestCount = g.Count()
            })
            .OrderByDescending(a => a.SpendUsd)
            .Take(50)
            .ToListAsync(ct);

        // 批量查询 Agent 名称
        var agentIds = agentSpends.Select(a => a.AgentId).ToList();
        var agents = agentIds.Count > 0
            ? await _agentRepository.Where(a => agentIds.Contains(a.Id)).Select(a => new { a.Id, a.Name }).ToListAsync(ct)
            : [];
        var agentNameMap = agents.ToDictionary(a => a.Id, a => a.Name);

        var budgetLimit = options.DefaultMonthlyBudgetUsd;
        var usagePercentage = budgetLimit > 0 ? (double)(totalSpend / budgetLimit) : 0;

        return new BudgetSummaryDto
        {
            PeriodStart = startTime,
            PeriodEnd = endTime,
            CurrentSpendUsd = totalSpend,
            BudgetLimitUsd = budgetLimit,
            UsagePercentage = Math.Min(usagePercentage, 1.0),
            Status = EvaluateBudgetStatus(usagePercentage, options.WarningThreshold),
            ByAgent = agentSpends.Select(a => new AgentSpendDto
            {
                AgentId = a.AgentId,
                AgentName = agentNameMap.GetValueOrDefault(a.AgentId, "Unknown"),
                SpendUsd = a.SpendUsd,
                AgentBudgetLimitUsd = ResolveAgentBudgetLimit(a.AgentId, agentNameMap.GetValueOrDefault(a.AgentId), options),
                RequestCount = a.RequestCount
            }).ToList()
        };
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private async Task<BudgetCheckResult?> CheckAgentBudgetAsync(Guid agentId, BudgetOptions options, CancellationToken ct)
    {
        if (options.PerAgentBudgets.Count == 0)
            return null;

        // 尝试按 Agent ID 查找预算
        var agentIdStr = agentId.ToString();
        if (!options.PerAgentBudgets.TryGetValue(agentIdStr, out var agentBudget))
        {
            // 按 Agent 名称查找
            var agentName = await GetAgentNameAsync(agentId, ct);
            if (agentName == null || !options.PerAgentBudgets.TryGetValue(agentName, out agentBudget))
            {
                return null; // 无 Agent 级预算，交由总预算检查
            }
        }

        var agentSpend = await GetAgentPeriodSpendAsync(agentId, options, ct);
        var result = EvaluateBudget(agentSpend, agentBudget, options.WarningThreshold);
        if (!result.IsAllowed)
        {
            result.Reason = $"Agent budget exceeded: spent ${agentSpend:F4} of ${agentBudget:F2} monthly limit";
        }

        return result;
    }

    private async Task<decimal> GetCurrentPeriodSpendAsync(Guid? userId, Guid? tenantId, BudgetOptions options, CancellationToken ct)
    {
        var cacheKey = BuildCacheKey(userId, tenantId);
        var ttl = TimeSpan.FromSeconds(Math.Max(options.CacheTtlSeconds, 0));

        if (SpendCache.TryGetValue(cacheKey, out var cached) && cached.Expiry > DateTime.UtcNow)
        {
            return cached.Spend;
        }

        var (periodStart, periodEnd) = GetCurrentMonthPeriod();

        var q = _usageLogRepository
            .Where(l => l.CreationTime >= periodStart && l.CreationTime < periodEnd && l.EstimatedCostUsd != null);

        if (tenantId.HasValue)
            q = q.Where(l => l.TenantId == tenantId.Value);

        // Note: UsageLog (CreationAuditedEntity) 没有 UserId 字段，
        // 预算管控在租户+Agent 维度生效。用户维度在缓存键中区分仅用于隔离，不过滤查询。

        var spend = await q.SumAsync(l => l.EstimatedCostUsd ?? 0m, ct);

        SetCacheEntry(cacheKey, spend, ttl);
        return spend;
    }

    private async Task<decimal> GetAgentPeriodSpendAsync(Guid agentId, BudgetOptions options, CancellationToken ct)
    {
        var cacheKey = $"budget:agent:{agentId}";
        var ttl = TimeSpan.FromSeconds(Math.Max(options.CacheTtlSeconds, 0));

        if (SpendCache.TryGetValue(cacheKey, out var cached) && cached.Expiry > DateTime.UtcNow)
        {
            return cached.Spend;
        }

        var (periodStart, periodEnd) = GetCurrentMonthPeriod();

        var spend = await _usageLogRepository
            .Where(l => l.CreationTime >= periodStart && l.CreationTime < periodEnd
                        && l.EstimatedCostUsd != null && l.AgentId == agentId)
            .SumAsync(l => l.EstimatedCostUsd ?? 0m, ct);

        SetCacheEntry(cacheKey, spend, ttl);
        return spend;
    }

    /// <summary>
    /// 设置缓存条目，超出 MaxCacheEntries 时先清除已过期条目，仍超出则清除最旧条目。
    /// </summary>
    private static void SetCacheEntry(string key, decimal spend, TimeSpan ttl)
    {
        if (SpendCache.Count >= MaxCacheEntries && !SpendCache.ContainsKey(key))
        {
            EvictExpiredEntries();
        }

        // 仍然超出上限，移除最旧的条目
        if (SpendCache.Count >= MaxCacheEntries && !SpendCache.ContainsKey(key))
        {
            var oldest = SpendCache
                .OrderBy(kvp => kvp.Value.Expiry)
                .Take(SpendCache.Count / 4 + 1) // 清除 25% 避免频繁淘汰
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var k in oldest)
            {
                SpendCache.TryRemove(k, out _);
            }
        }

        SpendCache[key] = (spend, DateTime.UtcNow.Add(ttl));
    }

    /// <summary>清除所有已过期的缓存条目</summary>
    private static void EvictExpiredEntries()
    {
        var now = DateTime.UtcNow;
        var expiredKeys = SpendCache
            .Where(kvp => kvp.Value.Expiry <= now)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            SpendCache.TryRemove(key, out _);
        }
    }

    private async Task<string?> GetAgentNameAsync(Guid agentId, CancellationToken ct)
    {
        var agent = await _agentRepository
            .Where(a => a.Id == agentId)
            .Select(a => a.Name)
            .FirstOrDefaultAsync(ct);
        return agent;
    }

    private static BudgetCheckResult EvaluateBudget(decimal currentSpend, decimal budgetLimit, double warningThreshold)
    {
        var usagePercentage = budgetLimit > 0 ? (double)(currentSpend / budgetLimit) : 0;
        var status = EvaluateBudgetStatus(usagePercentage, warningThreshold);

        return new BudgetCheckResult
        {
            IsAllowed = status != BudgetStatus.BudgetExceeded,
            Status = status,
            CurrentSpendUsd = currentSpend,
            BudgetLimitUsd = budgetLimit,
            UsagePercentage = Math.Min(usagePercentage, 1.0),
            Reason = status switch
            {
                BudgetStatus.BudgetExceeded => $"Monthly budget exceeded: spent ${currentSpend:F4} of ${budgetLimit:F2} limit",
                BudgetStatus.WarningThreshold => $"Budget warning: spent ${currentSpend:F4} ({usagePercentage:P0} of ${budgetLimit:F2} limit)",
                _ => null
            }
        };
    }

    private static BudgetStatus EvaluateBudgetStatus(double usagePercentage, double warningThreshold)
    {
        if (usagePercentage >= 1.0)
            return BudgetStatus.BudgetExceeded;
        if (usagePercentage >= warningThreshold)
            return BudgetStatus.WarningThreshold;
        return BudgetStatus.WithinBudget;
    }

    private static decimal? ResolveAgentBudgetLimit(Guid agentId, string? agentName, BudgetOptions options)
    {
        if (options.PerAgentBudgets.Count == 0)
            return null;

        if (options.PerAgentBudgets.TryGetValue(agentId.ToString(), out var budget))
            return budget;

        if (agentName != null && options.PerAgentBudgets.TryGetValue(agentName, out budget))
            return budget;

        return null;
    }

    private static string BuildCacheKey(Guid? userId, Guid? tenantId)
    {
        return $"budget:{tenantId?.ToString() ?? "global"}:{userId?.ToString() ?? "all"}";
    }

    private static (DateTime Start, DateTime End) GetCurrentMonthPeriod()
    {
        var now = DateTime.UtcNow;
        var start = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddMonths(1);
        return (start, end);
    }
}
