
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
        // 构造签名保留 aspNetCoreOptions（UseMiddleware 按它解析）；此前只用它判 HttpEncrypt 决定是否提前开缓冲，
        // 缓冲现在按需开，这里不再需要它
        Check.NotNull(aspNetCoreOptions);
        _trackingOptions = Check.NotNull(trackingOptions);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var trackingOptions = _trackingOptions.CurrentValue;

        // 请求体缓冲不在这里开：EnableBuffering 把 Body 换成 FileBufferingReadStream，下游每读一次都被复制进缓冲、
        // 超过 30 KB 溢出到临时文件 —— 闸门决定「不采」的体（multipart 上传、超上界的体、只开了 LogResponseBody）
        // 此前仍被整条落盘一份。缓冲由 CaptureRequestBodyAsync 在 Content-Type / Content-Length 闸门之后按需开。

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
            requestBody = await CaptureRequestBodyAsync(context, trackingOptions);
        }

        // 响应体采集：直通式，每次写入立刻转发给原始流，旁路只留前 MaxCapturedBodyBytes 字节。
        // ★ 不能换成 MemoryStream 等 action 跑完再拷回：那会把整条下载在内存里多存一份，
        //   并让 SSE / 分块流式端点变成「等全部生成完再一次性吐出」（2026-09-12 修复）。
        BoundedResponseCaptureStream? responseCapture = null;
        Stream? originalResponseBody = null;

        if (trackingOptions.LogResponseBody && allowsBodyCapture)
        {
            originalResponseBody = context.Response.Body;
            responseCapture = new BoundedResponseCaptureStream(
                originalResponseBody, context.Response, trackingOptions.MaxCapturedBodyBytes);
            context.Response.Body = responseCapture;
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

                if (trackingOptions.LogResponseBody && responseCapture != null)
                {
                    logEntry.ResponseBody = DescribeCapturedResponse(responseCapture, trackingOptions);
                }

                _logger.Log(logLevel, "{@RequestLog}", logEntry);
            }
        }
        finally
        {
            // 写入早已直通到原始流；这里只把流换回去并放掉旁路缓冲。
            if (originalResponseBody != null)
            {
                context.Response.Body = originalResponseBody;
            }
            responseCapture?.Dispose();
        }
    }

    /// <summary>
    /// 采集请求体：先按 Content-Type 与 Content-Length 闸住，再有界地读，最后脱敏、截断。
    /// </summary>
    /// <remarks>
    /// ★ 闸门在<b>读之前</b>。<see cref="RequestTrackingOptions.MaxRequestBodyLength"/> 只裁剪已经在内存里的字符串，
    /// 此前读侧是 <c>ReadToEndAsync()</c>，打开开关后一次 100 MB 的 multipart 上传就是 ~200 MB 的 UTF-16 string
    /// 再交给 JSON 脱敏器（2026-09-12 修复）。超过上界的体不记而不是记一半：截断过的 JSON 无法脱敏。
    /// </remarks>
    private static async Task<string> CaptureRequestBodyAsync(HttpContext context, RequestTrackingOptions options)
    {
        var request = context.Request;
        if (!BodyCapturePolicy.IsCapturable(request.ContentType))
        {
            return BodyCapturePolicy.RequestNotCapturedMarker(request.ContentType);
        }

        var capacity = Math.Max(0, options.MaxCapturedBodyBytes);
        if (request.ContentLength is { } declared && declared > capacity)
        {
            return BodyCapturePolicy.RequestExceededMarker;
        }

        var rawBody = await ReadRequestBodyAsync(context, capacity);
        if (rawBody == null)
        {
            return BodyCapturePolicy.RequestExceededMarker;
        }

        // ★ 先脱敏再截断，与响应体同序。反过来会把 JSON 截成非法串，
        // 脱敏器于是原样返回 —— 只有超过 MaxRequestBodyLength 的请求泄漏凭据。
        return Truncate(RedactBody(rawBody) ?? string.Empty, options.MaxRequestBodyLength);
    }

    /// <summary>
    /// 把采集到的响应体变成日志字段：跳过 / 超界只留标记，其余先脱敏再截断。
    /// </summary>
    private static string DescribeCapturedResponse(BoundedResponseCaptureStream capture, RequestTrackingOptions options)
    {
        if (capture.Skipped)
        {
            return BodyCapturePolicy.ResponseNotCapturedMarker(capture.ContentType, capture.ContentEncoding);
        }

        if (capture.Exceeded)
        {
            return BodyCapturePolicy.ResponseExceededMarker;
        }

        var responseBodyText = Encoding.UTF8.GetString(capture.Captured.Span);
        // ★ 先截断再脱敏会把 JSON 截成非法串，脱敏器于是原样返回 —— 顺序不能反。
        return Truncate(RedactBody(responseBodyText) ?? string.Empty, options.MaxResponseBodyLength);
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
    /// 有界地读取请求体原文：最多读 <paramref name="capacity"/> + 1 字节，多出的那一个只为判断「超了」。
    /// </summary>
    /// <returns>整条体（未超界）；超界返回 <c>null</c>，已读的部分丢弃。</returns>
    /// <remarks>
    /// ★ <strong>这里是「读不读」的界，不是「记多少」的截断。</strong>脱敏按 JSON 解析，
    /// 而截断过的 JSON 是非法串、脱敏器只能原样返回，所以展示截断必须发生在脱敏之后；
    /// 超界的体则整条不记。读完把流位置退回 0，下游模型绑定照常拿到整条体。
    /// </remarks>
    private static Task<string?> ReadRequestBodyAsync(HttpContext context, int capacity)
        => context.Request.TryReadAsStringAsync(capacity, context.RequestAborted);

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