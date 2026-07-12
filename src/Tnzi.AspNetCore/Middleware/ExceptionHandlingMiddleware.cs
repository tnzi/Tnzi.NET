
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

    public async Task InvokeAsync(HttpContext context)
    {
        // 读取一次快照，保证 EnableBuffering 决策与 catch 中的读取一致
        var logRequestBody = _options.CurrentValue.LogRequestBody;

        // 仅在开启时缓冲请求体，使其可在异常发生后被重新读取（关闭时零开销）
        if (logRequestBody)
        {
            context.Request.EnableBuffering();
        }

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            // 记录异常统计
            _exceptionStats?.RecordException(ex, context.TraceIdentifier);

            // 诊断：记录触发异常的请求体（可能含敏感数据，默认关闭）
            if (logRequestBody)
            {
                await LogRequestBodyAsync(context, ex);
            }

            // 尝试自定义处理器
            var customResult = await TryCustomHandlersAsync(ex, context);
            if (customResult != null)
            {
                if (customResult.ShouldContinueHandling)
                    throw;

                await HandleExceptionAsync(context, ex, customResult);
                return;
            }

            // 使用处理器链处理异常
            await HandleExceptionAsync(context, ex, null);
        }
    }

    /// <summary>
    /// 记录触发异常的请求体（仅在 LogRequestBody 开启时调用）。请求体已由 EnableBuffering 缓冲，
    /// 读取后回退流位置；限制 8KB 上限。任何读取失败仅告警，绝不掩盖原始异常。
    /// </summary>
    private async Task LogRequestBodyAsync(HttpContext context, Exception exception)
    {
        const int maxBytes = 8 * 1024;
        try
        {
            var request = context.Request;
            if (request.ContentLength is null or 0 || !request.Body.CanSeek)
            {
                return;
            }

            request.Body.Position = 0;
            using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
            var buffer = new char[maxBytes];
            var read = await reader.ReadBlockAsync(buffer.AsMemory(0, maxBytes));
            request.Body.Position = 0;

            if (read > 0)
            {
                _logger.LogError(exception, "Unhandled exception processing {Method} {Path}. Request body ({Length} chars captured): {RequestBody}",
                    request.Method, request.Path, read, new string(buffer, 0, read));
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
        var options = _options.CurrentValue;
        var exceptionType = exception.GetType();

        // 查找自定义处理器
        if (options.CustomHandlers.TryGetValue(exceptionType, out var handlerType))
        {
            var handler = _serviceProvider.GetService(handlerType);
            if (handler != null)
            {
                // 使用编译委托替代每次反射调用（Lazy 确保并发时只编译一次）
                var invokeDelegate = _customHandlerDelegateCache.GetOrAdd(exceptionType, CreateLazyDelegate).Value;
                return await invokeDelegate(handler, exception, context, default);
            }
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
        // If the response has already started (e.g., streaming), we can't modify headers or status code
        if (context.Response.HasStarted)
        {
            _logger.LogWarning("Response has already started, unable to write error response for {ExceptionType}: {Message}",
                exception.GetType().Name, exception.Message);
            return;
        }

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
        var hasExtendedInfo = result.ContextData != null || result.IsRetryable.HasValue;

        object responseObject;

        if (hasExtendedInfo || (_environment.IsDevelopment() && result.ErrorDetail != null))
        {
            // 包含扩展信息（ContextData/IsRetryable），开发环境额外包含 Detail
            // 错误详情字段与标准信封统一为 errorDetails（前端只读 errorDetails，
            // 旧的扩展形状用 Errors→序列化为 errors，前端读不到导致详情丢失）。
            responseObject = new
            {
                Code = result.StatusCode ?? 500,
                Message = result.Message ?? "An error occurred while processing your request.",
                ErrorCode = result.ErrorCode,
                ErrorDetails = result.ErrorDetails,
                ContextData = result.ContextData,
                IsRetryable = result.IsRetryable,
                Detail = _environment.IsDevelopment() ? result.ErrorDetail : null,
                RequestId = options.IncludeRequestId ? context.TraceIdentifier : null,
                Success = false
            };
        }
        else
        {
            // 标准响应
            responseObject = ApiResult.Error(
                result.Message ?? "An error occurred while processing your request.",
                result.StatusCode ?? 500,
                result.ErrorCode,
                result.ErrorDetails);
        }

        return context.Response.WriteAsync(JsonSerializer.Serialize(responseObject, TnziJsonDefaults.Options));
    }
}