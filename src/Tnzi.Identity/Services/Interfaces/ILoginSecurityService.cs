
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


