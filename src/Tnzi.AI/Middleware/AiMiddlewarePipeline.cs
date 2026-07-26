namespace Tnzi.AI.Middleware;

/// <summary>
/// AI 中间件管道构建器 - 洋葱模型，按 Order 排序执行
/// </summary>
public class AiMiddlewarePipeline
{
    private readonly List<IAiMiddleware> _middlewares = [];

    /// <summary>
    /// 添加中间件
    /// </summary>
    public AiMiddlewarePipeline Use(IAiMiddleware middleware)
    {
        Check.NotNull(middleware);
        _middlewares.Add(middleware);
        return this;
    }

    /// <summary>
    /// 添加 Lambda 中间件（非流式）
    /// </summary>
    public AiMiddlewarePipeline Use(int order, Func<AiMiddlewareContext, AiMiddlewareDelegate, CancellationToken, Task<AgentRunResult>> handler)
    {
        Check.NotNull(handler);
        _middlewares.Add(new LambdaMiddleware(order, handler, null));
        return this;
    }

    /// <summary>
    /// 添加 Lambda 中间件（同时支持流式和非流式）
    /// </summary>
    public AiMiddlewarePipeline Use(
        int order,
        Func<AiMiddlewareContext, AiMiddlewareDelegate, CancellationToken, Task<AgentRunResult>> handler,
        Func<AiMiddlewareContext, AiStreamingMiddlewareDelegate, CancellationToken, IAsyncEnumerable<AgentStreamChunk>> streamingHandler)
    {
        Check.NotNull(handler);
        Check.NotNull(streamingHandler);
        _middlewares.Add(new LambdaMiddleware(order, handler, streamingHandler));
        return this;
    }

    /// <summary>
    /// 获取已注册的中间件数量
    /// </summary>
    public int Count => _middlewares.Count;

    /// <summary>
    /// 构建非流式管道委托
    /// </summary>
    public AiMiddlewareDelegate Build(AiMiddlewareDelegate coreExecutor)
    {
        Check.NotNull(coreExecutor);

        var ordered = GetSortedMiddlewares();

        // 从内到外包装：最后一个中间件包裹 coreExecutor，第一个中间件最外层
        var next = coreExecutor;
        for (var i = ordered.Count - 1; i >= 0; i--)
        {
            var middleware = ordered[i];
            var currentNext = next;
            next = (context, ct) => middleware.InvokeAsync(context, currentNext, ct);
        }

        return next;
    }

    /// <summary>
    /// 构建流式管道委托
    /// </summary>
    public AiStreamingMiddlewareDelegate BuildStreaming(AiStreamingMiddlewareDelegate coreExecutor)
    {
        Check.NotNull(coreExecutor);

        var ordered = GetSortedMiddlewares();

        var next = coreExecutor;
        for (var i = ordered.Count - 1; i >= 0; i--)
        {
            var middleware = ordered[i];
            var currentNext = next;
            next = (context, ct) => middleware.InvokeStreamingAsync(context, currentNext, ct);
        }

        return next;
    }

    private List<IAiMiddleware>? _sortedCache;

    private List<IAiMiddleware> GetSortedMiddlewares() =>
        _sortedCache ??= _middlewares.OrderBy(m => m.Order).ToList();

    /// <summary>
    /// Lambda 中间件内部实现
    /// </summary>
    private sealed class LambdaMiddleware : IAiMiddleware
    {
        private readonly Func<AiMiddlewareContext, AiMiddlewareDelegate, CancellationToken, Task<AgentRunResult>> _handler;
        private readonly Func<AiMiddlewareContext, AiStreamingMiddlewareDelegate, CancellationToken, IAsyncEnumerable<AgentStreamChunk>>? _streamingHandler;

        public int Order { get; }

        public LambdaMiddleware(
            int order,
            Func<AiMiddlewareContext, AiMiddlewareDelegate, CancellationToken, Task<AgentRunResult>> handler,
            Func<AiMiddlewareContext, AiStreamingMiddlewareDelegate, CancellationToken, IAsyncEnumerable<AgentStreamChunk>>? streamingHandler)
        {
            Order = order;
            _handler = handler;
            _streamingHandler = streamingHandler;
        }

        public Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
        {
            return _handler(context, next, cancellationToken);
        }

        public IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(
            AiMiddlewareContext context,
            AiStreamingMiddlewareDelegate next,
            CancellationToken cancellationToken = default)
        {
            if (_streamingHandler == null)
            {
                throw new InvalidOperationException(
                    $"Lambda middleware with order {Order} does not support streaming. " +
                    "Use the overload that provides a streaming handler.");
            }

            return _streamingHandler(context, next, cancellationToken);
        }
    }
}
