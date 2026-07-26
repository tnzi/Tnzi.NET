namespace Tnzi.Identity.Services;

/// <summary>
/// 双因素认证服务接口
/// </summary>
public interface ITwoFactorService
{
    /// <summary>
    /// 发送SMS验证码
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="phoneNumber">手机号</param>
    /// <returns>是否发送成功</returns>
    Task<Result> SendSmsCodeAsync(Guid userId, string phoneNumber);

    /// <summary>
    /// 发送Email验证码
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="email">邮箱地址</param>
    /// <returns>是否发送成功</returns>
    Task<Result> SendEmailCodeAsync(Guid userId, string email);

    /// <summary>
    /// 验证验证码
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="code">验证码</param>
    /// <param name="type">验证方式</param>
    /// <returns>是否验证成功</returns>
    Task<Result> VerifyCodeAsync(Guid userId, string code, TwoFactorType type);

    /// <summary>
    /// 禁用全部 2FA(关闭所有方式 + 重置 authenticator key)。这是**销毁性**操作,
    /// 再次启用需从头设置。仅用于"彻底移除"场景;想临时关闭 2FA 而保留配置请用
    /// <see cref="SuspendTwoFactorAsync"/>。
    /// </summary>
    /// <param name="userId">用户ID</param>
    Task<Result> DisableTwoFactorAsync(Guid userId);

    /// <summary>
    /// 暂停 2FA(总开关关闭):登录不再要求二次验证,但**保留**每种方式的启用 flag、
    /// TOTP authenticator key 与首选方式。再次 <see cref="ResumeTwoFactorAsync"/> 即
    /// 恢复原配置,无需重新设置。<see cref="Entities.User.TwoFactorEnabled"/> 置 false,
    /// per-method flag 不动。
    /// </summary>
    /// <param name="userId">用户ID</param>
    Task<Result> SuspendTwoFactorAsync(Guid userId);

    /// <summary>
    /// 恢复被 <see cref="SuspendTwoFactorAsync"/> 暂停的 2FA(总开关重新开启)。要求
    /// 至少已配置一种方式;把 <see cref="Entities.User.TwoFactorEnabled"/> 置回 true,
    /// 原有 flag/key/首选立即重新生效。
    /// </summary>
    /// <param name="userId">用户ID</param>
    Task<Result> ResumeTwoFactorAsync(Guid userId);

    /// <summary>
    /// 禁用某一种 2FA 方式(其它方式保持不变)。禁用 TOTP 会重置 authenticator key。
    /// 若禁用后无任何方式启用,则聚合的 TwoFactorEnabled 自动置 false。
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="type">要禁用的方式</param>
    Task<Result> DisableTwoFactorMethodAsync(Guid userId, TwoFactorType type);

    /// <summary>
    /// 设置首选 2FA 方式(必须是当前已启用的方式)。登录时优先展示该方式。
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="type">首选方式</param>
    Task<Result> SetPreferredTwoFactorAsync(Guid userId, TwoFactorType type);

    /// <summary>
    /// 计算某用户当前"有效可用"的 2FA 方式集合(尊重按方式 flag,且**按部署渠道开关过滤**:
    /// 关闭 EnableSms/EnableEmail/EnableTotp 或地址失去验证的方式会被剔除;对尚未迁移的旧
    /// 用户回退为"可用方式")。供登录挑战使用 —— 返回空集合表示当前无任何可用方式,调用方应
    /// 按"未开启 2FA"直接放行。
    /// </summary>
    /// <param name="user">用户实体</param>
    Task<List<TwoFactorType>> GetEnabledTwoFactorTypesAsync(User user);

    /// <summary>
    /// 获取2FA状态
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>2FA状态信息</returns>
    Task<Result<TwoFactorStatusDto>> GetTwoFactorStatusAsync(Guid userId);

    /// <summary>
    /// 启用双因素认证
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="input">2FA配置</param>
    /// <returns>操作结果</returns>
    Task<Result<string>> EnableTwoFactorAsync(Guid userId, EnableTwoFactorDto input);

    /// <summary>
    /// 获取 TOTP 设置信息（生成密钥和二维码 URI）
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>TOTP 设置信息（包含密钥和 otpauth URI）</returns>
    Task<Result<TotpSetupDto>> GetTotpSetupInfoAsync(Guid userId);

    /// <summary>
    /// 启用 TOTP（验证用户输入的 code 后启用）
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="verificationCode">用户输入的验证码</param>
    /// <returns>操作结果</returns>
    Task<Result> EnableTotpAsync(Guid userId, string verificationCode);

    /// <summary>
    /// 禁用 TOTP
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>操作结果</returns>
    Task<Result> DisableTotpAsync(Guid userId);

    #region 验证码登录支持（基于地址，无需 UserId）

    /// <summary>
    /// 基于地址发送验证码（用于验证码登录，用户可能不存在）
    /// </summary>
    /// <param name="address">接收地址（邮箱或手机号）</param>
    /// <param name="type">验证方式（Email/Sms）</param>
    /// <param name="userId">可选的用户ID（如果用户已存在）</param>
    /// <returns>是否发送成功</returns>
    Task<Result> SendCodeByAddressAsync(string address, TwoFactorType type, Guid? userId = null);

    /// <summary>
    /// 基于地址验证验证码（不标记为已使用，由调用方决定）
    /// </summary>
    /// <param name="address">接收地址（邮箱或手机号）</param>
    /// <param name="code">验证码</param>
    /// <param name="type">验证方式</param>
    /// <returns>是否验证成功</returns>
    Task<Result> VerifyCodeByAddressAsync(string address, string code, TwoFactorType type);

    /// <summary>
    /// 基于地址验证验证码并标记为已使用
    /// </summary>
    /// <param name="address">接收地址（邮箱或手机号）</param>
    /// <param name="code">验证码</param>
    /// <param name="type">验证方式</param>
    /// <returns>验证成功时返回关联的 UserId（可能为空）</returns>
    Task<Result<Guid?>> VerifyCodeByAddressAndMarkUsedAsync(string address, string code, TwoFactorType type);

    #endregion
}

/// <summary>
/// 2FA状态信息
/// </summary>
public class TwoFactorStatusDto
{
    /// <summary>
    /// 是否启用2FA（聚合值：任一方式启用即为 true）
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// 可配置的验证方式（后端部署已开启且用户地址已验证的方式；TOTP 始终可配置）
    /// </summary>
    public List<TwoFactorType> SupportedTypes { get; set; } = new();

    /// <summary>
    /// 当前首选方式（= <see cref="PreferredType"/> 的兼容别名）
    /// </summary>
    public TwoFactorType? CurrentType { get; set; }

    /// <summary>
    /// 是否已启用 TOTP（Authenticator App）
    /// </summary>
    public bool IsTotpEnabled { get; set; }

    /// <summary>
    /// 首选 2FA 方式（登录时默认展示）
    /// </summary>
    public TwoFactorType? PreferredType { get; set; }

    /// <summary>
    /// 每种方式的独立状态（可配置 / 已启用 / 是否首选）
    /// </summary>
    public List<TwoFactorMethodDto> Methods { get; set; } = new();
}

/// <summary>
/// 单个 2FA 方式的状态
/// </summary>
public class TwoFactorMethodDto
{
    /// <summary>
    /// 方式类型
    /// </summary>
    public TwoFactorType Type { get; set; }

    /// <summary>
    /// 是否可配置（部署已开启该渠道且用户地址已验证；TOTP 始终可配置）
    /// </summary>
    public bool Available { get; set; }

    /// <summary>
    /// 是否已启用
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 是否为首选方式
    /// </summary>
    public bool IsPreferred { get; set; }

    /// <summary>
    /// 该渠道在部署层已开启，但用户尚未设置/验证对应地址（手机/邮箱），因此还不能启用。
    /// 前端据此显示该方式行（禁用态）+ "请先验证手机号/邮箱"提示，避免"开了全局配置却
    /// 在列表里看不到该方式"的困惑。TOTP 无地址要求，恒为 false。
    /// </summary>
    public bool RequiresAddress { get; set; }
}
