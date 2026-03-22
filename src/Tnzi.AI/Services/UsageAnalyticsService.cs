namespace Tnzi.AI.Services;

/// <summary>
/// 使用量分析服务实现
/// </summary>
public class UsageAnalyticsService : ApplicationService, IUsageAnalyticsService
{
    private readonly IRepository<UsageLog, Guid> _repository;
    private readonly IRepository<Agent, Guid> _agentRepository;
    private readonly IRepository<AgentThreadMessage, Guid>? _messageRepository;
    private readonly IRepository<AgentThread, Guid>? _threadRepository;

    public UsageAnalyticsService(
        IRepository<UsageLog, Guid> repository,
        IRepository<Agent, Guid> agentRepository,
        IServiceProvider serviceProvider,
        IRepository<AgentThreadMessage, Guid>? messageRepository = null,
        IRepository<AgentThread, Guid>? threadRepository = null)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _agentRepository = Check.NotNull(agentRepository);
        _messageRepository = messageRepository;
        _threadRepository = threadRepository;
    }

    public async Task<Result<UsageSummaryDto>> GetSummaryAsync(UsageSummaryQueryDto query)
    {
        var q = BuildBaseQuery(query.StartTime, query.EndTime, query.Provider, query.Model, query.AgentId);

        var stats = await q.GroupBy(_ => 1).Select(g => new
        {
            TotalRequests = g.Count(),
            SuccessCount = g.Count(l => l.IsSuccess),
            TotalInput = g.Sum(l => (long)l.InputTokens),
            TotalOutput = g.Sum(l => (long)l.OutputTokens),
            TotalTokens = g.Sum(l => (long)l.TotalTokens),
            AvgDuration = g.Average(l => (double)l.DurationMs),
            TotalCost = g.Sum(l => l.EstimatedCostUsd ?? 0m)
        }).FirstOrDefaultAsync();

        if (stats == null) return Ok(new UsageSummaryDto());

        return Ok(new UsageSummaryDto
        {
            TotalRequests = stats.TotalRequests,
            SuccessfulRequests = stats.SuccessCount,
            FailedRequests = stats.TotalRequests - stats.SuccessCount,
            TotalInputTokens = stats.TotalInput,
            TotalOutputTokens = stats.TotalOutput,
            TotalTokens = stats.TotalTokens,
            AverageDurationMs = stats.AvgDuration,
            SuccessRate = stats.TotalRequests > 0 ? (double)stats.SuccessCount / stats.TotalRequests : 0,
            TotalEstimatedCostUsd = stats.TotalCost
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
                AverageDurationMs = g.Average(l => (double)l.DurationMs),
                TotalEstimatedCostUsd = g.Sum(l => l.EstimatedCostUsd ?? 0m)
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
                AverageDurationMs = g.Average(l => (double)l.DurationMs),
                TotalEstimatedCostUsd = g.Sum(l => l.EstimatedCostUsd ?? 0m)
            })
            .OrderByDescending(m => m.TotalTokens)
            .ToListAsync();

        return Ok(result);
    }

    public async Task<Result<List<UsageTrendPointDto>>> GetUsageTrendAsync(UsageTrendQueryDto query)
    {
        Check.NotNull(query);

        var granularity = query.Granularity;
        var q = BuildBaseQuery(query.StartTime, query.EndTime, query.Provider, query.Model, query.AgentId);

        // 按时间粒度分组聚合
        // 策略：先在 SQL 中聚合数值，再在内存中构建 Period/PeriodStart（避免 new DateTime() 无法翻译到 SQL）
        List<UsageTrendPointDto> points;
        if (granularity == UsageGranularity.Monthly)
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
        else if (granularity == UsageGranularity.Weekly)
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

    public async Task<Result<List<AgentFeedbackStatsDto>>> GetAgentFeedbackStatsAsync(int topN = 20)
    {
        if (_messageRepository == null || _threadRepository == null)
            return Ok(new List<AgentFeedbackStatsDto>());

        // SQL 层聚合计数（不加载全量消息到内存）
        var feedbackQuery = _messageRepository
            .Where(m => m.Role == "assistant" && m.FeedbackRating != null)
            .Join(
                _threadRepository.AsQueryable().Where(t => t.AgentId != null),
                m => m.ThreadId,
                t => t.Id,
                (m, t) => new { m.FeedbackRating, m.FeedbackTags, AgentId = t.AgentId!.Value });

        var stats = await feedbackQuery
            .GroupBy(x => x.AgentId)
            .Select(g => new
            {
                AgentId = g.Key,
                TotalRated = g.Count(),
                PositiveCount = g.Count(x => x.FeedbackRating == true),
                NegativeCount = g.Count(x => x.FeedbackRating == false)
            })
            .OrderByDescending(a => a.TotalRated)
            .Take(topN)
            .ToListAsync();

        if (stats.Count == 0)
            return Ok(new List<AgentFeedbackStatsDto>());

        var agentIds = stats.Select(a => a.AgentId).ToList();

        // 批量查询 Agent 名称
        var agentNames = await _agentRepository
            .Where(a => agentIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Name })
            .ToDictionaryAsync(a => a.Id, a => a.Name);

        // 仅差评标签需要内存解析（JSON 无法在 SQL 层聚合）
        var negativeTags = await feedbackQuery
            .Where(x => agentIds.Contains(x.AgentId) && x.FeedbackRating == false && x.FeedbackTags != null)
            .Select(x => new { x.AgentId, x.FeedbackTags })
            .ToListAsync();

        var tagsByAgent = negativeTags
            .GroupBy(x => x.AgentId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var tagDist = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    foreach (var item in g)
                    {
                        try
                        {
                            var tags = JsonSerializer.Deserialize<List<string>>(item.FeedbackTags!);
                            if (tags != null)
                            {
                                foreach (var tag in tags)
                                    tagDist[tag] = tagDist.GetValueOrDefault(tag) + 1;
                            }
                        }
                        catch { /* 忽略格式异常 */ }
                    }
                    return tagDist;
                });

        var result = stats.Select(a =>
        {
            var positiveRate = a.TotalRated > 0 ? (double)a.PositiveCount / a.TotalRated : 0.0;
            return new AgentFeedbackStatsDto
            {
                AgentId = a.AgentId,
                AgentName = agentNames.GetValueOrDefault(a.AgentId, "Unknown"),
                TotalRated = a.TotalRated,
                PositiveCount = a.PositiveCount,
                NegativeCount = a.NegativeCount,
                PositiveRate = Math.Round(positiveRate, 4),
                TagDistribution = tagsByAgent.GetValueOrDefault(a.AgentId, new Dictionary<string, int>())
            };
        }).ToList();

        return Ok(result);
    }

    public async Task<Result<CostSummaryDto>> GetCostSummaryAsync(DateTime startTime, DateTime endTime, string? provider = null, string? model = null)
    {
        var q = BuildBaseQuery(startTime, endTime, provider, model)
            .Where(l => l.EstimatedCostUsd != null);

        var total = await q.GroupBy(_ => 1).Select(g => new
        {
            TotalCost = g.Sum(l => l.EstimatedCostUsd ?? 0m),
            TotalRequests = (long)g.Count(),
            TotalInput = g.Sum(l => (long)l.InputTokens),
            TotalOutput = g.Sum(l => (long)l.OutputTokens)
        }).FirstOrDefaultAsync();

        if (total == null || total.TotalRequests == 0)
            return Ok(new CostSummaryDto());

        // 按 Provider 分组
        var byProvider = await q
            .GroupBy(l => l.Provider)
            .Select(g => new ProviderCostDto
            {
                Provider = g.Key,
                TotalCostUsd = g.Sum(l => l.EstimatedCostUsd ?? 0m),
                TotalRequests = g.Count()
            })
            .OrderByDescending(p => p.TotalCostUsd)
            .ToListAsync();

        // 按 Model 分组
        var byModel = await q
            .GroupBy(l => new { l.Provider, l.Model })
            .Select(g => new ModelCostDto
            {
                Provider = g.Key.Provider,
                Model = g.Key.Model,
                TotalCostUsd = g.Sum(l => l.EstimatedCostUsd ?? 0m),
                TotalRequests = g.Count(),
                TotalInputTokens = g.Sum(l => (long)l.InputTokens),
                TotalOutputTokens = g.Sum(l => (long)l.OutputTokens)
            })
            .OrderByDescending(m => m.TotalCostUsd)
            .ToListAsync();

        // 计算百分比
        var totalCost = total.TotalCost;
        if (totalCost > 0)
        {
            foreach (var p in byProvider)
                p.CostPercentage = Math.Round(p.TotalCostUsd / totalCost * 100, 2);
            foreach (var m in byModel)
                m.CostPercentage = Math.Round(m.TotalCostUsd / totalCost * 100, 2);
        }

        return Ok(new CostSummaryDto
        {
            TotalCostUsd = totalCost,
            TotalRequests = total.TotalRequests,
            TotalInputTokens = total.TotalInput,
            TotalOutputTokens = total.TotalOutput,
            AverageCostPerRequest = total.TotalRequests > 0
                ? Math.Round(totalCost / total.TotalRequests, 6)
                : 0m,
            ByProvider = byProvider,
            ByModel = byModel
        });
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
