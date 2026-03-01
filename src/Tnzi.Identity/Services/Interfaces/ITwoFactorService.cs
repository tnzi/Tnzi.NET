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
    /// 禁用2FA
    /// </summary>
    /// <param name="userId">用户ID</param>
    Task<Result> DisableTwoFactorAsync(Guid userId);

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
    /// 是否启用2FA
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// 支持的验证方式
    /// </summary>
    public List<TwoFactorType> SupportedTypes { get; set; } = new();

    /// <summary>
    /// 当前使用的验证方式
    /// </summary>
    public TwoFactorType? CurrentType { get; set; }

    /// <summary>
    /// 是否已启用 TOTP（Authenticator App）
    /// </summary>
    public bool IsTotpEnabled { get; set; }
}
