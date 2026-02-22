
namespace Tnzi.SignalR.Filters;

/// <summary>
/// Hub 日志过滤器
/// 记录连接、断开和方法调用
/// </summary>
public class LoggingHubFilter : IHubFilter
{
    private readonly ILogger<LoggingHubFilter> _logger;

    /// <summary>
    /// 初始化一个<see cref="LoggingHubFilter"/>类型的新实例
    /// </summary>
    /// <param name="logger">日志记录器</param>
    public LoggingHubFilter(ILogger<LoggingHubFilter> logger)
    {
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 调用 Hub 方法时的日志记录
    /// </summary>
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var stopwatch = Stopwatch.StartNew();
        var hubName = invocationContext.Hub.GetType().Name;
        var methodName = invocationContext.HubMethodName;
        var userId = invocationContext.Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? invocationContext.Context.User?.Identity?.Name
            ?? "Anonymous";

        _logger.LogDebug(
            "Hub method {HubName}.{MethodName} invoked by user {UserId}",
            hubName, methodName, userId);

        try
        {
            var result = await next(invocationContext);
            stopwatch.Stop();

            _logger.LogDebug(
                "Hub method {HubName}.{MethodName} completed in {ElapsedMs}ms",
                hubName, methodName, stopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "Hub method {HubName}.{MethodName} failed after {ElapsedMs}ms",
                hubName, methodName, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    /// <summary>
    /// 连接建立时的日志记录
    /// </summary>
    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        var hubName = context.Hub.GetType().Name;
        var userId = context.Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.Context.User?.Identity?.Name
            ?? "Anonymous";
        var connectionId = context.Context.ConnectionId;

        _logger.LogInformation(
            "User {UserId} connected to {HubName}. ConnectionId: {ConnectionId}",
            userId, hubName, connectionId);

        await next(context);
    }

    /// <summary>
    /// 连接断开时的日志记录
    /// </summary>
    public async Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception, Func<HubLifetimeContext, Exception?, Task> next)
    {
        var hubName = context.Hub.GetType().Name;
        var userId = context.Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.Context.User?.Identity?.Name
            ?? "Anonymous";
        var connectionId = context.Context.ConnectionId;

        if (exception != null)
        {
            _logger.LogWarning(exception,
                "User {UserId} disconnected from {HubName} with error. ConnectionId: {ConnectionId}",
                userId, hubName, connectionId);
        }
        else
        {
            _logger.LogInformation(
                "User {UserId} disconnected from {HubName}. ConnectionId: {ConnectionId}",
                userId, hubName, connectionId);
        }

        await next(context, exception);
    }
}