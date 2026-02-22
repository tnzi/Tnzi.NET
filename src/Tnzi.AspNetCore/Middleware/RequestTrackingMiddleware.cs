
namespace Tnzi.AspNetCore.Middleware;

/// <summary>
/// 请求追踪中间件（增强版）
/// 自动生成和传递 RequestId，用于请求追踪和日志关联
/// 支持详细的请求/响应日志记录
/// </summary>
public class RequestTrackingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestTrackingMiddleware> _logger;
    private readonly IOptionsMonitor<AspNetCoreOptions> _aspNetCoreOptions;
    private readonly IOptionsMonitor<RequestTrackingOptions> _trackingOptions;

    /// <summary>
    /// Regex 缓存，避免每次请求都重新编译正则表达式
    /// </summary>
    private static readonly ConcurrentDictionary<string, Regex> _regexCache = new();

    public RequestTrackingMiddleware(
        RequestDelegate next,
        ILogger<RequestTrackingMiddleware> logger,
        IOptionsMonitor<AspNetCoreOptions> aspNetCoreOptions,
        IOptionsMonitor<RequestTrackingOptions> trackingOptions)
    {
        _next = Check.NotNull(next);
        _logger = Check.NotNull(logger);
        _aspNetCoreOptions = Check.NotNull(aspNetCoreOptions);
        _trackingOptions = Check.NotNull(trackingOptions);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var aspNetCoreOptions = _aspNetCoreOptions.CurrentValue;
        var trackingOptions = _trackingOptions.CurrentValue;

        // 注意：如果启用了 HTTP 加密，EnableBuffering 应该在加密中间件中处理
        // 这里只在未启用加密时启用缓冲
        if (aspNetCoreOptions.HttpEncrypt?.Enabled != true)
        {
            // 未启用加密时，早期启用请求体缓冲
            if (trackingOptions.LogRequestBody || trackingOptions.LogResponseBody)
            {
                context.Request.EnableBuffering();
            }
        }

        // 获取或生成 RequestId
        var requestId = GetOrGenerateRequestId(context);

        // 存储到 HttpContext.Items
        context.Items["RequestId"] = requestId;

        // 添加到响应头
        context.Response.Headers["X-Request-Id"] = requestId;

        // 检查是否需要记录
        if (!ShouldLogRequest(context, trackingOptions))
        {
            await _next(context);
            return;
        }

        // 记录请求开始（使用 Stopwatch 获取高精度计时）
        var stopwatch = Stopwatch.StartNew();
        var logLevel = trackingOptions.LogLevel;
        var isSlow = false;

        // 读取请求体（如果启用）
        string? requestBody = null;
        if (trackingOptions.LogRequestBody)
        {
            requestBody = await ReadRequestBodyAsync(context, trackingOptions.MaxRequestBodyLength);
        }

        // 启用响应缓冲（如果需要记录响应）
        MemoryStream? responseBuffer = null;
        Stream? originalResponseBody = null;

        if (trackingOptions.LogResponseBody)
        {
            responseBuffer = new MemoryStream();
            originalResponseBody = context.Response.Body;
            context.Response.Body = responseBuffer;
        }

        try
        {
            await _next(context);

            // 计算响应时间
            stopwatch.Stop();
            var duration = stopwatch.Elapsed.TotalMilliseconds;
            isSlow = trackingOptions.SlowRequestThresholdMs.HasValue &&
                      duration > trackingOptions.SlowRequestThresholdMs;

            // 如果是慢请求，提升日志级别
            if (isSlow && logLevel < LogLevel.Warning)
                logLevel = LogLevel.Warning;

            // 记录请求日志
            if (_logger.IsEnabled(logLevel))
            {
                var userId = GetUserId(context);
                var logEntry = new RequestLogEntry
                {
                    RequestId = requestId,
                    Method = context.Request.Method,
                    Path = context.Request.Path.Value ?? "",
                    QueryString = context.Request.QueryString.Value,
                    IpAddress = context.Connection.RemoteIpAddress?.ToString(),
                    UserAgent = context.Request.Headers["User-Agent"].ToString(),
                    RequestBody = requestBody,
                    StatusCode = context.Response.StatusCode,
                    DurationMs = duration,
                    IsSlow = isSlow,
                    UserId = userId
                };

                // 读取响应体
                if (trackingOptions.LogResponseBody && responseBuffer != null)
                {
                    responseBuffer.Position = 0;
                    using var reader = new StreamReader(responseBuffer);
                    var responseBodyText = await reader.ReadToEndAsync();
                    logEntry.ResponseBody = Truncate(responseBodyText, trackingOptions.MaxResponseBodyLength);

                    // 复制响应体到原始流
                    responseBuffer.Position = 0;
                    await responseBuffer.CopyToAsync(originalResponseBody!);
                }

                _logger.Log(logLevel, "{@RequestLog}", logEntry);
            }
        }
        finally
        {
            // 恢复响应体流
            if (originalResponseBody != null)
            {
                context.Response.Body = originalResponseBody;
            }
            responseBuffer?.Dispose();
        }
    }

    /// <summary>
    /// 检查是否应该记录该请求
    /// </summary>
    private bool ShouldLogRequest(HttpContext context, RequestTrackingOptions options)
    {
        if (!options.EnableRequestLogging)
            return false;

        var path = context.Request.Path.Value ?? "";

        // 检查排除路径
        if (options.ExcludePaths?.Any(pattern =>
                MatchesPattern(path, pattern)) == true)
            return false;

        // 检查包含路径
        if (options.IncludePaths?.Any(pattern =>
                MatchesPattern(path, pattern)) == false &&
            options.IncludePaths.Count > 0)
            return false;

        return true;
    }

    /// <summary>
    /// 匹配路径模式（使用缓存的 Regex 实例）
    /// </summary>
    private static bool MatchesPattern(string path, string pattern)
    {
        var regex = _regexCache.GetOrAdd(pattern, p =>
            new Regex("^" + Regex.Escape(p).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
                RegexOptions.Compiled));
        return regex.IsMatch(path);
    }

    /// <summary>
    /// 获取或生成 RequestId
    /// </summary>
    private static string GetOrGenerateRequestId(HttpContext context)
    {
        // 优先从请求头获取
        var requestId = context.Request.Headers["X-Request-Id"].FirstOrDefault()
            ?? context.Request.Headers["X-Trace-Id"].FirstOrDefault()
            ?? context.Request.Headers["X-Correlation-Id"].FirstOrDefault();

        // 如果不存在，生成新的 RequestId
        if (string.IsNullOrEmpty(requestId))
        {
            requestId = Guid.NewGuid().ToString("N");
        }

        return requestId;
    }

    /// <summary>
    /// 读取请求体
    /// </summary>
    private static async Task<string?> ReadRequestBodyAsync(HttpContext context, int maxLength)
    {
        if (!context.Request.Body.CanSeek)
        {
            // 如果流不支持定位，需要启用缓冲
            context.Request.EnableBuffering();
        }

        context.Request.Body.Position = 0;

        using var reader = new StreamReader(
            context.Request.Body,
            leaveOpen: true);

        var body = await reader.ReadToEndAsync();

        // 恢复流位置
        context.Request.Body.Position = 0;

        return Truncate(body, maxLength);
    }

    /// <summary>
    /// 获取当前用户 ID
    /// </summary>
    private static string? GetUserId(HttpContext context)
    {
        var currentUser = context.RequestServices.GetService<ICurrentUser>();
        return currentUser?.Id?.ToString();
    }

    /// <summary>
    /// 截断字符串到指定长度
    /// </summary>
    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;
        return value.Substring(0, maxLength) + "... (truncated)";
    }
}