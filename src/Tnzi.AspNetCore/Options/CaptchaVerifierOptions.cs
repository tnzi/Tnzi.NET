namespace Tnzi.AspNetCore.Options;

/// <summary>
/// 人机验证配置（<c>AspNetCore:Captcha</c> 节）。
/// </summary>
/// <remarks>
/// <para>
/// 只有 <see cref="Provider"/> 决定「启不启用、用哪家」；其余字段是那一家需要的材料。
/// 不配 <c>Provider</c> = 不启用：所有 <c>[RequireCaptcha]</c> 放行，Identity 的登录 / 注册开关也只在
/// 自己模块内退回内置图形验证码（Identity 模块加载时把默认值补成 <c>image</c>）。
/// </para>
/// <para>
/// 刻意不进配置中心（没有 <c>[RuntimeSettingGroup]</c>）：<see cref="SecretKey"/> 与
/// <see cref="AltchaOptions.HmacKey"/> 是服务端密钥，不该出现在管理界面的表单里；换提供商也不是
/// 运行期热切的事（前端脚本随之变化）。
/// </para>
/// </remarks>
[ConfigSection("AspNetCore:Captcha")]
public class CaptchaVerifierOptions
{
    /// <summary>
    /// 生效的提供商名。内置：<c>recaptcha</c>（v2 勾选框 / 隐形）、<c>recaptcha-v3</c>（评分）、
    /// <c>hcaptcha</c>、<c>turnstile</c>、<c>altcha</c>；加载 Identity 后有 <c>image</c>，加载 Imaging 后有
    /// <c>sliding</c>。null 表示不启用。
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>站点密钥（公开值，下发给浏览器）。托管型提供商必填。</summary>
    public string? SiteKey { get; set; }

    /// <summary>服务端密钥。托管型提供商必填，永不下发。</summary>
    public string? SecretKey { get; set; }

    /// <summary>
    /// 覆盖验证端点地址。默认取提供商描述符里的官方地址；中国大陆部署 reCAPTCHA 可改成
    /// <c>https://recaptcha.net/recaptcha/api/siteverify</c>。
    /// </summary>
    public string? VerifyUrl { get; set; }

    /// <summary>
    /// 覆盖客户端脚本地址（下发给浏览器）。默认取提供商描述符里的官方 CDN；
    /// Altcha 默认走 jsDelivr，自托管时改成自己的路径。
    /// </summary>
    public string? ScriptUrl { get; set; }

    /// <summary>
    /// 评分阈值（0 到 1），只对 reCAPTCHA v3 生效（hCaptcha 企业版的 score 是风险分、语义相反，框架不解释它）。
    /// 低于阈值按 <see cref="CaptchaFailure.LowScore"/> 拒绝。默认 0.5。
    /// </summary>
    public double ScoreThreshold { get; set; } = 0.5;

    /// <summary>
    /// 提供商回传了 action 时是否必须与端点声明的用途一致。默认 true。
    /// 关掉它等于允许在注册页解出的令牌拿去登录。
    /// </summary>
    public bool EnforceAction { get; set; } = true;

    /// <summary>
    /// 允许的站点主机名。非空时，提供商回传的 hostname 不在列表内按
    /// <see cref="CaptchaFailure.HostnameMismatch"/> 拒绝。空表示不核对。
    /// </summary>
    public string[]? ExpectedHostnames { get; set; }

    /// <summary>验证服务不可达时的处置。默认拒绝。</summary>
    public CaptchaUnavailablePolicy OnVerifierUnavailable { get; set; } = CaptchaUnavailablePolicy.Deny;

    /// <summary>调用验证服务的超时秒数。默认 5。</summary>
    public int TimeoutSeconds { get; set; } = 5;

    /// <summary>Altcha 专用参数。</summary>
    public AltchaOptions Altcha { get; set; } = new();
}

/// <summary>
/// Altcha（自托管工作量证明验证码）参数。
/// </summary>
public class AltchaOptions
{
    /// <summary>
    /// 签名挑战的 HMAC 密钥，至少 32 个字符。**必须配置**，框架不生成临时密钥：
    /// 进程内随机密钥在多实例部署下会让 A 实例出的题在 B 实例验签失败，症状是「验证码有时能过有时不能」。
    /// </summary>
    public string? HmacKey { get; set; }

    /// <summary>
    /// 随机数上界，决定客户端要算多少次哈希。默认 100000（现代浏览器约一秒内）。
    /// </summary>
    public int MaxNumber { get; set; } = 100_000;

    /// <summary>挑战有效期秒数，过期后即便解对也拒绝。默认 300。</summary>
    public int ExpiresSeconds { get; set; } = 300;
}
