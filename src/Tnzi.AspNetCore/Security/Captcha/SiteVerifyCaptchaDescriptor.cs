namespace Tnzi.AspNetCore.Security;

/// <summary>
/// 一家「siteverify 协议」提供商的参数。适配器是代码（<see cref="SiteVerifyCaptchaProvider"/>），
/// 描述符是数据：reCAPTCHA v2 / v3、hCaptcha、Turnstile 的服务端校验都是
/// <c>POST verifyUrl</c>（表单编码 <c>secret</c> + <c>response</c> + <c>remoteip</c>）→
/// <c>{ success, hostname, "error-codes", score?, action? }</c>，差别只在地址与回传了哪些字段。
/// </summary>
public sealed class SiteVerifyCaptchaDescriptor
{
    /// <summary>
    /// 初始化描述符。
    /// </summary>
    public SiteVerifyCaptchaDescriptor(string name, string verifyUrl, string scriptUrl)
    {
        Name = Check.NotNullOrWhiteSpace(name);
        VerifyUrl = Check.NotNullOrWhiteSpace(verifyUrl);
        ScriptUrl = Check.NotNullOrWhiteSpace(scriptUrl);
    }

    /// <summary>提供商名（配置值）。</summary>
    public string Name { get; }

    /// <summary>默认验证端点。</summary>
    public string VerifyUrl { get; }

    /// <summary>
    /// 默认客户端脚本地址。可含 <c>{siteKey}</c> 占位（reCAPTCHA v3 的脚本地址带站点密钥），
    /// 下发时替换。
    /// </summary>
    public string ScriptUrl { get; }

    /// <summary>是否把 <c>sitekey</c> 一并交给验证端点（hCaptcha 支持，用于核对令牌是给本站解的）。</summary>
    public bool SendsSiteKey { get; init; }

    /// <summary>是否回传 <c>action</c>（reCAPTCHA v3、Turnstile）。</summary>
    public bool ReportsAction { get; init; }

    /// <summary>是否回传 <c>score</c> 且语义是「越高越像人」（reCAPTCHA v3）。</summary>
    public bool ReportsScore { get; init; }

    /// <summary>Google reCAPTCHA v2（勾选框或隐形），无评分。</summary>
    public static SiteVerifyCaptchaDescriptor ReCaptcha { get; } = new(
        "recaptcha",
        "https://www.google.com/recaptcha/api/siteverify",
        "https://www.google.com/recaptcha/api.js?render=explicit");

    /// <summary>Google reCAPTCHA v3（评分 + action）。</summary>
    public static SiteVerifyCaptchaDescriptor ReCaptchaV3 { get; } = new(
        "recaptcha-v3",
        "https://www.google.com/recaptcha/api/siteverify",
        "https://www.google.com/recaptcha/api.js?render={siteKey}")
    {
        ReportsAction = true,
        ReportsScore = true
    };

    /// <summary>
    /// hCaptcha。<b>刻意不读它的 score</b>：hCaptcha 企业版回传的是<b>风险</b>分（越高越像机器），
    /// 与 reCAPTCHA v3 的「越高越像人」正好相反，套同一条 <c>score &lt; ScoreThreshold</c> 会把真人拒掉。
    /// </summary>
    public static SiteVerifyCaptchaDescriptor HCaptcha { get; } = new(
        "hcaptcha",
        "https://api.hcaptcha.com/siteverify",
        "https://js.hcaptcha.com/1/api.js?render=explicit")
    {
        SendsSiteKey = true
    };

    /// <summary>Cloudflare Turnstile。</summary>
    public static SiteVerifyCaptchaDescriptor Turnstile { get; } = new(
        "turnstile",
        "https://challenges.cloudflare.com/turnstile/v0/siteverify",
        "https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit")
    {
        ReportsAction = true
    };

    /// <summary>框架内置的四条描述符。</summary>
    public static IReadOnlyList<SiteVerifyCaptchaDescriptor> BuiltIn { get; } =
        [ReCaptcha, ReCaptchaV3, HCaptcha, Turnstile];
}
