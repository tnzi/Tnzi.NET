namespace Tnzi.System.Services;

/// <summary>
/// 访问日志服务实现
/// </summary>
public class AccessLogService : ApplicationService, IAccessLogService
{
    private readonly IRepository<AccessLog, Guid> _accessLogRepository;
    private readonly IAccessLogSender _accessLogSender;
    private readonly IOptionsMonitor<AccessLogOptions>? _accessLogOptions;
    private readonly ICurrentTenant? _currentTenant;
    private readonly IOptions<MultiTenancyOptions>? _multiTenancyOptions;

    /// <summary>初始化一个 <see cref="AccessLogService"/> 实例。</summary>
    /// <param name="serviceProvider">服务提供程序。</param>
    /// <param name="accessLogRepository">访问日志仓储。</param>
    /// <param name="accessLogSender">后台队列。</param>
    /// <param name="accessLogOptions">
    /// 采集开关（<c>System:AccessLog</c>），只用来在统计结果里如实报出「这个部署有没有在采集」。
    /// 可选：拿不到时按未采集报。
    /// </param>
    /// <param name="currentTenant">当前租户访问器；读取与删除面按调用者的租户收口（见 <see cref="CallerTenantScope"/>）。</param>
    /// <param name="multiTenancyOptions">多租户开关；关闭时 <c>TenantId</c> 列不存在，谓词一行不加。</param>
    public AccessLogService(
        IServiceProvider serviceProvider,
        IRepository<AccessLog, Guid> accessLogRepository,
        IAccessLogSender accessLogSender,
        IOptionsMonitor<AccessLogOptions>? accessLogOptions = null,
        ICurrentTenant? currentTenant = null,
        IOptions<MultiTenancyOptions>? multiTenancyOptions = null)
        : base(serviceProvider)
    {
        _accessLogRepository = Check.NotNull(accessLogRepository);
        _accessLogSender = Check.NotNull(accessLogSender);
        _accessLogOptions = accessLogOptions;
        _currentTenant = currentTenant;
        _multiTenancyOptions = multiTenancyOptions;
    }

    private bool CaptureEnabled => _accessLogOptions?.CurrentValue.Enabled ?? false;

    /// <summary>调用者的租户作用域（每次现算：租户上下文可在请求内被 <c>ICurrentTenant.Change</c> 切换）。</summary>
    private CallerTenantScope TenantScope => CallerTenantScope.Resolve(_multiTenancyOptions, _currentTenant, CurrentUser);

    /// <summary>
    /// 全部读取与删除面的起点：调用者有租户则钉在本租户，宿主看全部。
    /// 多租户关闭时原样返回：那时 <c>TenantId</c> 被 Ignore，引用它会让查询翻译失败。
    /// </summary>
    private IQueryable<AccessLog> ScopedLogs()
    {
        var scope = TenantScope;
        if (!scope.IsPinned)
            return _accessLogRepository.AsQueryable();

        var tenantId = scope.TenantId;
        return _accessLogRepository.Where(log => log.TenantId == tenantId);
    }

    /// <inheritdoc />
    /// <remarks>
    /// 手工上报的这一条同样记在<b>调用者的租户</b>名下，不认请求体里的任何租户与主键：主键由落库时生成
    /// （带一个已存在的 id 会让同一批的整条 INSERT 被拒）；租户取自身份，否则一个租户管理员可以往别的租户名下写日志。
    /// 路径与方法在这里就校验：它们是必填列，空值或超列宽会在后台批量落库时连带拖垮同一批的其它记录。
    /// </remarks>
    public async Task<Result> LogAccessAsync(AccessLogDto log)
    {
        Check.NotNull(log);

        if (string.IsNullOrWhiteSpace(log.Path) || log.Path.Length > AccessLogColumns.PathMaxLength)
            return Fail($"Path is required and must be at most {AccessLogColumns.PathMaxLength} characters", 400, ErrorCodes.VALIDATION_ERROR);

        if (string.IsNullOrWhiteSpace(log.Method) || log.Method.Length > AccessLogColumns.MethodMaxLength)
            return Fail($"Method is required and must be at most {AccessLogColumns.MethodMaxLength} characters", 400, ErrorCodes.VALIDATION_ERROR);

        var entry = log.MapTo<AccessLogDto>();
        entry.Id = Guid.Empty;
        entry.TenantId = TenantScope.TenantId;

        // 仅发送到后台队列，不阻塞当前请求
        await _accessLogSender.SendAsync(entry);
        return Ok("Access log sent to background queue");
    }

    /// <inheritdoc />
    public async Task<Result<IPagedList<AccessLogDto>>> GetAccessLogsAsync(AccessLogQueryDto query)
    {
        Check.NotNull(query);

        var q = ScopedLogs();

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
        // 别的租户的行等同于不存在（404，不泄露存在性）。
        var log = await ScopedLogs()
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id);

        if (log == null)
            return Fail<AccessLogDto>("Access log not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);

        return Ok(log.MapTo<AccessLogDto>());
    }

    /// <inheritdoc />
    public async Task<Result<AccessLogStatisticsDto>> GetAccessLogStatisticsAsync(DateTime? startTime = null, DateTime? endTime = null)
    {
        var query = ScopedLogs();

        if (startTime.HasValue)
            query = query.Where(log => log.CreationTime >= startTime.Value);

        if (endTime.HasValue)
            query = query.Where(log => log.CreationTime <= endTime.Value);

        // 检查是否有数据
        if (!await query.AnyAsync())
            return Ok(new AccessLogStatisticsDto { CaptureEnabled = CaptureEnabled });

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
            return Ok(new AccessLogStatisticsDto { CaptureEnabled = CaptureEnabled });

        var statistics = new AccessLogStatisticsDto
        {
            CaptureEnabled = CaptureEnabled,
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

        // 保留期清理是部署级的动作，不是租户能替宿主做的事：租户内的调用者 403，一行不删。
        // 曾对全表按时间删除，一个租户管理员一次 days=1 就抹掉全部租户的访问日志。
        if (TenantScope.IsPinned)
            return Fail("Deleting expired access logs is a host-level operation and is not available from inside a tenant", 403, ErrorCodes.FORBIDDEN);

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
        // 别的租户的 id 当作不存在：只删得到调用者自己租户的行。
        var logs = await ScopedLogs()
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
        var dailyStats = await ScopedLogs()
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
        var activeUserDays = await ScopedLogs()
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

    /// <summary>Top 端点统计一次最多返回的行数（请求的 top 夹到 1 到这个数之间）。</summary>
    public const int MaxTopEndpoints = 100;

    /// <inheritdoc />
    public async Task<Result<List<TopEndpointDto>>> GetTopEndpointsAsync(
        DateTime? startDate = null,
        DateTime? endDate = null,
        int top = 10,
        TopEndpointSortBy sortBy = TopEndpointSortBy.Hits,
        CancellationToken cancellationToken = default)
    {
        var query = ScopedLogs().AsNoTracking();

        if (startDate.HasValue)
            query = query.Where(l => l.CreationTime >= startDate.Value);
        if (endDate.HasValue)
            query = query.Where(l => l.CreationTime <= endDate.Value);

        // ★ 按 Path+Method 分组聚合后，排序与取前 N 都在数据库端做，只把 N 行拉回来。
        //   此前全部分组物化进内存再排序：Path 是原始请求路径（带资源 id），分组数约等于不同 URL 数，
        //   开着采集几周之后，一次不带时间范围的调用要把几十万个分组拉进 API 进程，只为返回 10 行。
        var grouped = query
            .GroupBy(l => new { l.Path, l.Method })
            .Select(g => new
            {
                g.Key.Path,
                g.Key.Method,
                TotalHits = g.Count(),
                SuccessHits = g.Count(l => l.StatusCode >= 200 && l.StatusCode < 300),
                ErrorHits = g.Count(l => l.StatusCode >= 400),
                AverageResponseTime = g.Average(l => (double)l.ResponseTime),
                MaxResponseTime = g.Max(l => l.ResponseTime)
            });

        var sorted = sortBy switch
        {
            TopEndpointSortBy.AverageResponseTime => grouped.OrderByDescending(e => e.AverageResponseTime),
            TopEndpointSortBy.Errors => grouped.OrderByDescending(e => e.ErrorHits),
            _ => grouped.OrderByDescending(e => e.TotalHits)
        };

        var rows = await sorted
            .Take(Math.Clamp(top, 1, MaxTopEndpoints))
            .ToListAsync(cancellationToken);

        return Ok(rows.Select(r => new TopEndpointDto
        {
            Path = r.Path,
            Method = r.Method,
            TotalHits = r.TotalHits,
            SuccessHits = r.SuccessHits,
            ErrorHits = r.ErrorHits,
            AverageResponseTime = r.AverageResponseTime,
            MaxResponseTime = r.MaxResponseTime,
            ErrorRate = r.TotalHits > 0 ? (double)r.ErrorHits / r.TotalHits : 0
        }).ToList());
    }
}
