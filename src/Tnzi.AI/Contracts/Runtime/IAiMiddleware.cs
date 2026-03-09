namespace Tnzi.AI.Contracts.Runtime;

/// <summary>
/// AI 中间件委托（非流式）
/// </summary>
public delegate Task<AgentRunResult> AiMiddlewareDelegate(
    AiMiddlewareContext context,
    CancellationToken cancellationToken);

/// <summary>
/// AI 流式中间件委托
/// </summary>
public delegate IAsyncEnumerable<AgentStreamChunk> AiStreamingMiddlewareDelegate(
    AiMiddlewareContext context,
    CancellationToken cancellationToken);

/// <summary>
/// AI 执行中间件接口。
/// 对标 Spring AI 的 Advisor 模式和 ASP.NET Core Middleware 管道。
/// </summary>
public interface IAiMiddleware
{
    /// <summary>中间件优先级，值越小越先执行</summary>
    int Order { get; }

    /// <summary>执行中间件逻辑（非流式）</summary>
    Task<AgentRunResult> InvokeAsync(
        AiMiddlewareContext context,
        AiMiddlewareDelegate next,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行中间件逻辑（流式）。
    /// 默认实现直接委托给 next，中间件只需在有流式特殊逻辑时重写。
    /// </summary>
    IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(
        AiMiddlewareContext context,
        AiStreamingMiddlewareDelegate next,
        CancellationToken cancellationToken = default)
    {
        // 默认实现：不干预流式路径，直接传递
        return next(context, cancellationToken);
    }
}
