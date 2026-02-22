namespace Tnzi.Identity.Entities;

/// <summary>
/// 双因素认证验证码实体
/// 用于 2FA 验证（已登录用户）和验证码登录（用户可能不存在）两种场景
/// </summary>
public class TwoFactorCode : EntityBase<Guid>, IHasCreationTime
{
    /// <summary>
    /// 获取或设置 用户ID（可空，验证码登录时用户可能不存在）
    /// 当 UserId 为空时，表示验证码登录场景（用户可能尚未注册）
    /// 当 UserId 有值时，表示已登录用户的 2FA 验证场景
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// 获取或设置 用户（可空，与 UserId 对应）
    /// </summary>
    public virtual User? User { get; set; }

    /// <summary>
    /// 获取或设置 验证码
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 验证方式（SMS/Email）
    /// </summary>
    public TwoFactorType Type { get; set; }

    /// <summary>
    /// 获取或设置 接收地址（手机号或邮箱）
    /// </summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 过期时间
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// 获取或设置 是否已使用
    /// </summary>
    public bool IsUsed { get; set; }

    /// <summary>
    /// 获取或设置 使用时间
    /// </summary>
    public DateTime? UsedAt { get; set; }

    /// <summary>
    /// 获取或设置 创建时间
    /// </summary>
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 双因素认证类型
/// </summary>
public enum TwoFactorType
{
    /// <summary>
    /// SMS短信验证
    /// </summary>
    Sms = 1,

    /// <summary>
    /// Email邮件验证
    /// </summary>
    Email = 2,

    /// <summary>
    /// TOTP 时间验证码（Authenticator App）
    /// </summary>
    Totp = 3
}
