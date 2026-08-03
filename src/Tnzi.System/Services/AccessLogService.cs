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

        if (!string.IsNullOrWhiteSpace(query.Path))
        {
            var pathLower = query.Path.ToLower();
            q = q.Where(log => log.Path.ToLower().Contains(pathLower));
        }

        if (!string.IsNullOrWhiteSpace(query.Method))
        {
            var methodUpper = query.Method.ToUpper();
            q = q.Where(log => log.Method == methodUpper);
        }

        if (query.StatusCode.HasValue)
            q = q.Where(log => log.StatusCode == query.StatusCode.Value);

        if (query.MinStatusCode.HasValue)
            q = q.Where(log => log.StatusCode >= query.MinStatusCode.Value);

        if (query.MaxStatusCode.HasValue)
            q = q.Where(log => log.StatusCode <= query.MaxStatusCode.Value);

        if (!string.IsNullOrWhiteSpace(query.IpAddress))
        {
            var ipLower = query.IpAddress.ToLower();
            q = q.Where(log => log.IpAddress != null && log.IpAddress.ToLower().Contains(ipLower));
        }

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var keyword = query.Keyword.ToLower();
            q = q.Where(log => log.Path.ToLower().Contains(keyword)
                || (log.UserName != null && log.UserName.ToLower().Contains(keyword)));
        }

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
            UniqueUsers = g.Where(log => log.UserId != null).Select(log => log.UserId).Distinct().Count(),
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
        // days <= 0 会把 expireDate 推到当前或未来，删掉全部（甚至尚未过期的）日志。
        // 与 IAuditOperationService.DeleteExpiredOperationsAsync 同一防护。
        if (days <= 0)
            return Fail("Days must be greater than 0", 400, ErrorCodes.VALIDATION_ERROR);

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

    /// <inheritdoc />
    public async Task<Result<AccessLogTrendDto>> GetAccessLogTrendAsync(
        TrendInterval interval,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default)
    {
        // 按天在数据库端聚合，避免加载全部原始记录到内存
        var dailyStats = await _accessLogRepository.AsQueryable()
            .AsNoTracking()
            .Where(l => l.CreationTime >= startDate && l.CreationTime <= endDate)
            .GroupBy(l => l.CreationTime.Date)
            .Select(g => new
            {
                Date = g.Key,
                TotalRequests = g.Count(),
                SuccessRequests = g.Count(l => l.StatusCode >= 200 && l.StatusCode < 300),
                ErrorRequests = g.Count(l => l.StatusCode >= 400),
                AverageResponseTime = g.Average(l => (double)l.ResponseTime)
            })
            .ToListAsync(cancellationToken);

        // 独立用户数不能按天汇总相加：跨多天活跃的同一用户会被重复计数（周/月区间尤甚）。
        // 取数据库端去重的 (天, 用户) 对，再在每个区间内二次去重 —— 行数受"天数 × 活跃用户"
        // 约束，仍远小于原始日志量。
        var activeUserDays = await _accessLogRepository.AsQueryable()
            .AsNoTracking()
            .Where(l => l.CreationTime >= startDate && l.CreationTime <= endDate && l.UserId != null)
            .Select(l => new { Date = l.CreationTime.Date, l.UserId })
            .Distinct()
            .ToListAsync(cancellationToken);

        // 分桶与标签统一走核心 TimeBucket。桶起点对齐到自然日 / ISO 周（周一起）/ 自然月，
        // 而不是「从 startDate 起每 N 天」—— 后者产出的"周"会随查询起点漂移（周三到周二算一周），
        // 标签却写着 ISO 周序号，两者对不上。
        var dataPoints = new List<AccessLogTrendDataPoint>();

        foreach (var bucketStart in TimeBucket.Enumerate(startDate, endDate, interval, endInclusive: false))
        {
            var bucketEnd = TimeBucket.Next(bucketStart, interval);

            // 从按天聚合结果中汇总当前区间的数据
            var bucketDays = dailyStats.Where(d => d.Date >= bucketStart && d.Date < bucketEnd).ToList();

            var totalRequests = bucketDays.Sum(d => d.TotalRequests);
            dataPoints.Add(new AccessLogTrendDataPoint
            {
                Label = TimeBucket.Label(bucketStart, interval),
                StartTime = bucketStart,
                TotalRequests = totalRequests,
                SuccessRequests = bucketDays.Sum(d => d.SuccessRequests),
                ErrorRequests = bucketDays.Sum(d => d.ErrorRequests),
                UniqueUsers = activeUserDays
                    .Where(u => u.Date >= bucketStart && u.Date < bucketEnd)
                    .Select(u => u.UserId)
                    .Distinct()
                    .Count(),
                AverageResponseTime = totalRequests > 0
                    ? bucketDays.Sum(d => d.AverageResponseTime * d.TotalRequests) / totalRequests
                    : 0
            });
        }

        return Ok(new AccessLogTrendDto
        {
            Interval = interval,
            StartDate = startDate,
            EndDate = endDate,
            DataPoints = dataPoints
        });
    }

    /// <inheritdoc />
    public async Task<Result<List<TopEndpointDto>>> GetTopEndpointsAsync(
        DateTime? startDate = null,
        DateTime? endDate = null,
        int top = 10,
        TopEndpointSortBy sortBy = TopEndpointSortBy.Hits,
        CancellationToken cancellationToken = default)
    {
        var query = _accessLogRepository.AsQueryable().AsNoTracking();

        if (startDate.HasValue)
            query = query.Where(l => l.CreationTime >= startDate.Value);
        if (endDate.HasValue)
            query = query.Where(l => l.CreationTime <= endDate.Value);

        // 按 Path+Method 分组聚合，在内存中排序取 Top N
        var grouped = await query
            .GroupBy(l => new { l.Path, l.Method })
            .Select(g => new TopEndpointDto
            {
                Path = g.Key.Path,
                Method = g.Key.Method,
                TotalHits = g.Count(),
                SuccessHits = g.Count(l => l.StatusCode >= 200 && l.StatusCode < 300),
                ErrorHits = g.Count(l => l.StatusCode >= 400),
                AverageResponseTime = g.Average(l => (double)l.ResponseTime),
                MaxResponseTime = g.Max(l => l.ResponseTime)
            })
            .ToListAsync(cancellationToken);

        // 计算错误率
        foreach (var item in grouped)
        {
            item.ErrorRate = item.TotalHits > 0 ? (double)item.ErrorHits / item.TotalHits : 0;
        }

        // 按指定方式排序取 Top N
        var sorted = sortBy switch
        {
            TopEndpointSortBy.AverageResponseTime => grouped.OrderByDescending(e => e.AverageResponseTime),
            TopEndpointSortBy.Errors => grouped.OrderByDescending(e => e.ErrorHits),
            _ => grouped.OrderByDescending(e => e.TotalHits)
        };

        return Ok(sorted.Take(top).ToList());
    }
}
