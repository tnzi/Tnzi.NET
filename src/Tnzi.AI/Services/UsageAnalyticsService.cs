namespace Tnzi.AI.Services;

/// <summary>
/// 使用量分析服务实现
/// </summary>
public class UsageAnalyticsService : ApplicationService, IUsageAnalyticsService
{
    private readonly IRepository<UsageLog, Guid> _repository;
    private readonly IRepository<Agent, Guid> _agentRepository;

    public UsageAnalyticsService(
        IRepository<UsageLog, Guid> repository,
        IRepository<Agent, Guid> agentRepository,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _agentRepository = Check.NotNull(agentRepository);
    }

    public async Task<Result<UsageSummaryDto>> GetSummaryAsync(UsageSummaryQueryDto query)
    {
        var q = BuildBaseQuery(query.StartTime, query.EndTime, query.Provider, query.Model, query.AgentId);

        var totalRequests = await q.CountAsync();
        if (totalRequests == 0)
        {
            return Ok(new UsageSummaryDto());
        }

        var successCount = await q.CountAsync(l => l.IsSuccess);
        var stats = await q.GroupBy(_ => 1).Select(g => new
        {
            TotalInput = g.Sum(l => (long)l.InputTokens),
            TotalOutput = g.Sum(l => (long)l.OutputTokens),
            TotalTokens = g.Sum(l => (long)l.TotalTokens),
            AvgDuration = g.Average(l => (double)l.DurationMs)
        }).FirstOrDefaultAsync();

        return Ok(new UsageSummaryDto
        {
            TotalRequests = totalRequests,
            SuccessfulRequests = successCount,
            FailedRequests = totalRequests - successCount,
            TotalInputTokens = stats?.TotalInput ?? 0,
            TotalOutputTokens = stats?.TotalOutput ?? 0,
            TotalTokens = stats?.TotalTokens ?? 0,
            AverageDurationMs = stats?.AvgDuration ?? 0,
            SuccessRate = totalRequests > 0 ? (double)successCount / totalRequests : 0
        });
    }

    public async Task<Result<IPagedList<UsageLogDto>>> GetLogsAsync(UsageLogQueryDto query)
    {
        var q = _repository.AsQueryable();

        if (query.StartTime.HasValue)
            q = q.Where(l => l.CreationTime >= query.StartTime.Value);
        if (query.EndTime.HasValue)
            q = q.Where(l => l.CreationTime <= query.EndTime.Value);
        if (!string.IsNullOrWhiteSpace(query.Provider))
            q = q.Where(l => l.Provider == query.Provider);
        if (!string.IsNullOrWhiteSpace(query.Model))
            q = q.Where(l => l.Model == query.Model);
        if (!string.IsNullOrWhiteSpace(query.OperationType))
            q = q.Where(l => l.OperationType == query.OperationType);
        if (query.IsSuccess.HasValue)
            q = q.Where(l => l.IsSuccess == query.IsSuccess.Value);
        if (query.AgentId.HasValue)
            q = q.Where(l => l.AgentId == query.AgentId.Value);
        var pagedList = await q
            .OrderByDescending(l => l.CreationTime)
            .ProjectTo<UsageLog, UsageLogDto>()
            .CreateAsync(query);

        return Ok(pagedList);
    }

    public async Task<Result<List<ProviderUsageDto>>> GetUsageByProviderAsync(DateTime startTime, DateTime endTime)
    {
        var result = await BuildBaseQuery(startTime, endTime)
            .GroupBy(l => l.Provider)
            .Select(g => new ProviderUsageDto
            {
                Provider = g.Key,
                TotalRequests = g.Count(),
                TotalInputTokens = g.Sum(l => (long)l.InputTokens),
                TotalOutputTokens = g.Sum(l => (long)l.OutputTokens),
                TotalTokens = g.Sum(l => (long)l.TotalTokens),
                AverageDurationMs = g.Average(l => (double)l.DurationMs)
            })
            .OrderByDescending(p => p.TotalTokens)
            .ToListAsync();

        return Ok(result);
    }

    public async Task<Result<List<ModelUsageDto>>> GetUsageByModelAsync(DateTime startTime, DateTime endTime, string? provider = null)
    {
        var q = BuildBaseQuery(startTime, endTime, provider);

        var result = await q
            .GroupBy(l => new { l.Provider, l.Model })
            .Select(g => new ModelUsageDto
            {
                Provider = g.Key.Provider,
                Model = g.Key.Model,
                TotalRequests = g.Count(),
                TotalInputTokens = g.Sum(l => (long)l.InputTokens),
                TotalOutputTokens = g.Sum(l => (long)l.OutputTokens),
                TotalTokens = g.Sum(l => (long)l.TotalTokens),
                AverageDurationMs = g.Average(l => (double)l.DurationMs)
            })
            .OrderByDescending(m => m.TotalTokens)
            .ToListAsync();

        return Ok(result);
    }

    public async Task<Result<List<UsageTrendPointDto>>> GetUsageTrendAsync(UsageTrendQueryDto query)
    {
        Check.NotNull(query);

        var granularity = query.Granularity?.ToLowerInvariant() ?? "daily";
        var q = BuildBaseQuery(query.StartTime, query.EndTime, query.Provider, query.Model, query.AgentId);

        // 按时间粒度分组聚合
        // 策略：先在 SQL 中聚合数值，再在内存中构建 Period/PeriodStart（避免 new DateTime() 无法翻译到 SQL）
        List<UsageTrendPointDto> points;
        if (granularity == "monthly")
        {
            var raw = await q
                .GroupBy(l => new { l.CreationTime.Year, l.CreationTime.Month })
                .Select(g => new
                {
                    g.Key.Year, g.Key.Month,
                    TotalRequests = (long)g.Count(),
                    SuccessfulRequests = (long)g.Count(l => l.IsSuccess),
                    TotalInputTokens = g.Sum(l => (long)l.InputTokens),
                    TotalOutputTokens = g.Sum(l => (long)l.OutputTokens),
                    TotalTokens = g.Sum(l => (long)l.TotalTokens),
                    AverageDurationMs = g.Average(l => (double)l.DurationMs)
                })
                .OrderBy(p => p.Year).ThenBy(p => p.Month)
                .ToListAsync();

            points = raw.Select(g => new UsageTrendPointDto
            {
                Period = $"{g.Year}-{g.Month:D2}",
                PeriodStart = new DateTime(g.Year, g.Month, 1, 0, 0, 0, DateTimeKind.Utc),
                TotalRequests = g.TotalRequests,
                SuccessfulRequests = g.SuccessfulRequests,
                TotalInputTokens = g.TotalInputTokens,
                TotalOutputTokens = g.TotalOutputTokens,
                TotalTokens = g.TotalTokens,
                AverageDurationMs = g.AverageDurationMs
            }).ToList();
        }
        else if (granularity == "weekly")
        {
            // 按 Year+Week 分组（使用 DayOfYear/7 近似周号）
            var raw = await q
                .GroupBy(l => new { l.CreationTime.Year, Week = (l.CreationTime.DayOfYear - 1) / 7 })
                .Select(g => new
                {
                    g.Key.Year, g.Key.Week,
                    TotalRequests = (long)g.Count(),
                    SuccessfulRequests = (long)g.Count(l => l.IsSuccess),
                    TotalInputTokens = g.Sum(l => (long)l.InputTokens),
                    TotalOutputTokens = g.Sum(l => (long)l.OutputTokens),
                    TotalTokens = g.Sum(l => (long)l.TotalTokens),
                    AverageDurationMs = g.Average(l => (double)l.DurationMs)
                })
                .OrderBy(p => p.Year).ThenBy(p => p.Week)
                .ToListAsync();

            points = raw.Select(g => new UsageTrendPointDto
            {
                Period = $"{g.Year}-W{(g.Week + 1):D2}",
                PeriodStart = new DateTime(g.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(g.Week * 7),
                TotalRequests = g.TotalRequests,
                SuccessfulRequests = g.SuccessfulRequests,
                TotalInputTokens = g.TotalInputTokens,
                TotalOutputTokens = g.TotalOutputTokens,
                TotalTokens = g.TotalTokens,
                AverageDurationMs = g.AverageDurationMs
            }).ToList();
        }
        else // daily (default)
        {
            var raw = await q
                .GroupBy(l => new { l.CreationTime.Year, l.CreationTime.Month, l.CreationTime.Day })
                .Select(g => new
                {
                    g.Key.Year, g.Key.Month, g.Key.Day,
                    TotalRequests = (long)g.Count(),
                    SuccessfulRequests = (long)g.Count(l => l.IsSuccess),
                    TotalInputTokens = g.Sum(l => (long)l.InputTokens),
                    TotalOutputTokens = g.Sum(l => (long)l.OutputTokens),
                    TotalTokens = g.Sum(l => (long)l.TotalTokens),
                    AverageDurationMs = g.Average(l => (double)l.DurationMs)
                })
                .OrderBy(p => p.Year).ThenBy(p => p.Month).ThenBy(p => p.Day)
                .ToListAsync();

            points = raw.Select(g => new UsageTrendPointDto
            {
                Period = $"{g.Year}-{g.Month:D2}-{g.Day:D2}",
                PeriodStart = new DateTime(g.Year, g.Month, g.Day, 0, 0, 0, DateTimeKind.Utc),
                TotalRequests = g.TotalRequests,
                SuccessfulRequests = g.SuccessfulRequests,
                TotalInputTokens = g.TotalInputTokens,
                TotalOutputTokens = g.TotalOutputTokens,
                TotalTokens = g.TotalTokens,
                AverageDurationMs = g.AverageDurationMs
            }).ToList();
        }

        return Ok(points);
    }

    public async Task<Result<List<AgentUsageDto>>> GetUsageByAgentAsync(DateTime startTime, DateTime endTime, int topN = 20)
    {
        var agentStats = await BuildBaseQuery(startTime, endTime)
            .Where(l => l.AgentId != null)
            .GroupBy(l => l.AgentId!.Value)
            .Select(g => new
            {
                AgentId = g.Key,
                TotalRequests = (long)g.Count(),
                SuccessfulRequests = (long)g.Count(l => l.IsSuccess),
                TotalInputTokens = g.Sum(l => (long)l.InputTokens),
                TotalOutputTokens = g.Sum(l => (long)l.OutputTokens),
                TotalTokens = g.Sum(l => (long)l.TotalTokens),
                AverageDurationMs = g.Average(l => (double)l.DurationMs)
            })
            .OrderByDescending(a => a.TotalTokens)
            .Take(topN)
            .ToListAsync();

        if (agentStats.Count == 0)
            return Ok(new List<AgentUsageDto>());

        // 批量查询 Agent 名称
        var agentIds = agentStats.Select(a => a.AgentId).ToList();
        var agentNames = await _agentRepository
            .Where(a => agentIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Name })
            .ToDictionaryAsync(a => a.Id, a => a.Name);

        var result = agentStats.Select(a => new AgentUsageDto
        {
            AgentId = a.AgentId,
            AgentName = agentNames.GetValueOrDefault(a.AgentId, "Unknown"),
            TotalRequests = a.TotalRequests,
            TotalInputTokens = a.TotalInputTokens,
            TotalOutputTokens = a.TotalOutputTokens,
            TotalTokens = a.TotalTokens,
            AverageDurationMs = a.AverageDurationMs,
            SuccessRate = a.TotalRequests > 0 ? (double)a.SuccessfulRequests / a.TotalRequests : 0
        }).ToList();

        return Ok(result);
    }

    private IQueryable<UsageLog> BuildBaseQuery(DateTime startTime, DateTime endTime, string? provider = null, string? model = null, Guid? agentId = null)
    {
        var q = _repository
            .Where(l => l.CreationTime >= startTime && l.CreationTime <= endTime);

        if (!string.IsNullOrWhiteSpace(provider))
            q = q.Where(l => l.Provider == provider);
        if (!string.IsNullOrWhiteSpace(model))
            q = q.Where(l => l.Model == model);
        if (agentId.HasValue)
            q = q.Where(l => l.AgentId == agentId.Value);

        return q;
    }
}
