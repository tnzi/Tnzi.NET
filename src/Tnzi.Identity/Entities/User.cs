namespace Tnzi.Identity.Entities;

/// <summary>
/// Tnzi 用户实体 (扩展自 ASP.NET Core Identity)
/// 只保留核心身份认证字段，个人资料信息存储在 UserDetail 表
/// </summary>
[Table("User")]
public class User : IdentityUser<Guid>, IEntity<Guid>, ISoftDelete, IHasCreationTime, IHasModificationTime
{
    /// <summary>
    /// 获取或设置 所属租户ID（null 表示全局用户/超级管理员）
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 获取或设置 组织ID
    /// </summary>
    public Guid? OrganizationId { get; set; }
    
    /// <summary>
    /// 获取或设置 组织
    /// </summary>
    public virtual Organization? Organization { get; set; }
    
    // ── 双因素认证：按方式独立启用状态 ──
    // ASP.NET Identity 的 TwoFactorEnabled 只表达"是否需要 2FA",不区分方式。
    // 这三个 flag 表达"具体哪种方式被用户启用",允许各自独立开关(保留 TOTP、
    // 单独关短信等)。TwoFactorEnabled 作为聚合值维护:任一 flag 为 true 即 true。
    // 详见 ITwoFactorService。

    /// <summary>
    /// 获取或设置 是否启用短信验证码 2FA(需手机号已验证)
    /// </summary>
    public bool SmsTwoFactorEnabled { get; set; }

    /// <summary>
    /// 获取或设置 是否启用邮箱验证码 2FA(需邮箱已验证)
    /// </summary>
    public bool EmailTwoFactorEnabled { get; set; }

    /// <summary>
    /// 获取或设置 是否启用身份验证器(TOTP)2FA(需已配置 authenticator key)
    /// </summary>
    public bool AuthenticatorTwoFactorEnabled { get; set; }

    /// <summary>
    /// 获取或设置 首选 2FA 方式(登录时默认展示;null 表示未指定,由系统按优先级选择)
    /// </summary>
    public TwoFactorType? PreferredTwoFactorType { get; set; }

    // 实现接口属性
    public bool IsDeleted { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime? LastModificationTime { get; set; }

    public object[] GetKeys() => new object[] { Id };
}


