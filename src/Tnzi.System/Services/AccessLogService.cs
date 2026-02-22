namespace Tnzi.System.Services;

/// <summary>
/// 访问日志服务实现
/// </summary>
public class AccessLogService : ApplicationService, IAccessLogService
{
    private readonly IRepository<AccessLog, Guid> _accessLogRepository;
    private readonly IAccessLogSender _accessLogSender;

    public AccessLogService(
        IServiceProvider serviceProvider,
        IRepository<AccessLog, Guid> accessLogRepository,
        IAccessLogSender accessLogSender)
        : base(serviceProvider)
    {
        _accessLogRepository = Check.NotNull(accessLogRepository);
        _accessLogSender = Check.NotNull(accessLogSender);
    }

    /// <inheritdoc />
    public async Task<Result> LogAccessAsync(AccessLogDto log)
    {
        Check.NotNull(log);

        // 仅发送到后台队列，不阻塞当前请求
        await _accessLogSender.SendAsync(log);
        return Ok("Access log sent to background queue");
    }

    /// <inheritdoc />
    public async Task<Result<IPagedList<AccessLogDto>>> GetAccessLogsAsync(AccessLogQueryDto query)
    {
        Check.NotNull(query);

        IQueryable<AccessLog> q = _accessLogRepository;

        if (query.UserId.HasValue)
            q = q.Where(log => log.UserId == query.UserId.Value);

        if (query.StartTime.HasValue)
            q = q.Where(log => log.CreationTime >= query.StartTime.Value);

        if (query.EndTime.HasValue)
            q = q.Where(log => log.CreationTime <= query.EndTime.Value);

        q = q.OrderByDescending(log => log.CreationTime);

        var paged = await q
            .ProjectTo<AccessLog, AccessLogDto>()
            .CreateAsync(query.PageIndex, query.PageSize);

        return Ok(paged);
    }

    /// <inheritdoc />
    public async Task<Result<AccessLogDto>> GetAccessLogByIdAsync(Guid id)
    {
        var log = await _accessLogRepository
            .AsQueryable()
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id);

        if (log == null)
            return Fail<AccessLogDto>("Access log not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        return Ok(log.MapTo<AccessLogDto>());
    }

    /// <inheritdoc />
    public async Task<Result<AccessLogStatisticsDto>> GetAccessLogStatisticsAsync(DateTime? startTime = null, DateTime? endTime = null)
    {
        var query = _accessLogRepository.AsQueryable();

        if (startTime.HasValue)
            query = query.Where(log => log.CreationTime >= startTime.Value);

        if (endTime.HasValue)
            query = query.Where(log => log.CreationTime <= endTime.Value);

        // 检查是否有数据
        if (!await query.AnyAsync())
            return Ok(new AccessLogStatisticsDto());

        // 使用单次聚合查询
        var stats = await query.GroupBy(o => 1).Select(g => new
        {
            TotalRequests = g.Count(),
            UniqueUsers = g.Select(log => log.UserId).Distinct().Count(),
            SuccessRequests = g.Count(log => log.StatusCode >= 200 && log.StatusCode < 300),
            ErrorRequests = g.Count(log => log.StatusCode >= 400),
            AverageResponseTime = g.Average(log => (double)log.ResponseTime)
        }).FirstOrDefaultAsync();

        if (stats == null)
            return Ok(new AccessLogStatisticsDto());

        var statistics = new AccessLogStatisticsDto
        {
            TotalRequests = stats.TotalRequests,
            UniqueUsers = stats.UniqueUsers,
            SuccessRequests = stats.SuccessRequests,
            ErrorRequests = stats.ErrorRequests,
            AverageResponseTime = stats.AverageResponseTime
        };
        return Ok(statistics);
    }

    /// <inheritdoc />
    public async Task<Result> DeleteExpiredAccessLogsAsync(int days = 90)
    {
        var expireDate = DateTime.UtcNow.AddDays(-days);

        var count = await _accessLogRepository.CountAsync(log => log.CreationTime < expireDate);
        if (count > 0)
        {
            await _accessLogRepository.DeleteAsync(log => log.CreationTime < expireDate);
        }

        LogInformation("Deleted {Count} expired access logs (older than {Days} days)", count, days);
        return Ok($"Deleted {count} expired access logs");
    }

    /// <inheritdoc />
    public async Task<Result> DeleteAccessLogsAsync(IEnumerable<Guid> ids)
    {
        Check.NotNullOrEmpty(ids);

        var idList = ids.ToList();
        var logs = await _accessLogRepository
            .Where(l => idList.Contains(l.Id))
            .ToListAsync();

        if (logs.Count == 0)
            return Ok("No access logs found to delete");

        await _accessLogRepository.DeleteManyAsync(logs);

        LogInformation("Batch deleted {Count} access logs", logs.Count);
        return Ok($"Deleted {logs.Count} access logs successfully");
    }
}
