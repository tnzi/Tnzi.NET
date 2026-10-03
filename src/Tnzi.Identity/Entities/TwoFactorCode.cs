namespace Tnzi.Identity.Entities;

/// <summary>
/// 一次性验证码实体。承载登录 2FA、免密验证码登录、找回密码、快速注册、换绑联系方式、
/// 敏感操作二次确认等多个流程的码 —— 由 <see cref="Purpose"/> 区分，**不同用途之间不可互换**。
/// </summary>
/// <remarks>
/// ★★★ <strong>类级 <see cref="AuditIgnoreAttribute"/>：整个实体不进实体级审计。</strong>
/// 属性级豁免在这里不够 —— <see cref="Code"/> 是一枚活的凭据，而
/// <see cref="Address"/> + <see cref="Purpose"/> 合起来是「谁正在收哪种码」的情报。
/// <para>
/// 不能指望 <c>AuditOptions.SensitiveFields</c> 兜住：那是按属性名<b>精确匹配</b>的跨实体名单，
/// 收录 <c>"Code"</c> 会掩掉全仓所有叫 Code 的业务字段（科目代码、货币代码、模块编码……），
/// 所以通用名进不了名单 —— 拦截器自己的注释写着这一条。少了本特性的后果是：
/// 每次发码都把明文验证码连同收件地址写进 <c>Audit_PropertyEntry</c>，
/// 于是一个只读的 <c>audit.operation.view</c> 就足以给任意账号发一枚找回密码的码、
/// 再从审计明细里读出来 —— 只读权限直接升级为账号接管。
/// </para>
/// <para>
/// 本实体的生命周期本来就有专门的 <c>LogInformation</c>（发出 / 验证 / 标记已用），
/// 退出实体级审计不损失任何可追溯性。守卫：<c>CredentialAuditIgnoreTests</c>。
/// </para>
/// </remarks>
[AuditIgnore]
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
    /// 获取或设置 用途。★ 验码时**精确匹配**：为某个流程发出的码不能用于另一个流程。
    /// 详见 <see cref="VerificationCodePurpose"/>（含 <c>Unknown</c> 为何永不匹配）。
    /// </summary>
    public VerificationCodePurpose Purpose { get; set; }

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
    Totp = 3,

    /// <summary>
    /// Passkey / 安全密钥（WebAuthn 断言：YubiKey 这类 FIDO2 硬件密钥，或平台认证器）。
    /// </summary>
    /// <remarks>
    /// 不是验证码：没有发码、没有输码，第二步是一次 <c>navigator.credentials.get()</c> 断言，
    /// 由 <c>verify-2fa/passkey/begin</c> + <c>verify-2fa/passkey/complete</c> 完成。
    /// 凭据本身在「通行密钥」里登记；这里只是「登录时是否拿它当第二因子」的开关。
    /// </remarks>
    Passkey = 4
}
