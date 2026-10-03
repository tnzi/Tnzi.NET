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
    /// <remarks>
    /// ★ 刻意**没有** <c>Organization</c> 导航属性：组织树住在可选包
    /// <c>Tnzi.Identity.Organization</c> 里，核心的 User 不认识那个实体（认识了，
    /// 那个包就永远拆不出去）。外键与删除行为由该包的 <c>OrganizationConfiguration</c>
    /// 从主体侧声明，列形状与拆分前逐字相同；不加载该包时这里只是一个带索引的可空 Guid。
    /// 需要组织名的地方经 <c>IOrganizationService.GetNamesAsync</c> 批量翻译。
    /// </remarks>
    public Guid? OrganizationId { get; set; }

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
    /// 获取或设置 是否启用 passkey / 安全密钥 2FA(需至少登记一枚 passkey)
    /// </summary>
    /// <remarks>
    /// 只是开关，不是凭据：凭据在 <c>UserPasskey</c>（运行时的 <c>IUserPasskeyStore</c>）里。
    /// 最后一枚凭据被删掉时这个开关随之清掉，否则登录会停在一个没人能完成的第二步。
    /// </remarks>
    public bool PasskeyTwoFactorEnabled { get; set; }

    /// <summary>
    /// 获取或设置 首选 2FA 方式(登录时默认展示;null 表示未指定,由系统按优先级选择)
    /// </summary>
    public TwoFactorType? PreferredTwoFactorType { get; set; }

    /// <summary>
    /// 获取或设置 这个账号还欠着的事（未接受邀请 / 必须改密 / …）。
    /// </summary>
    /// <remarks>
    /// 低位是阻断位（<see cref="Services.PendingActionsLoginGuard"/> 读它，命中即拒绝登录），
    /// 高位是义务位（<c>IssueTokenAsync</c> 读它，命中即发挑战）。
    /// <c>default</c> 是 <see cref="PendingUserActions.None"/>，所以存量账号不受影响。
    /// ★ 清位用 <c>&amp;= ~Flag</c> 而不是赋 <c>None</c>，理由见 <see cref="PendingUserActions"/>。
    /// </remarks>
    public PendingUserActions PendingActions { get; set; }

    // 实现接口属性
    public bool IsDeleted { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime? LastModificationTime { get; set; }

    public object[] GetKeys() => new object[] { Id };
}


