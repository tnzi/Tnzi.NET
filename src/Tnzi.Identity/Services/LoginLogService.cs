

namespace Tnzi.Identity.Services;

/// <summary>
/// 登录日志服务实现
/// </summary>
public class LoginLogService : ApplicationService, ILoginLogService, ILoginLogInternalService
{
    private const int DefaultExpiredLogsDays = 90;
    private const int DefaultRecentLogsCount = 10;
    private const int DefaultPageIndex = 1;
    private const int DefaultPageSize = 20;
    private const int DefaultFailedAttemptsTop = 100;

    private readonly IRepository<LoginLog, Guid> _repository;
    private readonly ILoginLogSender _sender;

    /// <summary>
    /// 初始化一个<see cref="LoginLogService"/>类型的新实例
    /// </summary>
    public LoginLogService(
        IRepository<LoginLog, Guid> repository,
        ILoginLogSender sender,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
        _sender = Check.NotNull(sender);
    }

    /// <summary>
    /// 记录登录日志
    /// </summary>
    public async Task<Guid> LogAsync(Guid? userId, string? userName, string? ipAddress, string? userAgent, LoginStatus status, string? failureReason = null)
    {
        var log = new LoginLog
        {
            UserId = userId,
            UserName = userName,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Status = status,
            FailureReason = failureReason,
            CreationTime = DateTime.UtcNow
        };

        await _sender.SendAsync(log);
        return log.Id;
    }

    /// <summary>
    /// 获取用户的登录日志（返回原始实体）
    /// </summary>
    public async Task<IPagedList<LoginLog>> GetPagedLogsAsync(Guid userId, int pageIndex = DefaultPageIndex, int pageSize = DefaultPageSize)
    {
        var query = _repository.Where(ll => ll.UserId == userId)
            .OrderByDescending(ll => ll.CreationTime);

        return await query.CreateAsync(pageIndex, pageSize);
    }

    /// <summary>
    /// 获取最近的登录日志（支持用户特定或全局）
    /// </summary>
    public async Task<IEnumerable<LoginLog>> GetRecentLogsAsync(Guid? userId = null, int count = DefaultRecentLogsCount)
    {
        // 验证count参数
        if (count <= 0)
        {
            Logger.LogWarning("Invalid count parameter: {Count}, using default value {Default}", count, DefaultRecentLogsCount);
            count = DefaultRecentLogsCount;
        }

        var query = _repository.AsQueryable();

        if (userId.HasValue)
        {
            query = query.Where(ll => ll.UserId == userId.Value);
        }

        return await query
            .OrderByDescending(ll => ll.CreationTime)
            .Take(count)
            .ToListAsync();
    }

    /// <summary>
    /// 删除过期日志
    /// 优化：直接执行删除操作，避免先Count再Delete的两次查询
    /// </summary>
    public async Task<Result<int>> DeleteExpiredLogsAsync(int days = DefaultExpiredLogsDays)
    {
        // 验证days参数
        if (days <= 0)
        {
            return Fail<int>("Days must be greater than 0", 400, ErrorCodes.VALIDATION_ERROR);
        }

        var expireDate = DateTime.UtcNow.AddDays(-days);

        // 直接执行删除操作，使用ExecuteDeleteAsync获取受影响的行数
        var count = await _repository
            .AsQueryable()
            .Where(ll => ll.CreationTime < expireDate)
            .ExecuteDeleteAsync();

        LogInformation("Deleted {Count} expired login logs (older than {Days} days)", count, days);
        return Ok(count);
    }

    /// <summary>
    /// 按IP地址查询登录日志
    /// </summary>
    public async Task<IPagedList<LoginLog>> GetLogsByIpAsync(string ipAddress, int pageIndex = DefaultPageIndex, int pageSize = DefaultPageSize)
    {
        Check.NotNullOrWhiteSpace(ipAddress);

        var query = _repository
            .Where(ll => ll.IpAddress == ipAddress)
            .OrderByDescending(ll => ll.CreationTime);

        return await query.CreateAsync(pageIndex, pageSize);
    }

    /// <summary>
    /// 按时间范围查询登录日志
    /// </summary>
    public async Task<IPagedList<LoginLog>> GetLogsByDateRangeAsync(DateTime startDate, DateTime endDate, int pageIndex = DefaultPageIndex, int pageSize = DefaultPageSize)
    {
        // 验证日期范围
        Check.Condition(startDate <= endDate, "Start date must be less than or equal to end date");

        var query = _repository
            .Where(ll => ll.CreationTime >= startDate && ll.CreationTime <= endDate)
            .OrderByDescending(ll => ll.CreationTime);

        return await query.CreateAsync(pageIndex, pageSize);
    }

    /// <summary>
    /// 查询登录日志（支持多条件）
    /// </summary>
    public async Task<IPagedList<LoginLog>> QueryLogsAsync(LoginLogQueryDto query)
    {
        var queryable = _repository.AsQueryable();

        if (query.UserId.HasValue)
        {
            queryable = queryable.Where(ll => ll.UserId == query.UserId.Value);
        }

        if (!string.IsNullOrEmpty(query.IpAddress))
        {
            queryable = queryable.Where(ll => ll.IpAddress == query.IpAddress);
        }

        if (query.StartDate.HasValue)
        {
            queryable = queryable.Where(ll => ll.CreationTime >= query.StartDate.Value);
        }

        if (query.EndDate.HasValue)
        {
            queryable = queryable.Where(ll => ll.CreationTime <= query.EndDate.Value);
        }

        if (query.Status.HasValue)
        {
            queryable = queryable.Where(ll => ll.Status == query.Status.Value);
        }

        queryable = queryable.OrderByDescending(ll => ll.CreationTime);

        return await queryable.CreateAsync(query);
    }

    /// <summary>
    /// 获取登录统计
    /// </summary>
    public async Task<LoginStatisticsDto> GetLoginStatisticsAsync(Guid? userId = null, DateTime? startDate = null, DateTime? endDate = null)
    {
        var query = _repository.AsQueryable();

        if (userId.HasValue)
        {
            query = query.Where(ll => ll.UserId == userId.Value);
        }

        if (startDate.HasValue)
        {
            query = query.Where(ll => ll.CreationTime >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(ll => ll.CreationTime <= endDate.Value);
        }

        // 检查是否有数据
        if (!await query.AnyAsync())
        {
            return new LoginStatisticsDto();
        }

        // 单次聚合查询
        var stats = await query.GroupBy(o => 1).Select(g => new
        {
            TotalLogins = g.Count(),
            SuccessfulLogins = g.Count(ll => ll.Status == LoginStatus.Success),
            FailedLogins = g.Count(ll => ll.Status == LoginStatus.Failed),
            UniqueIpCount = g.Where(ll => ll.IpAddress != null).Select(ll => ll.IpAddress).Distinct().Count()
        }).FirstOrDefaultAsync();

        var lastLogin = await query
            .Where(ll => ll.Status == LoginStatus.Success)
            .OrderByDescending(ll => ll.CreationTime)
            .Select(ll => new { ll.CreationTime, ll.IpAddress })
            .FirstOrDefaultAsync();

        return new LoginStatisticsDto
        {
            TotalLogins = stats?.TotalLogins ?? 0,
            SuccessfulLogins = stats?.SuccessfulLogins ?? 0,
            FailedLogins = stats?.FailedLogins ?? 0,
            UniqueIpCount = stats?.UniqueIpCount ?? 0,
            LastLoginTime = lastLogin?.CreationTime,
            LastLoginIp = lastLogin?.IpAddress
        };
    }

    /// <summary>
    /// 根据ID获取登录日志
    /// </summary>
    /// <param name="id">日志ID</param>
    /// <returns>登录日志DTO</returns>
    public async Task<Result<LoginLogDto>> GetByIdAsync(Guid id)
    {
        var log = await _repository.FindAsync(id);
        if (log == null)
        {
            return Fail<LoginLogDto>("Login log not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        var dto = log.MapTo<LoginLogDto>();
        return Ok(dto);
    }

    /// <summary>
    /// 获取登录日志列表（分页）
    /// </summary>
    /// <param name="query">查询条件</param>
    /// <returns>分页的登录日志列表</returns>
    public async Task<Result<IPagedList<LoginLogDto>>> GetPagedListAsync(LoginLogQueryDto query)
    {
        var queryable = _repository.AsQueryable();

        if (query.UserId.HasValue)
        {
            queryable = queryable.Where(ll => ll.UserId == query.UserId.Value);
        }

        if (!string.IsNullOrEmpty(query.IpAddress))
        {
            queryable = queryable.Where(ll => ll.IpAddress == query.IpAddress);
        }

        if (query.StartDate.HasValue)
        {
            queryable = queryable.Where(ll => ll.CreationTime >= query.StartDate.Value);
        }

        if (query.EndDate.HasValue)
        {
            queryable = queryable.Where(ll => ll.CreationTime <= query.EndDate.Value);
        }

        // 处理 IsSuccess 参数（兼容Controller参数）
        if (query.IsSuccess.HasValue)
        {
            var status = query.IsSuccess.Value ? LoginStatus.Success : LoginStatus.Failed;
            queryable = queryable.Where(ll => ll.Status == status);
        }
        else if (query.Status.HasValue)
        {
            queryable = queryable.Where(ll => ll.Status == query.Status.Value);
        }

        queryable = queryable.OrderByDescending(ll => ll.CreationTime);

        var paged = await queryable
            .ProjectTo<LoginLog, LoginLogDto>()
            .CreateAsync(query);

        return Ok(paged);
    }

    /// <summary>
    /// 获取用户的登录日志
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="startDate">开始日期（可选）</param>
    /// <param name="endDate">结束日期（可选）</param>
    /// <param name="isSuccess">是否成功（可选）</param>
    /// <returns>登录日志列表</returns>
    public async Task<Result<IEnumerable<LoginLogDto>>> GetUserLoginLogsAsync(Guid userId, DateTime? startDate = null, DateTime? endDate = null, bool? isSuccess = null)
    {
        var query = _repository.Where(ll => ll.UserId == userId);

        if (startDate.HasValue)
        {
            query = query.Where(ll => ll.CreationTime >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(ll => ll.CreationTime <= endDate.Value);
        }

        if (isSuccess.HasValue)
        {
            var status = isSuccess.Value ? LoginStatus.Success : LoginStatus.Failed;
            query = query.Where(ll => ll.Status == status);
        }

        var logs = await query
            .OrderByDescending(ll => ll.CreationTime)
            .ToListAsync();

        var dtos = logs.MapToList<LoginLogDto>();
        return Ok<IEnumerable<LoginLogDto>>(dtos);
    }

    /// <summary>
    /// 获取登录统计信息
    /// </summary>
    /// <param name="startDate">开始日期（可选）</param>
    /// <param name="endDate">结束日期（可选）</param>
    /// <returns>登录统计信息</returns>
    public async Task<Result<LoginStatisticsDto>> GetStatisticsAsync(DateTime? startDate = null, DateTime? endDate = null)
    {
        var statistics = await GetLoginStatisticsAsync(null, startDate, endDate);
        return Ok(statistics);
    }

    /// <summary>
    /// 获取用户的登录统计
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="startDate">开始日期（可选）</param>
    /// <param name="endDate">结束日期（可选）</param>
    /// <returns>用户登录统计信息</returns>
    public async Task<Result<UserLoginStatisticsDto>> GetUserStatisticsAsync(Guid userId, DateTime? startDate = null, DateTime? endDate = null)
    {
        var query = _repository.Where(ll => ll.UserId == userId);

        if (startDate.HasValue)
        {
            query = query.Where(ll => ll.CreationTime >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(ll => ll.CreationTime <= endDate.Value);
        }

        // 单次聚合查询替代 3 个独立 COUNT 查询
        var stats = await query.GroupBy(o => 1).Select(g => new
        {
            TotalLogins = g.Count(),
            SuccessfulLogins = g.Count(ll => ll.Status == LoginStatus.Success),
            FailedLogins = g.Count(ll => ll.Status == LoginStatus.Failed)
        }).FirstOrDefaultAsync();

        var lastLogin = await query
            .Where(ll => ll.Status == LoginStatus.Success)
            .OrderByDescending(ll => ll.CreationTime)
            .Select(ll => new { ll.CreationTime, ll.IpAddress })
            .FirstOrDefaultAsync();

        var statistics = new UserLoginStatisticsDto
        {
            UserId = userId,
            TotalLogins = stats?.TotalLogins ?? 0,
            SuccessfulLogins = stats?.SuccessfulLogins ?? 0,
            FailedLogins = stats?.FailedLogins ?? 0,
            LastLoginTime = lastLogin?.CreationTime,
            LastLoginIp = lastLogin?.IpAddress
        };

        return Ok(statistics);
    }

    /// <summary>
    /// Get daily login trend data for dashboards and charts
    /// </summary>
    public async Task<Result<IEnumerable<LoginTrendItem>>> GetLoginTrendAsync(DateTime startDate, DateTime endDate, Guid? userId = null)
    {
        if (startDate > endDate)
            return Fail<IEnumerable<LoginTrendItem>>("Start date must be before or equal to end date", 400, ErrorCodes.VALIDATION_ERROR);

        // Limit to 365 days max to prevent excessive queries
        if ((endDate - startDate).TotalDays > 365)
            return Fail<IEnumerable<LoginTrendItem>>("Date range cannot exceed 365 days", 400, ErrorCodes.VALIDATION_ERROR);

        var query = _repository.AsQueryable()
            .Where(ll => ll.CreationTime >= startDate && ll.CreationTime <= endDate);

        if (userId.HasValue)
        {
            query = query.Where(ll => ll.UserId == userId.Value);
        }

        var dailyData = await query
            .GroupBy(ll => ll.CreationTime.Date)
            .Select(g => new LoginTrendItem
            {
                Date = g.Key,
                TotalLogins = g.Count(),
                SuccessfulLogins = g.Count(ll => ll.Status == LoginStatus.Success),
                FailedLogins = g.Count(ll => ll.Status == LoginStatus.Failed),
                UniqueUsers = g.Where(ll => ll.UserId != null).Select(ll => ll.UserId).Distinct().Count()
            })
            .OrderBy(t => t.Date)
            .ToListAsync();

        // Fill in missing dates with zero values
        var result = new List<LoginTrendItem>();
        for (var date = startDate.Date; date <= endDate.Date; date = date.AddDays(1))
        {
            var existing = dailyData.FirstOrDefault(d => d.Date == date);
            result.Add(existing ?? new LoginTrendItem { Date = date });
        }

        return Ok<IEnumerable<LoginTrendItem>>(result);
    }

    /// <summary>
    /// 获取失败的登录尝试
    /// </summary>
    /// <param name="startDate">开始日期（可选）</param>
    /// <param name="endDate">结束日期（可选）</param>
    /// <param name="top">返回数量（默认100）</param>
    /// <returns>失败的登录尝试列表</returns>
    public async Task<Result<IEnumerable<LoginLogDto>>> GetFailedAttemptsAsync(DateTime? startDate = null, DateTime? endDate = null, int top = DefaultFailedAttemptsTop)
    {
        // 验证top参数
        if (top <= 0)
        {
            Logger.LogWarning("Invalid top parameter: {Top}, using default value {Default}", top, DefaultFailedAttemptsTop);
            top = DefaultFailedAttemptsTop;
        }

        var query = _repository.Where(ll => ll.Status == LoginStatus.Failed);

        if (startDate.HasValue)
        {
            query = query.Where(ll => ll.CreationTime >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(ll => ll.CreationTime <= endDate.Value);
        }

        var logs = await query
            .OrderByDescending(ll => ll.CreationTime)
            .Take(top)
            .ToListAsync();

        var dtos = logs.MapToList<LoginLogDto>();
        return Ok<IEnumerable<LoginLogDto>>(dtos);
    }
}