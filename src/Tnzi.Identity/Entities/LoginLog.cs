namespace Tnzi.Identity.Entities;

/// <summary>
/// 登录日志实体
/// </summary>
public class LoginLog : EntityBase<Guid>, IHasCreationTime
{
    /// <summary>
    /// 获取或设置 用户ID
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// 获取或设置 用户名
    /// </summary>
    public string? UserName { get; set; }

    /// <summary>
    /// 获取或设置 登录IP地址
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// 获取或设置 用户代理（User-Agent）
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// 获取或设置 登录状态（成功/失败）
    /// </summary>
    public LoginStatus Status { get; set; }

    /// <summary>
    /// 获取或设置 失败原因
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// 获取或设置 登录时间
    /// </summary>
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 登录状态
/// </summary>
public enum LoginStatus
{
    /// <summary>
    /// 成功
    /// </summary>
    Success = 1,

    /// <summary>
    /// 失败
    /// </summary>
    Failed = 2
}

