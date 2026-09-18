namespace Tnzi.AspNetCore.Dtos;

/// <summary>
/// 浏览器渲染人机验证控件所需的全部公开信息。由 <c>GET /captcha/config</c> 下发，
/// Identity 的 <c>GET /auth/config</c> 也原样内嵌一份。
/// </summary>
/// <remarks>
/// 这里只有**公开**值：站点密钥本来就要写进页面 HTML，挑战端点匿名可达。
/// 服务端密钥（<c>SecretKey</c> / <c>HmacKey</c>）永远不在这个对象里。
/// </remarks>
public class CaptchaClientConfigDto
{
    /// <summary>
    /// 是否启用了人机验证。false 时其余字段全为空，客户端不渲染任何控件。
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 启用的提供商名（<c>recaptcha</c> / <c>recaptcha-v3</c> / <c>hcaptcha</c> / <c>turnstile</c> /
    /// <c>altcha</c> / <c>image</c> / <c>sliding</c> 或消费方自注册的名字）。
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>站点密钥（公开值）。托管型提供商需要，自校验型为空。</summary>
    public string? SiteKey { get; set; }

    /// <summary>
    /// 客户端脚本地址。托管型提供商与 Altcha 控件由此加载；
    /// 部署可以覆盖成自托管的副本（例如内网镜像或 recaptcha.net）。
    /// </summary>
    public string? ScriptUrl { get; set; }

    /// <summary>
    /// 挑战端点（相对 API 根，已含 <c>api/</c> 之后的部分，例如 <c>captcha/altcha/challenge</c>）。
    /// 只有服务端出题的提供商（Altcha / 图形 / 滑块）有值。
    /// </summary>
    public string? ChallengeUrl { get; set; }
}
