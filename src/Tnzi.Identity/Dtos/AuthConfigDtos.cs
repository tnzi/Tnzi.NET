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

    /// <summary>
    /// 是否允许自助注册（用户名 + 密码 + 邮箱）。对应 <c>Identity:Registration:EnableSelfRegistration</c>。
    /// </summary>
    /// <remarks>
    /// ★ 与 <c>EnableRegistration</c> 的关系：后者是三条注册路径的并集（用来决定「显不显示注册入口」），
    /// 这一项决定其中的密码注册那一条显不显示。此前没有它，于是配置说关着而端点开着。
    /// </remarks>
    public bool RegisterViaPassword { get; set; }

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

    // ── 人机验证 ──

    /// <summary>登录是否启用人机验证（密码登录自适应；验证码登录发码前无条件）</summary>
    public bool EnableCaptchaOnLogin { get; set; }

    /// <summary>注册是否启用人机验证（发码 / 注册 / 重发确认邮件之前无条件）</summary>
    public bool EnableCaptchaOnRegister { get; set; }

    /// <summary>找回密码是否启用人机验证（<c>forgot-password</c> 与 <c>password-recovery/send-code</c> 发信之前无条件）</summary>
    public bool EnableCaptchaOnPasswordRecovery { get; set; }

    /// <summary>
    /// 浏览器渲染人机验证控件所需的公开配置（提供商名、站点密钥、脚本地址、出题端点），
    /// 与 <c>GET /captcha/config</c> 同一份。加载了本模块的部署里 <c>Enabled</c> 恒为 true：
    /// 没配 <c>AspNetCore:Captcha:Provider</c> 时本模块把默认补成内置图形验证码 <c>image</c>。
    /// </summary>
    public CaptchaClientConfigDto Captcha { get; set; } = new();

    // ── Passkey（WebAuthn） ──

    /// <summary>
    /// 是否启用 passkey（<c>Identity:Passkey:Enabled</c>，默认关闭）。
    /// </summary>
    /// <remarks>
    /// 客户端据此决定要不要显示 passkey 的注册与登录入口。
    /// ★ 它只说「这个部署开着 passkey」，不说「这个浏览器支持」——
    /// 后者要客户端自己检测（<c>@tnzi/core</c> 的 <c>isPasskeySupported()</c>），
    /// 两个条件都成立才该把入口显出来。
    /// </remarks>
    public bool EnablePasskey { get; set; }

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
