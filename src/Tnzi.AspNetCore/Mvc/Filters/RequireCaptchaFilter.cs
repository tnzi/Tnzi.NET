namespace Tnzi.AspNetCore.Mvc.Filters;

/// <summary>
/// <see cref="RequireCaptchaAttribute"/> 的执行体。
/// </summary>
public sealed class RequireCaptchaFilter : IAsyncActionFilter
{
    private readonly string _purpose;
    private readonly ICaptchaVerifier _verifier;
    private readonly ILogger<RequireCaptchaFilter> _logger;

    /// <summary>
    /// 初始化过滤器。
    /// </summary>
    public RequireCaptchaFilter(string purpose, ICaptchaVerifier verifier, ILogger<RequireCaptchaFilter> logger)
    {
        _purpose = Check.NotNullOrWhiteSpace(purpose);
        _verifier = Check.NotNull(verifier);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        Check.NotNull(context);
        Check.NotNull(next);

        if (!_verifier.IsEnabled)
        {
            await next();
            return;
        }

        var token = ExtractToken(context);
        var verification = await _verifier.VerifyAsync(token, _purpose, context.HttpContext.RequestAborted);
        if (verification.Passed)
        {
            await next();
            return;
        }

        _logger.LogInformation(
            "Captcha gate rejected {Method} {Path} (purpose {Purpose}): {Failure}",
            context.HttpContext.Request.Method, context.HttpContext.Request.Path, _purpose, verification.Failure);

        // 用户可见消息不区分原因（那是给机器人的反馈信号）；原因进 errorDetails 供前端决定要不要重置控件。
        context.Result = new BadRequestObjectResult(ApiResult.Error(
            "Captcha verification is required.",
            400,
            CAPTCHA_REQUIRED,
            new { provider = verification.Provider, failure = verification.Failure.ToString() }));
    }

    /// <summary>
    /// 请求头优先，其次是实现了 <see cref="ICaptchaProtectedRequest"/> 的 action 参数。
    /// </summary>
    internal static string? ExtractToken(ActionExecutingContext context)
    {
        if (context.HttpContext.Request.Headers.TryGetValue(RequireCaptchaAttribute.TokenHeaderName, out var header))
        {
            var fromHeader = header.ToString();
            if (!string.IsNullOrWhiteSpace(fromHeader))
                return fromHeader;
        }

        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is ICaptchaProtectedRequest protectedRequest && !string.IsNullOrWhiteSpace(protectedRequest.CaptchaToken))
                return protectedRequest.CaptchaToken;
        }

        return null;
    }
}
