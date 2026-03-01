
namespace Tnzi.Identity.Services;

/// <summary>
/// 登录安全服务接口
/// </summary>
public interface ILoginSecurityService
{
    /// <summary>
    /// 检测异常登录
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="ipAddress">IP地址</param>
    /// <param name="userAgent">User Agent</param>
    /// <returns>异常登录检测结果</returns>
    Task<AbnormalLoginResult> DetectAbnormalLoginAsync(Guid userId, string? ipAddress, string? userAgent);

    /// <summary>
    /// 获取用户最近的登录记录
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="count">记录数量</param>
    /// <returns>登录记录列表</returns>
    Task<IEnumerable<LoginLogDto>> GetRecentLoginsAsync(Guid userId, int count = 10);

    /// <summary>
    /// 获取用户的常用IP列表
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>常用IP列表</returns>
    Task<IEnumerable<string>> GetFrequentIpAddressesAsync(Guid userId);

    /// <summary>
    /// 生成设备指纹
    /// </summary>
    /// <param name="userAgent">User Agent</param>
    /// <param name="additionalInfo">附加信息</param>
    /// <returns>设备指纹</returns>
    string GenerateDeviceFingerprint(string? userAgent, string? additionalInfo = null);

    /// <summary>
    /// 获取登录安全概览（管理员仪表盘）
    /// </summary>
    /// <param name="hours">统计时间范围（小时，默认24小时）</param>
    /// <returns>安全概览数据</returns>
    Task<Result<SecurityOverviewDto>> GetSecurityOverviewAsync(int hours = 24);

    /// <summary>
    /// 获取近期有频繁登录失败的用户列表
    /// </summary>
    /// <param name="hours">统计时间范围（小时，默认24小时）</param>
    /// <param name="minFailures">最小失败次数阈值（默认3次）</param>
    /// <returns>频繁登录失败的用户列表</returns>
    Task<Result<IEnumerable<UserFailedLoginSummaryDto>>> GetUsersWithFrequentFailuresAsync(int hours = 24, int minFailures = 3);
}

/// <summary>
/// 异常登录检测结果
/// </summary>
public class AbnormalLoginResult
{
    /// <summary>
    /// 是否为异常登录
    /// </summary>
    public bool IsAbnormal { get; set; }

    /// <summary>
    /// 异常类型列表
    /// </summary>
    public List<AbnormalLoginType> AbnormalTypes { get; set; } = new();

    /// <summary>
    /// 风险等级（0-100）
    /// </summary>
    public int RiskLevel { get; set; }

    /// <summary>
    /// 详细信息
    /// </summary>
    public string? Details { get; set; }

    /// <summary>
    /// 建议的操作
    /// </summary>
    public AbnormalLoginAction RecommendedAction { get; set; } = AbnormalLoginAction.None;

    /// <summary>
    /// 创建正常登录结果
    /// </summary>
    public static AbnormalLoginResult Normal() => new() { IsAbnormal = false, RiskLevel = 0 };

    /// <summary>
    /// 创建异常登录结果
    /// </summary>
    public static AbnormalLoginResult Abnormal(AbnormalLoginType type, int riskLevel, string? details = null)
        => new()
        {
            IsAbnormal = true,
            AbnormalTypes = new List<AbnormalLoginType> { type },
            RiskLevel = riskLevel,
            Details = details,
            RecommendedAction = riskLevel > 70 ? AbnormalLoginAction.RequireVerification : AbnormalLoginAction.Notify
        };
}

/// <summary>
/// 异常登录类型
/// </summary>
public enum AbnormalLoginType
{
    /// <summary>
    /// 新设备登录
    /// </summary>
    NewDevice,

    /// <summary>
    /// 新IP地址登录
    /// </summary>
    NewIpAddress,

    /// <summary>
    /// 异地登录（IP地理位置变更）
    /// </summary>
    LocationChange,

    /// <summary>
    /// 短时间内多地登录
    /// </summary>
    ImpossibleTravel,

    /// <summary>
    /// 频繁登录尝试
    /// </summary>
    FrequentAttempts,

    /// <summary>
    /// 异常时间登录
    /// </summary>
    UnusualTime
}

/// <summary>
/// 异常登录建议操作
/// </summary>
public enum AbnormalLoginAction
{
    /// <summary>
    /// 无需操作
    /// </summary>
    None,

    /// <summary>
    /// 发送通知
    /// </summary>
    Notify,

    /// <summary>
    /// 要求二次验证
    /// </summary>
    RequireVerification,

    /// <summary>
    /// 阻止登录
    /// </summary>
    Block
}

/// <summary>
/// 登录安全概览数据
/// </summary>
public class SecurityOverviewDto
{
    /// <summary>
    /// 统计时间范围（小时）
    /// </summary>
    public int TimeRangeHours { get; set; }

    /// <summary>
    /// 总登录次数
    /// </summary>
    public int TotalLoginAttempts { get; set; }

    /// <summary>
    /// 成功登录次数
    /// </summary>
    public int SuccessfulLogins { get; set; }

    /// <summary>
    /// 失败登录次数
    /// </summary>
    public int FailedLogins { get; set; }

    /// <summary>
    /// 失败率（0-100）
    /// </summary>
    public double FailureRate { get; set; }

    /// <summary>
    /// 不同用户数
    /// </summary>
    public int DistinctUsers { get; set; }

    /// <summary>
    /// 不同IP数
    /// </summary>
    public int DistinctIpAddresses { get; set; }

    /// <summary>
    /// 当前锁定的用户数
    /// </summary>
    public int LockedOutUsers { get; set; }
}

/// <summary>
/// 用户登录失败摘要
/// </summary>
public class UserFailedLoginSummaryDto
{
    /// <summary>
    /// 用户ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 用户名
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// 邮箱
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// 失败次数
    /// </summary>
    public int FailureCount { get; set; }

    /// <summary>
    /// 最后一次失败时间
    /// </summary>
    public DateTime? LastFailureTime { get; set; }

    /// <summary>
    /// 失败使用的IP地址列表
    /// </summary>
    public List<string> IpAddresses { get; set; } = new();

    /// <summary>
    /// 用户是否已锁定
    /// </summary>
    public bool IsLockedOut { get; set; }
}
