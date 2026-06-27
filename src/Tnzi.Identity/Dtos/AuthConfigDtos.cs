namespace Tnzi.Identity.Dtos;

/// <summary>
/// 公开认证配置 DTO。
/// 由匿名端点 <c>GET /auth/config</c> 返回，供登录页按部署配置决定
/// 登录方式 / 注册 / 找回密码 / 第三方登录的显隐。
/// 只暴露布尔开关与已启用的第三方提供商，绝不包含任何密钥。
/// </summary>
public class AuthConfigDto
{
    // ── 账号标识（密码登录可接受的标识符类型） ──

    /// <summary>是否允许用户名登录</summary>
    public bool AllowUserNameLogin { get; set; }

    /// <summary>是否允许邮箱登录</summary>
    public bool AllowEmailLogin { get; set; }

    /// <summary>是否允许手机号登录</summary>
    public bool AllowSmsLogin { get; set; }

    /// <summary>是否使用邮箱作为用户名</summary>
    public bool UseEmailAsUserName { get; set; }

    // ── 验证码登录 ──

    /// <summary>是否启用验证码登录（邮箱或短信任一启用即为 true）</summary>
    public bool EnableCodeLogin { get; set; }

    /// <summary>验证码登录是否支持短信渠道</summary>
    public bool CodeLoginViaSms { get; set; }

    /// <summary>验证码登录是否支持邮箱渠道</summary>
    public bool CodeLoginViaEmail { get; set; }

    // ── 注册（快速注册：验证码 + 账号） ──

    /// <summary>是否启用注册入口（邮箱或短信快速注册任一启用即为 true）</summary>
    public bool EnableRegistration { get; set; }

    /// <summary>注册是否支持短信渠道</summary>
    public bool RegisterViaSms { get; set; }

    /// <summary>注册是否支持邮箱渠道</summary>
    public bool RegisterViaEmail { get; set; }

    // ── 找回密码 ──

    /// <summary>是否启用找回密码入口（邮箱或短信任一启用即为 true）</summary>
    public bool EnablePasswordRecovery { get; set; }

    /// <summary>找回密码是否支持邮箱渠道</summary>
    public bool RecoveryViaEmail { get; set; }

    /// <summary>找回密码是否支持短信渠道</summary>
    public bool RecoveryViaSms { get; set; }

    // ── 图形验证码 ──

    /// <summary>登录是否启用图形验证码</summary>
    public bool EnableCaptchaOnLogin { get; set; }

    /// <summary>注册是否启用图形验证码</summary>
    public bool EnableCaptchaOnRegister { get; set; }

    // ── 第三方登录（仅列出已启用的提供商） ──

    /// <summary>已启用的第三方登录提供商列表</summary>
    public List<OAuthProviderInfoDto> OAuthProviders { get; set; } = [];
}

/// <summary>
/// 已启用的 OAuth 提供商信息（公开，不含任何密钥）。
/// </summary>
public class OAuthProviderInfoDto
{
    /// <summary>提供商 key（小写，用于拼接 <c>/auth/oauth/{provider}/login</c>）</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>提供商展示名（如 "GitHub"）</summary>
    public string DisplayName { get; set; } = string.Empty;
}
