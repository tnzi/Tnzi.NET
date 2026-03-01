
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

        // 初始化异常处理器链（按优先级顺序）
        // 优先从DI容器获取，如果不存在则创建新实例
        _handlers = new List<ExceptionHandlerBase>
        {
            serviceProvider.GetService<Handlers.ValidationExceptionHandler>() ??
                new Handlers.ValidationExceptionHandler(environment, options, serviceProvider.GetRequiredService<ILogger<Handlers.ValidationExceptionHandler>>()),
            serviceProvider.GetService<Handlers.BusinessExceptionHandler>() ??
                new Handlers.BusinessExceptionHandler(environment, options, serviceProvider.GetRequiredService<ILogger<Handlers.BusinessExceptionHandler>>()),
            serviceProvider.GetService<Handlers.InfrastructureExceptionHandler>() ??
                new Handlers.InfrastructureExceptionHandler(environment, options, serviceProvider.GetRequiredService<ILogger<Handlers.InfrastructureExceptionHandler>>()),
            serviceProvider.GetService<Handlers.DefaultExceptionHandler>() ??
                new Handlers.DefaultExceptionHandler(environment, options, serviceProvider.GetRequiredService<ILogger<Handlers.DefaultExceptionHandler>>())
        };
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            // 记录异常统计
            _exceptionStats?.RecordException(ex, context.TraceIdentifier);

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
            // 使用处理器链处理异常（按优先级降序选择第一个匹配的处理器）
            var handler = _handlers
                .Where(h => h.CanHandle(exception))
                .OrderByDescending(h => h.Priority)
                .FirstOrDefault();
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
        var isDevelopment = _environment.IsDevelopment();

        // 构建响应对象
        object responseObject;

        if (isDevelopment && (result.ErrorDetail != null || result.ContextData != null || result.IsRetryable.HasValue))
        {
            // 开发环境：包含详细信息
            responseObject = new
            {
                Code = result.StatusCode ?? 500,
                Message = result.Message ?? "An error occurred while processing your request.",
                ErrorCode = result.ErrorCode,
                Errors = result.ErrorDetails,
                ContextData = result.ContextData,
                IsRetryable = result.IsRetryable,
                Detail = result.ErrorDetail,
                RequestId = options.IncludeRequestId ? context.TraceIdentifier : null,
                Success = false
            };
        }
        else if (result.ContextData != null || result.IsRetryable.HasValue)
        {
            // 生产环境：包含上下文数据和重试信息
            responseObject = new
            {
                Code = result.StatusCode ?? 500,
                Message = result.Message ?? "An error occurred while processing your request.",
                ErrorCode = result.ErrorCode,
                Errors = result.ErrorDetails,
                ContextData = result.ContextData,
                IsRetryable = result.IsRetryable,
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