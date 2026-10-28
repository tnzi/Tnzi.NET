
namespace Tnzi.Performance.Middleware;

/// <summary>
/// 性能分析中间件
/// 收集请求性能指标，包括响应时间、请求/响应大小等
/// </summary>
/// <remarks>
/// <para>
/// ★ 挂载位置是 <see cref="RequestPipelineStage.AfterAuthentication"/>（认证之后、限流与授权之前），由 <see cref="PerformanceModule"/>
/// 经 <c>AddRequestPipelineMiddleware</c> 登记。追加在授权之后的话，401 / 403 / 429 由授权与限流中间件就地写出、不再调用下游，
/// 样本里只剩通过了的请求，故障期间的错误数与 P95 反而更好看。
/// </para>
/// <para>
/// ★ 抛异常的请求同样计入样本，状态码按客户端将会收到的那个记（<see cref="ExceptionHttpStatusCode.Resolve(HttpContext, Exception)"/>：
/// 业务异常是它自己的码，客户端断开是 499）。异常本身由外层的异常处理中间件统一记录，这里不再重复记一条 Error。
/// </para>
/// <para>
/// ★ 长连接不计入：WebSocket、SignalR Hub 连接（含 SSE / 长轮询传输）与 <c>text/event-stream</c> 响应的「耗时」是连接存活时长，
/// 计入的话慢请求列表、最大值与 P99 会被小时级的连接占满，真正慢的接口反而看不见。
/// </para>
/// <para>
/// ★ 端点统计按路由模板分组（<see cref="PerformanceMetrics.Route"/>），不按原始路径：<c>/api/users/{id}</c> 不该按每个 id 拆成一行。
/// 没有匹配端点的请求（扫描器、拼错的地址）一律归入 <see cref="UnmatchedRoute"/>，原始路径只保留在样本明细里。
/// </para>
/// </remarks>
public class PerformanceMiddleware
{
    /// <summary>
    /// 没有匹配到任何端点的请求共用的路由键。按原始路径分组的话，扫描器一个请求就能造出一个「端点」。
    /// </summary>
    public const string UnmatchedRoute = "(unmatched)";

    private readonly RequestDelegate _next;
    private readonly ILogger<PerformanceMiddleware> _logger;
    private readonly IPerformanceCollector _performanceCollector;
    private readonly IOptionsMonitor<PerformanceOptions> _options;

    /// <summary>
    /// 初始化性能分析中间件。
    /// 中间件为单例，Options 必须经 <see cref="IOptionsMonitor{T}"/> 消费，
    /// 每次请求读 <see cref="IOptionsMonitor{T}.CurrentValue"/> 以支持配置中心热更新。
    /// </summary>
    public PerformanceMiddleware(
        RequestDelegate next,
        ILogger<PerformanceMiddleware> logger,
        IPerformanceCollector performanceCollector,
        IOptionsMonitor<PerformanceOptions> options)
    {
        _next = Check.NotNull(next);
        _logger = Check.NotNull(logger);
        _performanceCollector = Check.NotNull(performanceCollector);
        _options = Check.NotNull(options);
    }

    /// <summary>
    /// 处理请求
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        // 每请求读取最新配置快照（支持热更新）
        var options = _options.CurrentValue;

        if (!options.Enabled || !ShouldRecord(context, options) || context.IsLongLivedConnection())
        {
            await _next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();

        // 记录请求大小
        long? requestSize = null;
        if (options.RecordRequestSize && context.Request.ContentLength.HasValue)
        {
            requestSize = context.Request.ContentLength.Value;
        }

        // 使用计数包装流代替 MemoryStream 缓冲，避免大响应导致 OOM
        CountingStream? countingStream = null;
        if (options.RecordResponseSize)
        {
            countingStream = new CountingStream(context.Response.Body);
            context.Response.Body = countingStream;
        }

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

            // 确保在异常路径下也恢复原始响应体
            if (countingStream != null)
            {
                context.Response.Body = countingStream.InnerStream;
            }

            // 长连接不进样本；事件流只有在响应写出之后才认得出来，所以写完再判一次
            if (!context.IsLongLivedConnection())
            {
                TryRecord(context, options, stopwatch.Elapsed.TotalMilliseconds, requestSize, countingStream?.BytesWritten, failure);
            }
        }
    }

    /// <summary>
    /// 采样是旁路：它在 <c>finally</c> 里执行，自己出错不能顶掉下游正在上抛的异常，也不能让一个成功的请求失败。
    /// </summary>
    private void TryRecord(HttpContext context, PerformanceOptions options, double duration, long? requestSize, long? responseSize, Exception? failure)
    {
        try
        {
            Record(context, options, duration, requestSize, responseSize, failure);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record performance metrics for {Method} {Path}", context.Request.Method, context.GetRedactedPath());
        }
    }

    private void Record(HttpContext context, PerformanceOptions options, double duration, long? requestSize, long? responseSize, Exception? failure)
    {
        // 记进指标与日志的路径：值命中敏感名单的路由参数（签署 / 分享链接的 {token}）换成掩码。
        var path = context.GetRedactedPath();
        var method = context.Request.Method;
        var statusCode = failure is null ? context.Response.StatusCode : ExceptionHttpStatusCode.Resolve(context, failure);

        var currentUser = context.RequestServices?.GetService<ICurrentUser>();
        var userId = currentUser?.Id?.ToString();
        var requestId = context.Items["RequestId"]?.ToString() ?? context.TraceIdentifier;

        _performanceCollector.Record(new PerformanceMetrics(
            path,
            method,
            statusCode,
            duration,
            requestSize,
            responseSize,
            userId,
            requestId,
            DateTime.UtcNow,
            context.GetEndpointRouteTemplate() ?? UnmatchedRoute));

        if (options.SlowRequestThresholdMs.HasValue && duration > options.SlowRequestThresholdMs)
        {
            _logger.LogWarning(
                "Slow request detected - Path: {Path}, Method: {Method}, Duration: {Duration}ms, StatusCode: {StatusCode}, RequestId: {RequestId}",
                path, method, duration, statusCode, requestId);
        }
    }

    /// <summary>
    /// 检查是否应该记录该路径的性能数据
    /// </summary>
    private static bool ShouldRecord(HttpContext context, PerformanceOptions options)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // 检查排除列表
        if (options.ExcludePaths != null && options.ExcludePaths.Length > 0)
        {
            if (options.ExcludePaths.Any(excludePath =>
                path.StartsWith(excludePath, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        // 检查包含列表
        if (options.IncludePaths != null && options.IncludePaths.Length > 0)
        {
            return options.IncludePaths.Any(includePath =>
                path.StartsWith(includePath, StringComparison.OrdinalIgnoreCase));
        }

        return true;
    }
}

/// <summary>
/// 计数包装流，直接将数据写入内部流并记录写入字节数
/// 避免使用 MemoryStream 缓冲整个响应导致 OOM
/// </summary>
internal sealed class CountingStream : Stream
{
    public Stream InnerStream { get; }

    /// <summary>
    /// 已写入的字节数
    /// </summary>
    public long BytesWritten { get; private set; }

    public CountingStream(Stream innerStream)
    {
        InnerStream = Check.NotNull(innerStream);
    }

    public override bool CanRead => InnerStream.CanRead;
    public override bool CanSeek => InnerStream.CanSeek;
    public override bool CanWrite => InnerStream.CanWrite;
    public override long Length => InnerStream.Length;
    public override long Position
    {
        get => InnerStream.Position;
        set => InnerStream.Position = value;
    }

    public override void Flush() => InnerStream.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => InnerStream.FlushAsync(cancellationToken);
    public override int Read(byte[] buffer, int offset, int count) => InnerStream.Read(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin) => InnerStream.Seek(offset, origin);
    public override void SetLength(long value) => InnerStream.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count)
    {
        InnerStream.Write(buffer, offset, count);
        BytesWritten += count;
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        await InnerStream.WriteAsync(buffer, offset, count, cancellationToken);
        BytesWritten += count;
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await InnerStream.WriteAsync(buffer, cancellationToken);
        BytesWritten += buffer.Length;
    }

    // 不 dispose InnerStream (原始 Response.Body)，防止后续中间件无法写入
    protected override void Dispose(bool disposing) { }
    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
