
namespace Tnzi.Performance.Middleware;

/// <summary>
/// 性能分析中间件
/// 收集请求性能指标，包括响应时间、请求/响应大小等
/// </summary>
public class PerformanceMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<PerformanceMiddleware> _logger;
    private readonly IPerformanceCollector _performanceCollector;
    private readonly PerformanceOptions _options;

    /// <summary>
    /// 初始化性能分析中间件
    /// </summary>
    public PerformanceMiddleware(
        RequestDelegate next,
        ILogger<PerformanceMiddleware> logger,
        IPerformanceCollector performanceCollector,
        IOptions<PerformanceOptions> options)
    {
        _next = Check.NotNull(next);
        _logger = Check.NotNull(logger);
        _performanceCollector = Check.NotNull(performanceCollector);
        _options = Check.NotNull(options).Value;
    }

    /// <summary>
    /// 处理请求
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        // 检查是否启用
        if (!_options.Enabled)
        {
            await _next(context);
            return;
        }

        // 检查路径是否需要记录
        if (!ShouldRecord(context))
        {
            await _next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var path = context.Request.Path.Value ?? string.Empty;
        var method = context.Request.Method;

        // 记录请求大小
        long? requestSize = null;
        if (_options.RecordRequestSize && context.Request.ContentLength.HasValue)
        {
            requestSize = context.Request.ContentLength.Value;
        }

        // 使用计数包装流代替 MemoryStream 缓冲，避免大响应导致 OOM
        long? responseSize = null;
        CountingStream? countingStream = null;

        if (_options.RecordResponseSize)
        {
            countingStream = new CountingStream(context.Response.Body);
            context.Response.Body = countingStream;
        }

        try
        {
            await _next(context);

            // 计算响应时间
            stopwatch.Stop();
            var duration = stopwatch.Elapsed.TotalMilliseconds;

            // 获取响应大小（直接从计数流读取，无需缓冲复制）
            if (countingStream != null)
            {
                responseSize = countingStream.BytesWritten;
                context.Response.Body = countingStream.InnerStream;
            }

            // 获取用户ID和请求ID
            var currentUser = context.RequestServices.GetService<ICurrentUser>();
            var userId = currentUser?.Id?.ToString();
            var requestId = context.Items["RequestId"]?.ToString() ?? context.TraceIdentifier;

            // 记录性能指标
            var metrics = new PerformanceMetrics(
                path,
                method,
                context.Response.StatusCode,
                duration,
                requestSize,
                responseSize,
                userId,
                requestId,
                DateTime.UtcNow);

            _performanceCollector.Record(metrics);

            // 如果是慢请求，记录警告日志
            if (_options.SlowRequestThresholdMs.HasValue && duration > _options.SlowRequestThresholdMs)
            {
                _logger.LogWarning(
                    "Slow request detected - Path: {Path}, Method: {Method}, Duration: {Duration}ms, StatusCode: {StatusCode}, RequestId: {RequestId}",
                    path, method, duration, context.Response.StatusCode, requestId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error in performance middleware - Path: {Path}, Method: {Method}",
                path, method);
            throw;
        }
        finally
        {
            // 确保在异常路径下也恢复原始响应体
            if (countingStream != null)
            {
                context.Response.Body = countingStream.InnerStream;
            }
        }
    }

    /// <summary>
    /// 检查是否应该记录该路径的性能数据
    /// </summary>
    private bool ShouldRecord(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // 检查排除列表
        if (_options.ExcludePaths != null && _options.ExcludePaths.Length > 0)
        {
            if (_options.ExcludePaths.Any(excludePath =>
                path.StartsWith(excludePath, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        // 检查包含列表
        if (_options.IncludePaths != null && _options.IncludePaths.Length > 0)
        {
            return _options.IncludePaths.Any(includePath =>
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
}
