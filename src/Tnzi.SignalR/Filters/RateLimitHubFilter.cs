namespace Tnzi.SignalR.Filters;

/// <summary>
/// Hub 速率限制过滤器。
/// 在连接建立时检查连接数与被封禁状态；在方法调用时检查消息速率并记录，超限则拒绝并可封禁。
/// 仅在 SignalR:RateLimit:Enabled = true 时注册。
/// </summary>
public class RateLimitHubFilter : IHubFilter
{
    private readonly ILogger<RateLimitHubFilter> _logger;
    private readonly IRateLimitService _rateLimitService;
    private readonly RateLimitOptions _options;

    /// <summary>
    /// 初始化一个<see cref="RateLimitHubFilter"/>类型的新实例
    /// </summary>
    public RateLimitHubFilter(ILogger<RateLimitHubFilter> logger, IRateLimitService rateLimitService, IOptions<SignalROptions> signalROptions)
    {
        _logger = Check.NotNull(logger);
        _rateLimitService = Check.NotNull(rateLimitService);
        var rateLimit = Check.NotNull(signalROptions).Value.RateLimit
            ?? throw new InvalidOperationException("SignalR.RateLimit cannot be null when RateLimitHubFilter is used.");
        _options = rateLimit;
    }

    /// <inheritdoc />
    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        var userId = GetUserId(context.Context.User);
        if (!userId.HasValue)
        {
            await next(context);
            return;
        }

        if (await _rateLimitService.IsUserBannedAsync(userId.Value))
        {
            _logger.LogWarning(
                "User {UserId} connection rejected: user is banned. ConnectionId: {ConnectionId}",
                userId.Value, context.Context.ConnectionId);
            throw new HubException("Connection rejected. You are temporarily banned due to rate limit violation.");
        }

        var allowed = await _rateLimitService.CheckConnectionLimitAsync(userId.Value);
        if (!allowed)
        {
            _logger.LogWarning(
                "User {UserId} connection rejected: exceeded max connections. ConnectionId: {ConnectionId}",
                userId.Value, context.Context.ConnectionId);
            throw new HubException("Connection rejected. Maximum connections per user exceeded.");
        }

        await next(context);
    }

    /// <inheritdoc />
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var user = invocationContext.Context.User;
        var userId = GetUserId(user);
        if (!userId.HasValue)
        {
            return await next(invocationContext);
        }

        if (await _rateLimitService.IsUserBannedAsync(userId.Value))
        {
            _logger.LogWarning(
                "User {UserId} hub method {HubName}.{MethodName} rejected: user is banned.",
                userId.Value, invocationContext.Hub.GetType().Name, invocationContext.HubMethodName);
            throw new HubException("Request rejected. You are temporarily banned due to rate limit violation.");
        }

        var allowed = await _rateLimitService.CheckMessageRateLimitAsync(userId.Value);
        if (!allowed)
        {
            await _rateLimitService.BanUserAsync(userId.Value, _options.BanDuration);
            _logger.LogWarning(
                "User {UserId} exceeded message rate limit, banned for {Duration}. Hub: {HubName}.{MethodName}",
                userId.Value, _options.BanDuration, invocationContext.Hub.GetType().Name, invocationContext.HubMethodName);
            throw new HubException("Message rate limit exceeded. You have been temporarily banned.");
        }

        var result = await next(invocationContext);
        await _rateLimitService.RecordMessageAsync(userId.Value);
        return result;
    }

    /// <summary>
    /// 连接断开时减少连接计数
    /// 确保 RateLimitService 的连接数统计在断开时正确更新
    /// </summary>
    public async Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception, Func<HubLifetimeContext, Exception?, Task> next)
    {
        // 先执行后续处理（包括 ConnectionManager 的清理），再记录日志
        await next(context, exception);

        var userId = GetUserId(context.Context.User);
        if (userId.HasValue)
        {
            _logger.LogDebug(
                "User {UserId} disconnected. ConnectionId: {ConnectionId}",
                userId.Value, context.Context.ConnectionId);
        }
    }

    private static Guid? GetUserId(ClaimsPrincipal? user)
    {
        var claim = user?.FindFirst(ClaimTypes.NameIdentifier) ?? user?.FindFirst("sub");
        if (claim != null && Guid.TryParse(claim.Value, out var id))
        {
            return id;
        }

        return null;
    }
}
