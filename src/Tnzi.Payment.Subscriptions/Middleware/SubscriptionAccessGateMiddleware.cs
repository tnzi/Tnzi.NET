namespace Tnzi.Payment.Subscriptions.Middleware;

/// <summary>
/// 服务暂停时的 HTTP 门禁：账户已暂停的用户发来的已认证请求答 402 <c>SUBSCRIPTION_SUSPENDED</c>。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>位置：授权之后、端点执行之前</b>（本模块在 <c>OnApplicationInitializationAsync</c> 里追加，AspNetCore 模块的固定管线
/// 认证 → 限流 → 租户解析 → 授权 已经挂完）。三个前提都要满足，所以既不能挂在 <c>AfterAuthentication</c> 插入点，也不能更早：
/// 要知道是谁（认证）；Tenant 范围要知道是哪个租户（租户解析在插入点之后才跑）；
/// 未认证、无权限的请求应当照常得到 401 / 403，而不是一个泄露「这家欠费了」的 402。
/// 端点在管线最前面就由 <c>UseRouting</c> 选好了，所以这里读得到端点元数据（<c>[AllowAnonymous]</c>、<c>[AllowWhenSuspended]</c>）。
/// </para>
/// <para>
/// SignalR：协商与建连请求经过这里（同样会被拒）；已经连上的连接上的方法调用不经过任何 HTTP 中间件，
/// 由 <see cref="SubscriptionAccessGateHubFilter"/> 把关。后台任务、事件处理器、渠道回调不是用户请求，门禁不管。
/// </para>
/// <para>
/// 约定式中间件是进程级单例：scoped 依赖（门禁服务）从 <c>InvokeAsync</c> 参数按请求解析。
/// </para>
/// </remarks>
public class SubscriptionAccessGateMiddleware
{
    /// <summary>402 响应里给人看的说明。</summary>
    public const string SuspendedMessage = "Service is suspended because the subscription payment is overdue.";

    private readonly RequestDelegate _next;
    private readonly IOptionsMonitor<SubscriptionAccessGateOptions> _options;
    private readonly ILogger<SubscriptionAccessGateMiddleware> _logger;
    private readonly string _apiPrefix;

    /// <summary>DI 构造函数。</summary>
    public SubscriptionAccessGateMiddleware(
        RequestDelegate next,
        IOptionsMonitor<SubscriptionAccessGateOptions> options,
        IOptions<AspNetCoreOptions> aspNetCoreOptions,
        ILogger<SubscriptionAccessGateMiddleware> logger)
    {
        _next = Check.NotNull(next);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
        _apiPrefix = "/" + (Check.NotNull(aspNetCoreOptions).Value.ApiPathPrefix ?? string.Empty).Trim('/');
    }

    /// <summary>处理请求。</summary>
    public async Task InvokeAsync(HttpContext context, ISubscriptionAccessGate gate)
    {
        var options = _options.CurrentValue;
        if (!options.Enabled || !ShouldGate(context, options))
        {
            await _next(context);
            return;
        }

        var userId = SubscriptionAccessGateUser.IdOf(context.User);
        if (!userId.HasValue)
        {
            await _next(context);
            return;
        }

        var decision = await gate.EvaluateAsync(userId.Value, context.RequestAborted);
        if (decision.Allowed)
        {
            await _next(context);
            return;
        }

        _logger.LogInformation("Request {Method} {Path} refused: the billing account of user {UserId} is suspended.",
            context.Request.Method, context.GetRedactedPath(), userId.Value);

        context.Response.StatusCode = StatusCodes.Status402PaymentRequired;
        context.Response.ContentType = "application/json";
        var envelope = ApiResult<object>.Error(SuspendedMessage, StatusCodes.Status402PaymentRequired, ErrorCodes.SubscriptionSuspended,
            new SubscriptionSuspendedDetails { Standing = decision.Standing, CanManage = decision.CanManage });
        await context.Response.WriteAsync(JsonSerializer.Serialize(envelope, TnziJsonDefaults.Options), context.RequestAborted);
    }

    /// <summary>这次请求归不归门禁管：没有端点、匿名端点、未认证、放行路径、标了放行特性的一律不管。</summary>
    private bool ShouldGate(HttpContext context, SubscriptionAccessGateOptions options)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint == null)
            return false;

        if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() != null
            || endpoint.Metadata.GetMetadata<AllowWhenSuspendedAttribute>() != null)
            return false;

        if (context.User.Identity?.IsAuthenticated != true)
            return false;

        return !IsAllowedPath(context.Request.Path, options);
    }

    private bool IsAllowedPath(PathString path, SubscriptionAccessGateOptions options)
    {
        foreach (var entry in SubscriptionAccessGateOptions.BuiltInAllowedPaths)
        {
            if (Matches(path, entry))
                return true;
        }

        foreach (var entry in options.AllowedPaths)
        {
            if (Matches(path, entry))
                return true;
        }

        return false;
    }

    /// <summary>按路径段匹配：<c>firm</c> 放行 <c>/api/firm</c> 与 <c>/api/firm/x</c>，不放行 <c>/api/firms</c>。</summary>
    private bool Matches(PathString path, string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
            return false;

        var trimmed = entry.Trim();
        var prefix = trimmed.StartsWith('/')
            ? trimmed.TrimEnd('/')
            : (_apiPrefix == "/" ? string.Empty : _apiPrefix) + "/" + trimmed.Trim('/');

        return path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>402 信封里的 <c>errorDetails</c>：前端据此显示补缴入口还是「请联系管理员」。</summary>
public sealed class SubscriptionSuspendedDetails
{
    /// <summary>账户境况（恒为 <see cref="BillingStanding.Suspended"/>）。</summary>
    public BillingStanding Standing { get; set; }

    /// <summary>调用者能否为账户付款、换卡。</summary>
    public bool CanManage { get; set; }
}

/// <summary>从主体里取用户 id：与框架 <c>ICurrentUser</c> 同一个 claim，HTTP 与 SignalR 两侧共用。</summary>
internal static class SubscriptionAccessGateUser
{
    public static Guid? IdOf(ClaimsPrincipal? user)
        => user?.FindFirst(ClaimTypes.NameIdentifier)?.Value is string id && Guid.TryParse(id, out var guid) ? guid : null;
}
