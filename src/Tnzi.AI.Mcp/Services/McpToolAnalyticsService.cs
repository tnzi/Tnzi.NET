namespace Tnzi.AI.Mcp.Services;

/// <summary>
/// MCP 工具使用分析服务实现
/// </summary>
public class McpToolAnalyticsService : ApplicationService, IMcpToolAnalyticsService
{
    private readonly IRepository<McpToolUsageRecord, long> _repository;

    public McpToolAnalyticsService(IServiceProvider serviceProvider, IRepository<McpToolUsageRecord, long> repository)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
    }

    public async Task RecordUsageAsync(string toolName, long durationMs, bool isSuccess, string? errorMessage = null, string? callerApiKeyId = null)
    {
        Check.NotNullOrWhiteSpace(toolName);

        var record = new McpToolUsageRecord
        {
            ToolName = toolName,
            DurationMs = durationMs,
            IsSuccess = isSuccess,
            ErrorMessage = errorMessage,
            CallerApiKeyId = callerApiKeyId
        };

        await _repository.InsertAsync(record);
    }

    public async Task<Result<McpToolStatsDto>> GetToolStatsAsync(string toolName, DateTime? from = null, DateTime? to = null)
    {
        Check.NotNullOrWhiteSpace(toolName);

        var query = BuildBaseQuery(toolName, from, to);

        var stats = await query.GroupBy(_ => 1).Select(g => new
        {
            TotalCalls = g.Count(),
            ErrorCount = g.Count(r => !r.IsSuccess),
            AvgDuration = g.Average(r => (double)r.DurationMs),
            FirstUsed = g.Min(r => r.CreationTime),
            LastUsed = g.Max(r => r.CreationTime),
            UniqueCallers = g.Select(r => r.CallerApiKeyId).Distinct().Count()
        }).FirstOrDefaultAsync();

        if (stats == null)
        {
            return Ok(new McpToolStatsDto { ToolName = toolName });
        }

        // P95 延迟：取所有记录按时长排序后的 95% 位置值
        var p95Duration = await CalculateP95Async(query);

        return Ok(new McpToolStatsDto
        {
            ToolName = toolName,
            TotalCalls = stats.TotalCalls,
            AvgDurationMs = Math.Round(stats.AvgDuration, 2),
            P95DurationMs = p95Duration,
            ErrorRate = stats.TotalCalls > 0 ? Math.Round((double)stats.ErrorCount / stats.TotalCalls, 4) : 0,
            UniqueCallers = stats.UniqueCallers,
            FirstUsed = stats.FirstUsed,
            LastUsed = stats.LastUsed
        });
    }

    public async Task<Result<List<McpToolPopularityDto>>> GetMostUsedToolsAsync(int top = 10, DateTime? from = null, DateTime? to = null)
    {
        var query = _repository.AsQueryable();

        if (from.HasValue)
            query = query.Where(r => r.CreationTime >= from.Value);
        if (to.HasValue)
            query = query.Where(r => r.CreationTime <= to.Value);

        var result = await query
            .GroupBy(r => r.ToolName)
            .Select(g => new McpToolPopularityDto
            {
                ToolName = g.Key,
                CallCount = g.Count(),
                SuccessRate = g.Count() > 0 ? Math.Round((double)g.Count(r => r.IsSuccess) / g.Count(), 4) : 0,
                AvgDurationMs = Math.Round(g.Average(r => (double)r.DurationMs), 2)
            })
            .OrderByDescending(d => d.CallCount)
            .Take(top)
            .ToListAsync();

        return Ok(result);
    }

    public async Task<Result<List<McpToolErrorDto>>> GetToolErrorsAsync(string toolName, int top = 20)
    {
        Check.NotNullOrWhiteSpace(toolName);

        var result = await _repository.AsQueryable()
            .Where(r => r.ToolName == toolName && !r.IsSuccess && r.ErrorMessage != null)
            .GroupBy(r => r.ErrorMessage!)
            .Select(g => new McpToolErrorDto
            {
                ErrorMessage = g.Key,
                Count = g.Count(),
                LastOccurred = g.Max(r => r.CreationTime)
            })
            .OrderByDescending(d => d.Count)
            .Take(top)
            .ToListAsync();

        return Ok(result);
    }

    public async Task<Result<int>> CleanupOldRecordsAsync(int retentionDays = 90)
    {
        // Guard against deleting the entire table: a retention window below one day
        // (0 or negative) would set the cutoff to now/future and purge every record.
        if (retentionDays < 1)
        {
            return Fail<int>("retentionDays must be at least 1 day.", 400);
        }

        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var deleted = await _repository.AsQueryable()
            .Where(r => r.CreationTime < cutoff)
            .ExecuteDeleteAsync();

        return Ok(deleted);
    }

    private IQueryable<McpToolUsageRecord> BuildBaseQuery(string toolName, DateTime? from, DateTime? to)
    {
        var query = _repository.AsQueryable()
            .Where(r => r.ToolName == toolName);

        if (from.HasValue)
            query = query.Where(r => r.CreationTime >= from.Value);
        if (to.HasValue)
            query = query.Where(r => r.CreationTime <= to.Value);

        return query;
    }

    private static async Task<double> CalculateP95Async(IQueryable<McpToolUsageRecord> query)
    {
        var totalCount = await query.CountAsync();
        if (totalCount == 0) return 0;

        var p95Index = (int)Math.Ceiling(totalCount * 0.95) - 1;
        if (p95Index < 0) p95Index = 0;

        var p95Value = await query
            .OrderBy(r => r.DurationMs)
            .Skip(p95Index)
            .Select(r => (double)r.DurationMs)
            .FirstOrDefaultAsync();

        return Math.Round(p95Value, 2);
    }
}
