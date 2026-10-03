namespace Tnzi.AspNetCore.Mvc.Filters;

/// <summary>
/// <see cref="RequireCaptchaAttribute"/> 的执行体。
/// </summary>
public sealed class RequireCaptchaFilter : IAsyncActionFilter
{
    /// <summary>本请求已经过一次验证码闸门的标记（<see cref="HttpContext.Items"/> 键）。</summary>
    private static readonly object VerifiedItemKey = new();

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

        // 同一个 action 上可能叠着多个闸门（类级 + 方法级、基类 + 派生类）。令牌是一次性的，
        // 验第二次必判重放 —— 所以只让离 action 最近的那个用途验，且一个请求至多验一次。
        if (!_verifier.IsEnabled || !IsNearestGate(context) || context.HttpContext.Items.ContainsKey(VerifiedItemKey))
        {
            await next();
            return;
        }

        var token = ExtractToken(context);
        var verification = await _verifier.VerifyAsync(token, _purpose, context.HttpContext.RequestAborted);
        if (verification.Passed)
        {
            context.HttpContext.Items[VerifiedItemKey] = true;
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
    /// 本过滤器的用途是否就是离 action 最近的那个 <see cref="RequireCaptchaAttribute"/> 的用途。
    /// 作用域更外层、用途又不同的闸门让位，否则令牌会先按外层用途被核销，内层用途拿到的只剩重放。
    /// </summary>
    private bool IsNearestGate(ActionExecutingContext context)
    {
        RequireCaptchaAttribute? nearest = null;
        var nearestScope = int.MinValue;
        var descriptors = context.ActionDescriptor.FilterDescriptors;
        if (descriptors != null)
        {
            foreach (var descriptor in descriptors)
            {
                if (descriptor.Filter is RequireCaptchaAttribute gate && descriptor.Scope >= nearestScope)
                {
                    nearest = gate;
                    nearestScope = descriptor.Scope;
                }
            }
        }

        return nearest == null || string.Equals(nearest.Purpose, _purpose, StringComparison.Ordinal);
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
