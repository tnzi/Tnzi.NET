namespace Tnzi.SignalR.Filters;

/// <summary>
/// Hub 速率限制过滤器。
/// 在连接建立时检查连接数与被封禁状态；在方法调用时检查消息速率并记录，超限则拒绝并可封禁。
/// 仅在 SignalR:RateLimit:Enabled = true 时注册。
///
/// ★ 分区键有两种：登录用户用用户 id，匿名连接用客户端 IP（取不到则退回连接 id）。
/// 此前只有前一种，取不到就整条放行 —— 于是匿名 Hub（<c>[AllowAnonymous]</c>，
/// 或未要求认证的 Hub 上认证失败的客户端）既不计连接数、不计消息速率，也不查封禁，
/// 而且既没有告警也没有配置项能看出这一点。匿名连接的处置由
/// <see cref="RateLimitOptions.AnonymousPolicy"/> 决定，默认按分区限流。
/// </summary>
public class RateLimitHubFilter : IHubFilter
{
    /// <summary>
    /// 匿名分区键存进 <c>Context.Items</c>，断开时要用同一个键把名额还回去。
    /// 重算的话，IP 的解析结果在连接生命周期里不保证还是同一个（代理头、
    /// HttpContext 已释放），还错了名额就永远漏在那里。
    /// </summary>
    private const string AnonymousPartitionItemKey = "Tnzi.SignalR.AnonymousRateLimitPartition";

    /// <summary>
    /// 「退回按连接分区」的一次性告警闸。过滤器实例是按连接/调用解析的，
    /// 每次都记会把日志刷满；而完全不记的话，一次部署级配置就让匿名连接数上限
    /// 静默失效 —— 默认值可以不动，失效必须看得见。
    /// </summary>
    private static int _connectionPartitionFallbackWarned;

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
        Check.NotNull(context);
        Check.NotNull(next);

        var userId = GetUserId(context.Context.User);
        if (userId.HasValue)
        {
            await CheckUserConnectionAsync(userId.Value, context.Context.ConnectionId);
            await next(context);
            return;
        }

        switch (_options.AnonymousPolicy)
        {
            case AnonymousHubRateLimitPolicy.Allow:
                break;

            case AnonymousHubRateLimitPolicy.Reject:
                _logger.LogWarning(
                    "Anonymous connection rejected by policy. ConnectionId: {ConnectionId}",
                    context.Context.ConnectionId);
                throw new HubException("Connection rejected. Anonymous connections are not accepted.");

            default:
                await CheckAnonymousConnectionAsync(context.Context);
                break;
        }

        await next(context);
    }

    /// <inheritdoc />
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        Check.NotNull(invocationContext);
        Check.NotNull(next);

        var userId = GetUserId(invocationContext.Context.User);
        var hubName = invocationContext.Hub.GetType().Name;

        if (userId.HasValue)
        {
            await CheckUserMessageAsync(userId.Value, hubName, invocationContext.HubMethodName);
            return await next(invocationContext);
        }

        if (_options.AnonymousPolicy == AnonymousHubRateLimitPolicy.Allow)
        {
            return await next(invocationContext);
        }

        // Reject 策略下匿名连接根本建立不起来；万一是先于本过滤器建立的旧连接，
        // 这里同样拒绝。
        if (_options.AnonymousPolicy == AnonymousHubRateLimitPolicy.Reject)
        {
            throw new HubException("Request rejected. Anonymous connections are not accepted.");
        }

        await CheckAnonymousMessageAsync(invocationContext.Context, hubName, invocationContext.HubMethodName);
        return await next(invocationContext);
    }

    /// <summary>
    /// 连接断开时减少连接计数
    /// 确保 RateLimitService 的连接数统计在断开时正确更新
    /// </summary>
    public async Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception, Func<HubLifetimeContext, Exception?, Task> next)
    {
        Check.NotNull(context);
        Check.NotNull(next);

        // 先执行后续处理（包括 ConnectionManager 的清理），再记录日志
        await next(context, exception);

        var userId = GetUserId(context.Context.User);
        if (userId.HasValue)
        {
            _logger.LogDebug(
                "User {UserId} disconnected. ConnectionId: {ConnectionId}",
                userId.Value, context.Context.ConnectionId);
            return;
        }

        // 匿名连接：把占用的名额还回去。用连接期存下的那个键，不重算。
        if (context.Context.Items.TryGetValue(AnonymousPartitionItemKey, out var stored)
            && stored is string partitionKey)
        {
            await _rateLimitService.ReleaseAnonymousConnectionAsync(partitionKey);
        }
    }

    private async Task CheckUserConnectionAsync(Guid userId, string connectionId)
    {
        if (await _rateLimitService.IsUserBannedAsync(userId))
        {
            _logger.LogWarning(
                "User {UserId} connection rejected: user is banned. ConnectionId: {ConnectionId}",
                userId, connectionId);
            throw new HubException("Connection rejected. You are temporarily banned due to rate limit violation.");
        }

        var allowed = await _rateLimitService.CheckConnectionLimitAsync(userId);
        if (!allowed)
        {
            _logger.LogWarning(
                "User {UserId} connection rejected: exceeded max connections. ConnectionId: {ConnectionId}",
                userId, connectionId);
            throw new HubException("Connection rejected. Maximum connections per user exceeded.");
        }
    }

    private async Task CheckAnonymousConnectionAsync(HubCallerContext context)
    {
        var partitionKey = ResolvePartitionKey(context);

        if (await _rateLimitService.IsAnonymousBannedAsync(partitionKey))
        {
            _logger.LogWarning(
                "Anonymous connection rejected: partition {Partition} is banned. ConnectionId: {ConnectionId}",
                partitionKey, context.ConnectionId);
            throw new HubException("Connection rejected. You are temporarily banned due to rate limit violation.");
        }

        if (!await _rateLimitService.TryAcquireAnonymousConnectionAsync(partitionKey))
        {
            throw new HubException("Connection rejected. Maximum connections exceeded.");
        }

        // 占用成功后才记键 —— 没占上的连接不该在断开时还一次名额。
        context.Items[AnonymousPartitionItemKey] = partitionKey;
    }

    private async Task CheckUserMessageAsync(Guid userId, string hubName, string methodName)
    {
        if (await _rateLimitService.IsUserBannedAsync(userId))
        {
            _logger.LogWarning(
                "User {UserId} hub method {HubName}.{MethodName} rejected: user is banned.",
                userId, hubName, methodName);
            throw new HubException("Request rejected. You are temporarily banned due to rate limit violation.");
        }

        var allowed = await _rateLimitService.CheckMessageRateLimitAsync(userId);
        if (!allowed)
        {
            await _rateLimitService.BanUserAsync(userId, _options.BanDuration);
            _logger.LogWarning(
                "User {UserId} exceeded message rate limit, banned for {Duration}. Hub: {HubName}.{MethodName}",
                userId, _options.BanDuration, hubName, methodName);
            throw new HubException("Message rate limit exceeded. You have been temporarily banned.");
        }

        // 先记录消息再执行，防止恶意用户通过发送导致异常的请求绕过限流
        await _rateLimitService.RecordMessageAsync(userId);
    }

    private async Task CheckAnonymousMessageAsync(HubCallerContext context, string hubName, string methodName)
    {
        var partitionKey = ResolvePartitionKey(context);

        if (await _rateLimitService.IsAnonymousBannedAsync(partitionKey))
        {
            _logger.LogWarning(
                "Anonymous hub method {HubName}.{MethodName} rejected: partition {Partition} is banned.",
                hubName, methodName, partitionKey);
            throw new HubException("Request rejected. You are temporarily banned due to rate limit violation.");
        }

        if (!await _rateLimitService.CheckAnonymousMessageRateLimitAsync(partitionKey))
        {
            await _rateLimitService.BanAnonymousAsync(partitionKey, _options.BanDuration);
            _logger.LogWarning(
                "Anonymous partition {Partition} exceeded message rate limit, banned for {Duration}. Hub: {HubName}.{MethodName}",
                partitionKey, _options.BanDuration, hubName, methodName);
            throw new HubException("Message rate limit exceeded. You have been temporarily banned.");
        }

        // 与按用户那条一样：先记录再执行，避免用会抛异常的调用绕过计数
        await _rateLimitService.RecordAnonymousMessageAsync(partitionKey);
    }

    /// <summary>
    /// 匿名连接的分区键：连接期存下来的那个优先，其次客户端 IP，最后退回连接 ID。
    ///
    /// 退回连接 ID 时每条连接自成一区：消息速率与封禁仍然按连接生效（有意义），
    /// 但**连接数上限等同于每区一条，实际不再约束任何东西**。这比整条放行强得多，
    /// 却也确实是一次降级，所以进程内首次发生时记一条 Warning。
    ///
    /// ★ 最常见的触发原因不是"拿不到地址"，而是一次部署级隐私决策：
    /// <c>AspNetCore:CollectClientIpAddress = false</c> 会让 <c>GetClientIp()</c> 恒返回 null
    /// （那是全框架采集来源地址的唯一入口）。这两项配置各自都合理，叠在一起才让
    /// 匿名连接数上限失效 —— 正是不会有人主动发现的那种组合。
    /// </summary>
    private string ResolvePartitionKey(HubCallerContext context)
    {
        if (context.Items.TryGetValue(AnonymousPartitionItemKey, out var stored) && stored is string cached)
        {
            return cached;
        }

        var ip = context.GetHttpContext()?.Request?.GetClientIp();
        if (!string.IsNullOrWhiteSpace(ip))
        {
            return $"ip:{ip}";
        }

        if (Interlocked.Exchange(ref _connectionPartitionFallbackWarned, 1) == 0)
        {
            _logger.LogWarning(
                "SignalR anonymous rate limiting has no client IP to partition by and is falling back to "
                + "per-connection partitions. Message rate limits and bans still apply per connection, but "
                + "MaxConnectionsPerAnonymousPartition no longer constrains anything. The usual cause is "
                + "AspNetCore:CollectClientIpAddress = false. Set SignalR:RateLimit:AnonymousPolicy = Reject "
                + "if anonymous hub connections should not be accepted at all.");
        }

        return $"conn:{context.ConnectionId}";
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
