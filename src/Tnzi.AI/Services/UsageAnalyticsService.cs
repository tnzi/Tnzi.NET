namespace Tnzi.AI.Services;

/// <summary>
/// 使用量分析服务实现
/// </summary>
public class UsageAnalyticsService : ApplicationService, IUsageAnalyticsService
{
    private readonly IRepository<UsageLog, Guid> _repository;

    public UsageAnalyticsService(IRepository<UsageLog, Guid> repository, IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
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
