namespace Tnzi.Identity.Services;

/// <summary>
/// 内置图形验证码作为一家 <see cref="ICaptchaProvider"/>（名字 <c>image</c>）。
/// Identity 模块加载时它是 <c>AspNetCore:Captcha:Provider</c> 的默认值，让没配任何提供商的部署
/// 保持此前的行为（开了登录 / 注册验证码就出文字图）。
/// </summary>
/// <remarks>
/// 令牌形状 <c>{captchaId}:{code}</c>（<see cref="ImageCaptchaToken"/>）。出题走
/// <c>GET auth/captcha/{purpose}/json</c>，校验委托 <see cref="ICaptchaService.VerifyAsync"/>（一次性消费、大小写不敏感）。
/// 4 位字母数字对 OCR 与视觉模型基本没有防护力，只能挡最粗糙的脚本 —— 需要真防护的部署应换 Turnstile / Altcha。
/// </remarks>
public class ImageCaptchaProvider : ICaptchaProvider
{
    /// <summary>提供商名。</summary>
    public const string ProviderName = IdentityConstants.ImageCaptchaProvider;

    /// <summary>出题端点模板（相对 API 根，<c>{purpose}</c> 由客户端替换）。</summary>
    public const string ChallengePathTemplate = "auth/captcha/{purpose}/json";

    private readonly ICaptchaService _captchaService;

    /// <summary>
    /// 初始化提供商。
    /// </summary>
    public ImageCaptchaProvider(ICaptchaService captchaService)
    {
        _captchaService = Check.NotNull(captchaService);
    }

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public async Task<CaptchaVerification> VerifyAsync(CaptchaVerificationRequest request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        if (!ImageCaptchaToken.TryParse(request.Token, out var captchaId, out var code))
            return CaptchaVerification.Fail(Name, CaptchaFailure.Rejected, "Malformed image captcha token");

        var ok = await _captchaService.VerifyAsync(captchaId, code, request.Purpose);
        return ok
            ? CaptchaVerification.Pass(Name).WithReport(null, request.Purpose, null)
            : CaptchaVerification.Fail(Name, CaptchaFailure.Rejected);
    }

    /// <inheritdoc />
    public CaptchaClientConfigDto GetClientConfig() => new()
    {
        Enabled = true,
        Provider = Name,
        ChallengeUrl = ChallengePathTemplate
    };
}

/// <summary>
/// 图形验证码令牌的形状：<c>{captchaId}:{code}</c>。captchaId 是 32 位十六进制（无连字符 GUID），不含冒号，
/// 所以按第一个冒号切分没有歧义。
/// </summary>
public static class ImageCaptchaToken
{
    /// <summary>分隔符。</summary>
    public const char Separator = ':';

    /// <summary>
    /// 由 id 与答案拼出令牌；任一为空返回 null（表示没有提交验证码）。
    /// </summary>
    public static string? Compose(string? captchaId, string? code)
    {
        if (string.IsNullOrWhiteSpace(captchaId) || string.IsNullOrWhiteSpace(code))
            return null;
        return $"{captchaId}{Separator}{code}";
    }

    /// <summary>
    /// 拆出 id 与答案。
    /// </summary>
    public static bool TryParse(string? token, out string captchaId, out string code)
    {
        captchaId = string.Empty;
        code = string.Empty;
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var index = token.IndexOf(Separator);
        if (index <= 0 || index == token.Length - 1)
            return false;

        captchaId = token[..index];
        code = token[(index + 1)..];
        return true;
    }

    /// <summary>
    /// 归一化一次提交：<c>CaptchaToken</c> 优先，其次由历史形式的 id + code 拼出。
    /// </summary>
    public static string? Resolve(ICaptchaSubmission submission)
    {
        Check.NotNull(submission);
        return string.IsNullOrWhiteSpace(submission.CaptchaToken)
            ? Compose(submission.CaptchaId, submission.CaptchaCode)
            : submission.CaptchaToken;
    }
}
