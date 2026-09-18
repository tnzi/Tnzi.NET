namespace Tnzi.Imaging.Services;

/// <summary>
/// 滑块验证码作为一家 <see cref="ICaptchaProvider"/>（名字 <c>sliding</c>）：核销 <see cref="SlidingCaptchaVerifyResult.PassToken"/>。
/// </summary>
/// <remarks>
/// 在此之前滑块是一个孤立控件：<c>/captcha/sliding/verify</c> 只回 <c>{ Success }</c>，受保护端点无从分辨
/// 这次提交前有没有真的滑过。现在验证通过签一枚一次性通行令牌，客户端把它作为 <c>captchaToken</c> 交给
/// 受保护端点，由本提供商核销。出题端点带 <c>?purpose=</c> 时通行令牌绑用途。
/// 框架不带滑块的前端组件（模板匹配找缺口对机器不是难题，它的价值是交互体验不是防护力），消费方按需自建。
/// </remarks>
public class SlidingCaptchaProvider : ICaptchaProvider
{
    /// <summary>提供商名。</summary>
    public const string ProviderName = "sliding";

    /// <summary>出题端点模板（相对 API 根，<c>{purpose}</c> 由客户端替换）。</summary>
    public const string ChallengePathTemplate = "captcha/sliding/generate?purpose={purpose}";

    private readonly ISlidingCaptchaService _slidingCaptchaService;

    /// <summary>
    /// 初始化提供商。
    /// </summary>
    public SlidingCaptchaProvider(ISlidingCaptchaService slidingCaptchaService)
    {
        _slidingCaptchaService = Check.NotNull(slidingCaptchaService);
    }

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public async Task<CaptchaVerification> VerifyAsync(CaptchaVerificationRequest request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);
        var ok = await _slidingCaptchaService.RedeemPassTokenAsync(request.Token, request.Purpose, cancellationToken);
        return ok
            ? CaptchaVerification.Pass(Name).WithReport(null, request.Purpose, null)
            : CaptchaVerification.Fail(Name, CaptchaFailure.ExpiredOrReplayed, "Pass token missing, used, expired or bound to another purpose");
    }

    /// <inheritdoc />
    public CaptchaClientConfigDto GetClientConfig() => new()
    {
        Enabled = true,
        Provider = Name,
        ChallengeUrl = ChallengePathTemplate
    };
}
