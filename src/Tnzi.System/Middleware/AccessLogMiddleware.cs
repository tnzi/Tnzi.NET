namespace Tnzi.System.Middleware;

/// <summary>
/// 访问日志采集中间件：响应结束后把这一次请求（路径 / 方法 / 状态码 / 耗时 / 来源地址 / UA / 用户）
/// 投进 <see cref="IAccessLogSender"/>，由后台服务成批落库并富化。
/// </summary>
/// <remarks>
/// <para>
/// ★ 这是 <c>Sys_AccessLog</c> 的<b>第一个</b>生产者。此前 8 个查询端点、后台富化、管理页与仪表盘 KPI
/// 全围着一张恒空的表，唯一的写入口是要 <c>system.accessLog.create</c> 的管理端手工 POST，
/// 而文档写着「自动记录 API 访问日志」——机制完整、外观正常、从未生效。
/// </para>
/// <para>
/// 采集按 <see cref="AccessLogOptions.Enabled"/> 开关（默认关，opt-in），排除路径与审计模块同一初值；
/// 两者都经 <see cref="IOptionsMonitor{TOptions}"/> 逐请求热读。来源地址走
/// <see cref="HttpContextExtensions.GetClientIp(HttpContext)"/>：不读调用方可随便写的转发头，
/// 受信代理由 <c>AspNetCore:TrustedProxies</c> 声明并由 <c>UseForwardedHeaders</c> 翻译，
/// 隐私开关 <c>CollectClientIpAddress</c> 关掉时恒为 null。
/// </para>
/// <para>
/// 访问日志是旁路：投递失败只记 Warning，绝不让请求失败；下游异常原样上抛，但这一条按客户端<b>实际会收到</b>的码记下来
/// （异常还没到外层的异常处理中间件，此刻 <c>Response.StatusCode</c> 仍是 200）：经 <see cref="ExceptionHttpStatusCode"/>
/// 折算，服务层抛的 <c>ForbiddenException</c> / <c>NotFoundException</c> 是 403 / 404 而不是 500 —— 一律记 500 会让
/// 「服务器错误」的统计被业务拒绝灌满。路径收敛到列宽：一个超长 URL 不能让整批 <c>InsertMany</c> 失败。
/// </para>
/// <para>
/// ★ 挂载位置是 <see cref="RequestPipelineStage.AfterAuthentication"/>（认证之后、限流与授权之前），由 <c>SystemModule</c>
/// 经 <c>AddRequestPipelineMiddleware</c> 登记。曾在 <c>OnApplicationInitializationAsync</c> 里追加在授权之后：那里只看得见
/// 通过了认证、限流与授权的请求，401 / 403 / 429 由授权与限流中间件就地写出、不再调用下游，最该被记下来的
/// 「谁被拒绝了」一条都记不到。
/// </para>
/// </remarks>
public class AccessLogMiddleware
{
    /// <summary>与 <c>AccessLogConfiguration</c> 里 <c>Path</c> 的列宽一致。</summary>
    public const int MaxPathLength = 500;

    private const int MaxMethodLength = 10;

    private readonly RequestDelegate _next;
    private readonly IAccessLogSender _sender;
    private readonly IOptionsMonitor<AccessLogOptions> _options;
    private readonly ILogger<AccessLogMiddleware> _logger;

    /// <summary>初始化一个 <see cref="AccessLogMiddleware"/> 实例。</summary>
    public AccessLogMiddleware(
        RequestDelegate next,
        IAccessLogSender sender,
        IOptionsMonitor<AccessLogOptions> options,
        ILogger<AccessLogMiddleware> logger)
    {
        _next = Check.NotNull(next);
        _sender = Check.NotNull(sender);
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    // 中间件是管线单例；ICurrentUser 是按请求的，由框架从 RequestServices 逐请求解析。
    public async Task InvokeAsync(HttpContext context, ICurrentUser currentUser)
    {
        var options = _options.CurrentValue;
        if (!options.Enabled || IsExcludedPath(context.Request.Path, options.ExcludedPaths))
        {
            await _next(context);
            return;
        }

        var startedAt = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        Exception? failure = null;

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            failure = ex;
            throw;
        }
        finally
        {
            stopwatch.Stop();
            await EnqueueAsync(context, currentUser, startedAt, stopwatch.ElapsedMilliseconds, failure);
        }
    }

    private async Task EnqueueAsync(HttpContext context, ICurrentUser currentUser, DateTime startedAt, long elapsedMilliseconds, Exception? failure)
    {
        try
        {
            var log = new AccessLogDto
            {
                UserId = currentUser.Id,
                UserName = currentUser.UserName,
                Path = Fit(context.Request.Path.Value ?? string.Empty, MaxPathLength),
                Method = Fit(context.Request.Method, MaxMethodLength),
                IpAddress = context.GetClientIp(),
                UserAgent = context.GetUserAgent(),
                StatusCode = failure is null ? context.Response.StatusCode : ExceptionHttpStatusCode.Resolve(failure),
                ResponseTime = elapsedMilliseconds,
                CreationTime = startedAt,
            };

            await _sender.SendAsync(log);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enqueue the access log for {Method} {Path}", context.Request.Method, context.Request.Path);
        }
    }

    private static bool IsExcludedPath(PathString path, string[] excludedPaths)
    {
        foreach (var excluded in excludedPaths)
        {
            if (path.StartsWithSegments(excluded))
                return true;
        }

        return false;
    }

    private static string Fit(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
