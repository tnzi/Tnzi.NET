namespace Tnzi.AspNetCore.Security;

/// <summary>
/// 一家人机验证提供商的服务端实现：拿到客户端交上来的令牌，回答它是不是人解出来的。
/// </summary>
/// <remarks>
/// <para>
/// 框架内置 reCAPTCHA v2 / v3、hCaptcha、Cloudflare Turnstile（四条描述符共用一个
/// <see cref="SiteVerifyCaptchaProvider"/>，因为四家的 siteverify 协议逐字同形）与自托管的
/// <see cref="AltchaCaptchaProvider"/>；Identity 模块注册内置图形验证码 <c>image</c>，Imaging 模块
/// 注册滑块 <c>sliding</c>。消费方接第三方（极验、腾讯云验证码……）只需实现本接口并经
/// <c>services.AddCaptchaProvider&lt;T&gt;()</c> 注册，再把 <c>AspNetCore:Captcha:Provider</c> 配成它的名字。
/// </para>
/// <para>
/// 实现只负责「这枚令牌对不对」。哪一家生效、验证服务不可达怎么办、用途绑定与主机名核对这些
/// 跨提供商的策略，由 <see cref="ICaptchaVerifier"/> 统一执行；不要在实现里再读一遍配置做同样的事。
/// </para>
/// </remarks>
public interface ICaptchaProvider
{
    /// <summary>
    /// 提供商名，与 <c>AspNetCore:Captcha:Provider</c> 配置值比对（不区分大小写）。
    /// 惯例全小写、连字符分词。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 校验一枚令牌。实现应把网络失败、超时、非 2xx 报成 <see cref="CaptchaFailure.VerifierUnavailable"/>
    /// 而不是抛异常 —— 「验证服务挂了」和「答错了」是两种处置。
    /// </summary>
    Task<CaptchaVerification> VerifyAsync(CaptchaVerificationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 浏览器渲染本提供商控件所需的公开信息。没有可下发的（纯服务端出题且客户端已知端点）返回 null，
    /// 验证器会只填 <c>Enabled</c> 与 <c>Provider</c>。
    /// </summary>
    CaptchaClientConfigDto? GetClientConfig() => null;
}
