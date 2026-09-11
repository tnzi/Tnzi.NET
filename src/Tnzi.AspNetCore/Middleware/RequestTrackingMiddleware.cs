
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

    /// <summary>
    /// 默认排除路径（当用户未配置 ExcludePaths 时使用）。
    /// "/hubs/*" 必须排除：SignalR WebSocket/SSE 经 access_token 查询参数携带 JWT，
    /// 本中间件原样记录 QueryString，不排除会把完整令牌写进请求日志。
    /// 注意匹配是锚定全串（* 为通配符），故须带 "/*" 才能覆盖 "/hubs/chat" 等子路径。
    /// </summary>
    private static readonly List<string> DefaultExcludePaths =
    [
        "/health", "/metrics", "/favicon.ico", "/swagger", "/api-docs", "/hubs/*"
    ];

    /// <summary>
    /// 请求体 / 响应体的脱敏器。无状态，共用一个实例。
    /// </summary>
    private static readonly RequestBodyRedactor BodyRedactor = new();

    /// <summary>
    /// 无论开关如何，这些路径下的请求体与响应体一律不采集。
    /// </summary>
    /// <remarks>
    /// ★★★ <strong>脱敏是按字段名做的，而认证端点上「哪个字段是凭据」并不总是猜得到。</strong>
    /// 登录响应把令牌放在 <c>data.accessToken</c>（名单能盖住），但 OAuth 回调返回的是一整页
    /// HTML、验证码相关端点返回的是图片的 base64、错误信封里还会带临时令牌的细节对象。
    /// 与其逐个补名单，不如整条路径不采集 —— 这些端点的请求体与响应体<b>没有一个字段</b>
    /// 是运维排障时非看不可的，而其中随便哪一个泄漏都等于账号失守。
    /// </remarks>
    private static readonly string[] BodyExcludedPathSegments =
    [
        "/auth/", "/connect/"
    ];

    /// <summary>这条请求的体是否允许采集。</summary>
    private static bool AllowsBodyCapture(HttpContext context)
    {
        var path = context.Request.Path.Value;
        if (string.IsNullOrEmpty(path))
        {
            return true;
        }

        // 尾部补一个 '/'，让 "/api/auth" 这种不带尾斜杠的写法也命中 "/auth/"。
        var probe = path.EndsWith('/') ? path : path + "/";
        foreach (var segment in BodyExcludedPathSegments)
        {
            if (probe.Contains(segment, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 按敏感字段名脱敏一段 JSON 体；不是合法 JSON 时原样返回。
    /// </summary>
    /// <remarks>
    /// ★★★ <strong>此前这两个开关完全不脱敏。</strong>查询串有一套（见
    /// <see cref="QueryStringRedactor.DefaultSensitiveKeys"/>），而请求体与响应体是
    /// <c>RequestBody = requestBody</c> 直接赋值 —— 于是打开 <c>LogRequestBody</c>，
    /// <c>POST auth/login</c> 的密码明文进日志；打开 <c>LogResponseBody</c>，
    /// 登录响应里的访问令牌与刷新令牌进日志。两个开关都带 <c>[RuntimeSetting]</c>，
    /// 在配置中心点一下就能打开，通常发生在排障当下、然后忘了关。
    /// 名单与审计模块共用核心的那一份（<see cref="RequestBodyRedactor.DefaultSensitiveFields"/>）。
    /// </remarks>
    private static string? RedactBody(string? body)
        => string.IsNullOrWhiteSpace(body)
            ? body
            : BodyRedactor.Redact(body, RequestBodyRedactor.DefaultSensitiveFields);

    /// <summary>
    /// 把敏感参数的值替换成 <c>***</c>,其余原样保留。
    /// </summary>
    /// <remarks>
    /// 名单与算法都在核心的 <see cref="QueryStringRedactor"/>：审计模块把同一条查询串存进
    /// <c>Audit_Operation.Url</c>，两处各维护一份名单的结果是「审计表比请求日志多露出一个键」。
    /// 部署自己给的名单<b>替换</b>默认名单，不是叠加。
    /// </remarks>
    private static string? RedactQueryString(IQueryCollection query, string? raw, RequestTrackingOptions options)
    {
        if (string.IsNullOrEmpty(raw) || query.Count == 0)
            return raw;

        IReadOnlyCollection<string> keys = options.SensitiveQueryKeys is { Count: > 0 }
            ? options.SensitiveQueryKeys
            : QueryStringRedactor.DefaultSensitiveKeys;

        return QueryStringRedactor.Redact(query, raw, keys);
    }

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

        // 认证端点整条不采集体（见 BodyExcludedPathSegments）。
        var allowsBodyCapture = AllowsBodyCapture(context);

        // 读取请求体（如果启用）
        string? requestBody = null;
        if (trackingOptions.LogRequestBody && allowsBodyCapture)
        {
            // ★ 先脱敏再截断，与下面的响应体同序。反过来会把 JSON 截成非法串，
            // 脱敏器于是原样返回 —— 只有超过 MaxRequestBodyLength 的请求泄漏凭据。
            var rawBody = await ReadRequestBodyAsync(context);
            requestBody = Truncate(RedactBody(rawBody) ?? string.Empty, trackingOptions.MaxRequestBodyLength);
        }

        // 启用响应缓冲（如果需要记录响应）
        MemoryStream? responseBuffer = null;
        Stream? originalResponseBody = null;

        if (trackingOptions.LogResponseBody && allowsBodyCapture)
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
                    QueryString = RedactQueryString(context.Request.Query, context.Request.QueryString.Value, trackingOptions),
                    // 走 GetClientIp 而不是直接读 Connection：一来它支持反向代理（直接读连接
                    // 在代理后面拿到的是代理地址，没有价值），二来它是隐私开关
                    // AspNetCoreOptions.CollectClientIpAddress 的唯一判定点。
                    IpAddress = context.Request.GetClientIp(),
                    UserAgent = context.Request.Headers["User-Agent"].ToString(),
                    RequestBody = requestBody,
                    StatusCode = context.Response.StatusCode,
                    DurationMs = duration,
                    IsSlow = isSlow,
                    UserId = userId
                };

                // 读取响应体（使用 leaveOpen 保留 buffer 供后续复制）
                if (trackingOptions.LogResponseBody && responseBuffer != null)
                {
                    responseBuffer.Position = 0;
                    using var reader = new StreamReader(responseBuffer, leaveOpen: true);
                    var responseBodyText = await reader.ReadToEndAsync();
                    // ★ 先截断再脱敏会把 JSON 截成非法串，脱敏器于是原样返回 —— 顺序不能反。
                    logEntry.ResponseBody = Truncate(RedactBody(responseBodyText) ?? string.Empty, trackingOptions.MaxResponseBodyLength);
                }

                _logger.Log(logLevel, "{@RequestLog}", logEntry);
            }
        }
        finally
        {
            // 始终将响应缓冲区复制回原始流（无论是否记录日志）
            if (originalResponseBody != null)
            {
                if (responseBuffer is { Length: > 0 })
                {
                    responseBuffer.Position = 0;
                    await responseBuffer.CopyToAsync(originalResponseBody);
                }
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

        // 检查排除路径（使用用户配置或默认值）
        var excludePaths = options.ExcludePaths ?? DefaultExcludePaths;
        if (excludePaths.Any(pattern =>
                MatchesPattern(path, pattern)))
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
    /// <remarks>
    /// ★ 调用方给的标识<b>要过字形校验</b>才作数：它会被写回响应头，
    /// 带换行的值会让 Kestrel 在写头时抛异常，而任意长的值会跟着每条日志走。
    /// 校验不过就当作没给 —— 追踪照常，只是链路对不上。见 <see cref="RequestIdentifier"/>。
    /// </remarks>
    private static string GetOrGenerateRequestId(HttpContext context)
    {
        // 优先从请求头获取
        var requestId = RequestIdentifier.Accept(context.Request.Headers["X-Request-Id"].FirstOrDefault())
            ?? RequestIdentifier.Accept(context.Request.Headers["X-Trace-Id"].FirstOrDefault())
            ?? RequestIdentifier.Accept(context.Request.Headers["X-Correlation-Id"].FirstOrDefault());

        // 如果不存在（或不可用），生成新的 RequestId
        return string.IsNullOrEmpty(requestId) ? Guid.NewGuid().ToString("N") : requestId;
    }

    /// <summary>
    /// 读取请求体（原文，不截断）。
    /// </summary>
    /// <remarks>
    /// ★ <strong>刻意不在这里截断。</strong>脱敏按 JSON 解析，
    /// 而截断过的 JSON 是非法串、脱敏器只能原样返回。截断必须发生在脱敏之后。
    /// </remarks>
    private static async Task<string?> ReadRequestBodyAsync(HttpContext context)
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

        return body;
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