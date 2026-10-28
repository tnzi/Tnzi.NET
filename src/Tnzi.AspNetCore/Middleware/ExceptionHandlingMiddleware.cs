
namespace Tnzi.AspNetCore.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IWebHostEnvironment _environment;
    private readonly IOptionsMonitor<ExceptionHandlingOptions> _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly IExceptionStatistics? _exceptionStats;
    private readonly List<ExceptionHandlerBase> _handlers;

    /// <summary>
    /// 自定义异常处理器编译委托缓存（每个异常类型只编译一次）
    /// 使用 Lazy 包装确保并发场景下工厂方法只执行一次，避免重复编译表达式树
    /// </summary>
    private static readonly ConcurrentDictionary<Type, Lazy<Func<object, Exception, HttpContext, CancellationToken, Task<ExceptionHandlingResult?>>>> _customHandlerDelegateCache = new();

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IWebHostEnvironment environment,
        IOptionsMonitor<ExceptionHandlingOptions> options,
        IServiceProvider serviceProvider,
        IExceptionStatistics? exceptionStats = null)
    {
        _next = Check.NotNull(next);
        _logger = Check.NotNull(logger);
        _environment = Check.NotNull(environment);
        _options = Check.NotNull(options);
        _serviceProvider = Check.NotNull(serviceProvider);
        _exceptionStats = exceptionStats;

        // 初始化异常处理器链（按优先级降序预排序，避免每次请求重新排序）
        // 优先从DI容器获取，如果不存在则创建新实例
        _handlers =
        [
            serviceProvider.GetService<Handlers.ValidationExceptionHandler>() ??
                new Handlers.ValidationExceptionHandler(environment, options, serviceProvider.GetRequiredService<ILogger<Handlers.ValidationExceptionHandler>>()),
            serviceProvider.GetService<Handlers.BusinessExceptionHandler>() ??
                new Handlers.BusinessExceptionHandler(environment, options, serviceProvider.GetRequiredService<ILogger<Handlers.BusinessExceptionHandler>>()),
            serviceProvider.GetService<Handlers.InfrastructureExceptionHandler>() ??
                new Handlers.InfrastructureExceptionHandler(environment, options, serviceProvider.GetRequiredService<ILogger<Handlers.InfrastructureExceptionHandler>>()),
            serviceProvider.GetService<Handlers.DefaultExceptionHandler>() ??
                new Handlers.DefaultExceptionHandler(environment, options, serviceProvider.GetRequiredService<ILogger<Handlers.DefaultExceptionHandler>>())
        ];
        _handlers.Sort((a, b) => b.Priority.CompareTo(a.Priority));
    }

    /// <summary>
    /// 异常诊断最多留存的请求体字节数，同时也是「要不要缓冲」的闸门。
    /// </summary>
    /// <remarks>
    /// 这里的体是在异常<b>之后</b>回读的，所以缓冲只能在下游读它之前就开好；而 <c>EnableBuffering</c>
    /// 会把整条体复制一份、超过 30 KB 溢出到临时文件。开着 <c>LogRequestBody</c> 时此前对每个请求
    /// 无条件开缓冲：只为在出错时看 8 KB，每一次 multipart 上传都被完整落盘一遍。
    /// 所以闸门在缓冲之前：Content-Type 不可采（文件上传、二进制、事件流）或声明的 Content-Length
    /// 超过这个数的体，缓冲都不开 —— 它们本来就不会被记进日志。没有 Content-Length 的体也不缓冲：
    /// 读发生在下游消费完之后，那时已经没有办法再给缓冲设上界了。
    /// </remarks>
    private const int MaxCapturedBodyBytes = 8 * 1024;

    public async Task InvokeAsync(HttpContext context)
    {
        // 读取一次快照，保证缓冲决策与 catch 中的读取一致
        var captureRequestBody = _options.CurrentValue.LogRequestBody
                                 && LoggedBodySanitizer.AllowsBodyCapture(context.Request.Path)
                                 && ShouldBufferRequestBody(context.Request);

        // 过了闸门才缓冲请求体，使其可在异常发生后被重新读取（关闭或不可采时零开销）
        if (captureRequestBody)
        {
            context.Request.EnableBuffering();
        }

        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // 客户端断开（关页面、导航走）引起的取消不是服务器错误：不记 Error、不进异常统计、不往已断开的连接写 500。
            // 只认 RequestAborted 已触发的取消；内部超时等其它取消仍走下面的 500 路径。
            _logger.LogDebug("Request {Method} {Path} was aborted by the client", context.Request.Method, context.GetRedactedPath());
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
            }
        }
        catch (Exception ex)
        {
            // 记录异常统计。请求 ID 用追踪中间件写回给客户端的那一个（X-Request-Id），
            // 而不是客户端从来看不到的连接级 TraceIdentifier，否则支持人员拿着客户端的 id 对不上任何记录。
            _exceptionStats?.RecordException(ex, ResolveRequestId(context));

            // 诊断：记录触发异常的请求体（可能含敏感数据，默认关闭）
            if (captureRequestBody)
            {
                await LogRequestBodyAsync(context, ex);
            }

            if (context.Response.HasStarted)
            {
                // 响应头已发出（流式端点写过一段之后抛异常）：已经写不出错误信封。
                // 正常返回会让服务器写出结束块，客户端看到的是一个 200、形式完整、内容被截断的响应体；
                // 原样抛出则服务器中止这条响应（并带堆栈记录这次未处理异常）。客户端已经走了就没有可中止的了。
                if (context.RequestAborted.IsCancellationRequested)
                {
                    _logger.LogDebug(ex, "Request {Method} {Path} failed after the response started and the client has disconnected",
                        context.Request.Method, context.GetRedactedPath());
                    return;
                }

                throw;
            }

            // 尝试自定义处理器；返回 null 或 ShouldContinueHandling 都交给内置处理器链
            var customResult = await TryCustomHandlersAsync(ex, context);
            if (customResult is { ShouldContinueHandling: false })
            {
                await HandleExceptionAsync(context, ex, customResult);
                return;
            }

            // 使用处理器链处理异常
            await HandleExceptionAsync(context, ex, null);
        }
    }

    /// <summary>
    /// 错误路径上的请求 ID：优先取 <see cref="RequestTrackingMiddleware"/> 写进 Items 并回显在 X-Request-Id 里的那一个，
    /// 没有追踪中间件时才退回连接级的 <see cref="HttpContext.TraceIdentifier"/>。
    /// </summary>
    private static string ResolveRequestId(HttpContext context) => context.GetRequestId() ?? context.TraceIdentifier;

    /// <summary>
    /// 这个请求的体值不值得为异常诊断缓冲一份：文本型 Content-Type，且声明了不超过
    /// <see cref="MaxCapturedBodyBytes"/> 的 Content-Length。
    /// </summary>
    private static bool ShouldBufferRequestBody(HttpRequest request)
    {
        if (request.ContentLength is not { } declared || declared <= 0)
        {
            return false;
        }

        return declared <= MaxCapturedBodyBytes && BodyCapturePolicy.IsCapturable(request.ContentType);
    }

    /// <summary>
    /// 记录触发异常的请求体（仅在 LogRequestBody 开启且过了 <see cref="ShouldBufferRequestBody"/> 时调用）。
    /// 请求体已由 EnableBuffering 缓冲，有界回读后流位置放回 0。任何读取失败仅告警，绝不掩盖原始异常。
    /// </summary>
    private async Task LogRequestBodyAsync(HttpContext context, Exception exception)
    {
        try
        {
            var request = context.Request;
            if (!request.Body.CanSeek)
            {
                return;
            }

            // 闸门按声明的 Content-Length 放行，这里按实际字节数再兜一次：超界返回 null，一个字节都不记。
            var rawBody = await request.TryReadAsStringAsync(MaxCapturedBodyBytes, context.RequestAborted);

            // 与请求日志同一道脱敏：这个开关可以热开，而异常最常见的现场恰恰是登录、改密这类带凭据的请求。
            // 回读是完整的（超界已返回 null），所以 JSON 不会因截断变成非法串而被原样放过。
            var body = LoggedBodySanitizer.Redact(rawBody, request.ContentType);
            if (!string.IsNullOrEmpty(body))
            {
                // 路径与请求日志同一道脱敏：signing/{token}、files/share/{token} 这类路由把凭据放在路径里。
                _logger.LogError(exception, "Unhandled exception processing {Method} {Path}. Request body ({Length} chars captured): {RequestBody}",
                    request.Method, context.GetRedactedPath(), body.Length, body);
            }
        }
        catch (Exception readEx)
        {
            // 记录请求体是尽力而为的诊断，绝不允许它掩盖或替代原始异常
            _logger.LogWarning(readEx, "Failed to capture request body for exception diagnostics");
        }
    }

    private async Task<ExceptionHandlingResult?> TryCustomHandlersAsync(
        Exception exception, HttpContext context)
    {
        var customHandlers = _options.CurrentValue.CustomHandlers;
        if (customHandlers.Count == 0)
        {
            return null;
        }

        // 沿继承链向上找：登记在基类上的处理器也处理它的子类，离抛出类型最近的登记优先。
        for (var registeredType = exception.GetType(); registeredType != null; registeredType = registeredType.BaseType)
        {
            if (!customHandlers.TryGetValue(registeredType, out var handlerType))
            {
                continue;
            }

            // 从请求作用域解析：中间件是进程级单例，构造时拿到的是根容器。自定义处理器注册成 scoped 时，
            // 从根容器解析在 Development 抛异常（而且是在 catch 块里，原异常被它替换），
            // 在 Production 则被捕获成全进程共享的单例。没有请求作用域（直接构造中间件）时才退回根容器。
            var handler = (context.RequestServices ?? _serviceProvider).GetService(handlerType);
            if (handler == null)
            {
                return null;
            }

            // 委托按登记的类型编译：处理器实现的是 IExceptionHandler<登记类型>，不是 IExceptionHandler<抛出类型>
            // （使用编译委托替代每次反射调用，Lazy 确保并发时只编译一次）
            var invokeDelegate = _customHandlerDelegateCache.GetOrAdd(registeredType, CreateLazyDelegate).Value;
            return await invokeDelegate(handler, exception, context, default);
        }

        return null;
    }

    /// <summary>
    /// 创建 Lazy 包装的委托，确保并发场景下只编译一次表达式树
    /// </summary>
    private static Lazy<Func<object, Exception, HttpContext, CancellationToken, Task<ExceptionHandlingResult?>>> CreateLazyDelegate(Type exceptionType)
    {
        return new Lazy<Func<object, Exception, HttpContext, CancellationToken, Task<ExceptionHandlingResult?>>>(
            () => BuildCustomHandlerDelegate(exceptionType));
    }

    /// <summary>
    /// 编译自定义异常处理器调用委托
    /// (object handler, Exception exception, HttpContext context, CancellationToken ct) => Task&lt;ExceptionHandlingResult?&gt;
    /// </summary>
    private static Func<object, Exception, HttpContext, CancellationToken, Task<ExceptionHandlingResult?>> BuildCustomHandlerDelegate(Type exceptionType)
    {
        var genericInterfaceType = typeof(IExceptionHandler<>).MakeGenericType(exceptionType);
        var handleMethod = genericInterfaceType.GetMethod(nameof(IExceptionHandler<Exception>.HandleAsync))!;

        var handlerParam = Expression.Parameter(typeof(object), "handler");
        var exceptionParam = Expression.Parameter(typeof(Exception), "exception");
        var contextParam = Expression.Parameter(typeof(HttpContext), "context");
        var ctParam = Expression.Parameter(typeof(CancellationToken), "ct");

        var call = Expression.Call(
            Expression.Convert(handlerParam, genericInterfaceType),
            handleMethod,
            Expression.Convert(exceptionParam, exceptionType),
            contextParam,
            ctParam);

        return Expression.Lambda<Func<object, Exception, HttpContext, CancellationToken, Task<ExceptionHandlingResult?>>>(
            call, handlerParam, exceptionParam, contextParam, ctParam).Compile();
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception, ExceptionHandlingResult? customResult = null)
    {
        // 响应已开始的情形在 InvokeAsync 里处理（原样抛出），到这里响应头一定还没发出
        context.Response.ContentType = "application/json";

        ExceptionHandlingResult result;

        // 使用自定义结果（如果存在）
        if (customResult != null)
        {
            result = customResult;
        }
        else
        {
            // 使用处理器链处理异常（已按优先级降序预排序，选择第一个匹配的处理器）
            var handler = _handlers.Find(h => h.CanHandle(exception));
            if (handler != null)
            {
                result = await handler.HandleAsync(context, exception);
            }
            else
            {
                // 如果没有找到处理器，使用默认处理器
                var defaultHandler = _handlers.OfType<DefaultExceptionHandler>().First();
                result = await defaultHandler.HandleAsync(context, exception);
            }
        }

        // 设置HTTP状态码
        context.Response.StatusCode = result.StatusCode ?? 500;

        // 构建响应
        await WriteResponseAsync(context, result);
    }

    private Task WriteResponseAsync(HttpContext context, ExceptionHandlingResult result)
    {
        var options = _options.CurrentValue;
        // IncludeRetryInfo 关闭时不输出 isRetryable，也不因它切到扩展形状（与 IncludeContextData 同一做法）
        var isRetryable = options.IncludeRetryInfo ? result.IsRetryable : null;
        var hasExtendedInfo = result.ContextData != null || isRetryable.HasValue;
        var requestId = options.IncludeRequestId ? ResolveRequestId(context) : null;

        string json;

        if (hasExtendedInfo || (_environment.IsDevelopment() && result.ErrorDetail != null))
        {
            // 包含扩展信息（ContextData/IsRetryable），开发环境额外包含 Detail
            // 错误详情字段与标准信封统一为 errorDetails（前端只读 errorDetails，
            // 旧的扩展形状用 Errors→序列化为 errors，前端读不到导致详情丢失）。
            json = JsonSerializer.Serialize(new
            {
                Code = result.StatusCode ?? 500,
                Message = result.Message ?? "An error occurred while processing your request.",
                ErrorCode = result.ErrorCode,
                ErrorDetails = result.ErrorDetails,
                ContextData = result.ContextData,
                IsRetryable = isRetryable,
                Detail = _environment.IsDevelopment() ? result.ErrorDetail : null,
                RequestId = requestId,
                Success = false
            }, TnziJsonDefaults.Options);
        }
        else
        {
            // 标准响应；IncludeRequestId 开着时追加 requestId（只加字段，信封其余部分不变）
            var envelope = ApiResult.Error(
                result.Message ?? "An error occurred while processing your request.",
                result.StatusCode ?? 500,
                result.ErrorCode,
                result.ErrorDetails);
            var node = JsonSerializer.SerializeToNode(envelope, TnziJsonDefaults.Options)!.AsObject();
            if (requestId != null)
            {
                node["requestId"] = requestId;
            }
            json = node.ToJsonString(TnziJsonDefaults.Options);
        }

        return context.Response.WriteAsync(json);
    }
}