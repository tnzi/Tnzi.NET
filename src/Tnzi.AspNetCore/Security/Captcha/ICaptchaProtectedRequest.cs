namespace Tnzi.AspNetCore.Security;

/// <summary>
/// 请求体里带人机验证令牌的 DTO。<see cref="RequireCaptchaAttribute"/> 在请求头 <c>X-Captcha-Token</c>
/// 之外，会从实现了本接口的 action 参数上取令牌。
/// </summary>
public interface ICaptchaProtectedRequest
{
    /// <summary>
    /// 人机验证令牌。各提供商的形状不同（siteverify 的 response、Altcha 的 base64 载荷、
    /// 图形验证码的 <c>id:code</c>、滑块的通行令牌），服务端只当不透明字符串转交给提供商。
    /// </summary>
    string? CaptchaToken { get; }
}
