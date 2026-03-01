namespace Tnzi.Identity.Services;

/// <summary>
/// 登录安全服务实现
/// </summary>
public class LoginSecurityService : ApplicationService, ILoginSecurityService
{
    private const int ImpossibleTravelTimeWindowMinutes = 30;
    private const int FrequentAttemptsTimeWindowHours = 1;
    private const int FrequentAttemptsThreshold = 10;
    private const int RecentLoginsCount = 50;
    private const int FrequentIpAnalysisDays = 90;
    private const int FrequentIpTopCount = 10;

    private readonly IRepository<LoginLog, Guid>? _loginLogRepository;
    private readonly UserManager<User>? _userManager;
    private readonly AccountSecurityOptions _securityOptions;

    public LoginSecurityService(
        IServiceProvider serviceProvider,
        IOptions<IdentityOptions>? identityOptions = null,
        IRepository<LoginLog, Guid>? loginLogRepository = null,
        UserManager<User>? userManager = null)
        : base(serviceProvider)
    {
        _securityOptions = identityOptions?.Value.AccountSecurity ?? new AccountSecurityOptions();
        _loginLogRepository = loginLogRepository;
        _userManager = userManager;
    }

    /// <inheritdoc />
    public async Task<AbnormalLoginResult> DetectAbnormalLoginAsync(Guid userId, string? ipAddress, string? userAgent)
    {
        // 如果未启用异常登录检测，直接返回正常
        if (!_securityOptions.EnableAbnormalLoginDetection)
        {
            return AbnormalLoginResult.Normal();
        }

        if (_loginLogRepository == null)
        {
            return AbnormalLoginResult.Normal();
        }

        var result = new AbnormalLoginResult { IsAbnormal = false, RiskLevel = 0 };
        var currentFingerprint = GenerateDeviceFingerprint(userAgent);

        // 获取用户最近的成功登录记录
        var recentLogins = await _loginLogRepository
            .Where(l => l.UserId == userId && l.Status == LoginStatus.Success)
            .OrderByDescending(l => l.CreationTime)
            .Take(RecentLoginsCount)
            .ToListAsync();

        if (!recentLogins.Any())
        {
            // 首次登录，不算异常但可能需要通知
            return AbnormalLoginResult.Normal();
        }

        // 检测新IP
        if (!string.IsNullOrEmpty(ipAddress))
        {
            var knownIps = recentLogins
                .Where(l => !string.IsNullOrEmpty(l.IpAddress))
                .Select(l => l.IpAddress!)
                .Distinct()
                .ToList();

            if (!knownIps.Contains(ipAddress))
            {
                result.IsAbnormal = true;
                result.AbnormalTypes.Add(AbnormalLoginType.NewIpAddress);
                result.RiskLevel = Math.Max(result.RiskLevel, _securityOptions.NewIpRiskLevel);
                Logger.LogInformation("New IP address detected for user {UserId}: {IpAddress}", userId, ipAddress);
            }
        }

        // 检测新设备
        if (!string.IsNullOrEmpty(currentFingerprint))
        {
            var knownDevices = recentLogins
                .Where(l => !string.IsNullOrEmpty(l.UserAgent))
                .Select(l => GenerateDeviceFingerprint(l.UserAgent))
                .Distinct()
                .ToList();

            if (!knownDevices.Contains(currentFingerprint))
            {
                result.IsAbnormal = true;
                result.AbnormalTypes.Add(AbnormalLoginType.NewDevice);
                result.RiskLevel = Math.Max(result.RiskLevel, _securityOptions.NewDeviceRiskLevel);
                Logger.LogInformation("New device detected for user {UserId}", userId);
            }
        }

        // 检测不可能旅行（短时间内从不同IP登录，假设IP代表不同地理位置）
        var lastLogin = recentLogins.FirstOrDefault();
        if (lastLogin != null && !string.IsNullOrEmpty(ipAddress) && !string.IsNullOrEmpty(lastLogin.IpAddress))
        {
            var timeSinceLastLogin = DateTime.UtcNow - lastLogin.CreationTime;
            if (timeSinceLastLogin.TotalMinutes < ImpossibleTravelTimeWindowMinutes && lastLogin.IpAddress != ipAddress)
            {
                // 30分钟内从不同IP登录，可能是异常
                result.IsAbnormal = true;
                result.AbnormalTypes.Add(AbnormalLoginType.ImpossibleTravel);
                result.RiskLevel = Math.Max(result.RiskLevel, _securityOptions.ImpossibleTravelRiskLevel);
                result.Details = $"Login from different IP within {timeSinceLastLogin.TotalMinutes:F0} minutes";
                Logger.LogWarning("Impossible travel detected for user {UserId}: last login from {LastIp}, current from {CurrentIp}",
                    userId, lastLogin.IpAddress, ipAddress);
            }
        }

        // 检测频繁登录尝试
        var recentAttempts = await _loginLogRepository
            .Where(l => l.UserId == userId && l.CreationTime > DateTime.UtcNow.AddHours(-FrequentAttemptsTimeWindowHours))
            .CountAsync();

        if (recentAttempts > FrequentAttemptsThreshold)
        {
            result.IsAbnormal = true;
            result.AbnormalTypes.Add(AbnormalLoginType.FrequentAttempts);
            result.RiskLevel = Math.Max(result.RiskLevel, _securityOptions.FrequentAttemptsRiskLevel);
            result.Details = $"{recentAttempts} login attempts in the last hour";
            Logger.LogWarning("Frequent login attempts detected for user {UserId}: {Count} attempts", userId, recentAttempts);
        }

        // 根据风险等级确定建议操作（使用配置的阈值）
        if (result.RiskLevel >= _securityOptions.HighRiskThreshold)
        {
            result.RecommendedAction = AbnormalLoginAction.RequireVerification;
        }
        else if (result.RiskLevel >= _securityOptions.MediumRiskThreshold)
        {
            result.RecommendedAction = AbnormalLoginAction.Notify;
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<LoginLogDto>> GetRecentLoginsAsync(Guid userId, int count = 10)
    {
        if (_loginLogRepository == null)
        {
            return Enumerable.Empty<LoginLogDto>();
        }

        var logs = await _loginLogRepository
            .Where(l => l.UserId == userId)
            .OrderByDescending(l => l.CreationTime)
            .Take(count)
            .ProjectTo<LoginLog, LoginLogDto>()
            .ToListAsync();

        return logs;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<string>> GetFrequentIpAddressesAsync(Guid userId)
    {
        if (_loginLogRepository == null)
        {
            return Enumerable.Empty<string>();
        }

        // 获取最近90天内登录次数最多的IP
        var frequentIps = await _loginLogRepository
            .Where(l => l.UserId == userId &&
                        l.Status == LoginStatus.Success &&
                        l.CreationTime > DateTime.UtcNow.AddDays(-FrequentIpAnalysisDays) &&
                        !string.IsNullOrEmpty(l.IpAddress))
            .GroupBy(l => l.IpAddress)
            .Select(g => new { IpAddress = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(FrequentIpTopCount)
            .ToListAsync();

        return frequentIps.Select(x => x.IpAddress!);
    }

    /// <inheritdoc />
    public string GenerateDeviceFingerprint(string? userAgent, string? additionalInfo = null)
    {
        if (string.IsNullOrEmpty(userAgent))
        {
            return string.Empty;
        }

        var input = userAgent;
        if (!string.IsNullOrEmpty(additionalInfo))
        {
            input += additionalInfo;
        }

        // 使用 SHA256 生成指纹
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(bytes)[..16]; // 取前16个字符作为指纹
    }

    /// <inheritdoc />
    public async Task<Result<SecurityOverviewDto>> GetSecurityOverviewAsync(int hours = 24)
    {
        if (_loginLogRepository == null)
        {
            return Fail<SecurityOverviewDto>("Login log repository is not available");
        }

        var since = DateTime.UtcNow.AddHours(-hours);

        var logs = await _loginLogRepository
            .Where(l => l.CreationTime >= since)
            .GroupBy(l => l.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        var totalAttempts = logs.Sum(g => g.Count);
        var successCount = logs.Where(g => g.Status == LoginStatus.Success).Sum(g => g.Count);
        var failedCount = totalAttempts - successCount;

        // 统计不同用户和IP
        var distinctUsers = await _loginLogRepository
            .Where(l => l.CreationTime >= since && l.UserId != null)
            .Select(l => l.UserId)
            .Distinct()
            .CountAsync();

        var distinctIps = await _loginLogRepository
            .Where(l => l.CreationTime >= since && !string.IsNullOrEmpty(l.IpAddress))
            .Select(l => l.IpAddress)
            .Distinct()
            .CountAsync();

        // 锁定用户数
        var lockedOutUsers = 0;
        if (_userManager != null)
        {
            lockedOutUsers = await _userManager.Users
                .Where(u => u.LockoutEnd != null && u.LockoutEnd > DateTimeOffset.UtcNow)
                .CountAsync();
        }

        return Ok(new SecurityOverviewDto
        {
            TimeRangeHours = hours,
            TotalLoginAttempts = totalAttempts,
            SuccessfulLogins = successCount,
            FailedLogins = failedCount,
            FailureRate = totalAttempts > 0 ? Math.Round((double)failedCount / totalAttempts * 100, 1) : 0,
            DistinctUsers = distinctUsers,
            DistinctIpAddresses = distinctIps,
            LockedOutUsers = lockedOutUsers
        });
    }

    /// <inheritdoc />
    public async Task<Result<IEnumerable<UserFailedLoginSummaryDto>>> GetUsersWithFrequentFailuresAsync(int hours = 24, int minFailures = 3)
    {
        if (_loginLogRepository == null)
        {
            return Fail<IEnumerable<UserFailedLoginSummaryDto>>("Login log repository is not available");
        }

        var since = DateTime.UtcNow.AddHours(-hours);

        // 查询有频繁失败的用户
        var failedGroups = await _loginLogRepository
            .Where(l => l.CreationTime >= since && l.Status == LoginStatus.Failed && l.UserId != null)
            .GroupBy(l => l.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                FailureCount = g.Count(),
                LastFailureTime = g.Max(l => l.CreationTime)
            })
            .Where(g => g.FailureCount >= minFailures)
            .OrderByDescending(g => g.FailureCount)
            .ToListAsync();

        if (!failedGroups.Any())
        {
            return Ok(Enumerable.Empty<UserFailedLoginSummaryDto>());
        }

        var userIds = failedGroups.Select(g => g.UserId!.Value).ToList();

        // 获取用户信息
        var users = _userManager != null
            ? await _userManager.Users.Where(u => userIds.Contains(u.Id)).ToListAsync()
            : new List<User>();

        // 获取失败的IP地址
        var failedIps = await _loginLogRepository
            .Where(l => l.CreationTime >= since && l.Status == LoginStatus.Failed &&
                        l.UserId != null && userIds.Contains(l.UserId.Value) &&
                        !string.IsNullOrEmpty(l.IpAddress))
            .Select(l => new { l.UserId, l.IpAddress })
            .ToListAsync();

        var ipsByUser = failedIps
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key!.Value, g => g.Select(x => x.IpAddress!).Distinct().ToList());

        var results = failedGroups.Select(g =>
        {
            var user = users.FirstOrDefault(u => u.Id == g.UserId);
            return new UserFailedLoginSummaryDto
            {
                UserId = g.UserId!.Value,
                UserName = user?.UserName,
                Email = user?.Email,
                FailureCount = g.FailureCount,
                LastFailureTime = g.LastFailureTime,
                IpAddresses = ipsByUser.GetValueOrDefault(g.UserId!.Value, new List<string>()),
                IsLockedOut = user?.LockoutEnd != null && user.LockoutEnd > DateTimeOffset.UtcNow
            };
        });

        return Ok(results);
    }
}

