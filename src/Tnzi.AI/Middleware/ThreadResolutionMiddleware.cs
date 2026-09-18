namespace Tnzi.AI.Middleware;

/// <summary>
/// 线程解析中间件 - 在任何环境中间件之前把本轮的 <c>ThreadId</c> 定下来（为 null 时新建，非 null 时校验归属）。
/// </summary>
/// <remarks>
/// <para>
/// ★ 此前线程只在 <see cref="HistoryMiddleware"/>（Order 300）里创建并回写 <c>Request.ThreadId</c>，
/// 而 ThreadData（50）/ Sandbox（55）在它之前运行、遇到 <c>ThreadId == null</c> 直接跳过：
/// 每个新会话的<b>首轮没有沙箱</b>，五个沙箱工具一律答 "Sandbox is not available"，第二轮客户端回传 threadId
/// 后一切正常 —— 看起来像抖动，其实是接线顺序。出厂客户端首条消息就是 <c>threadId: null</c>。
/// </para>
/// <para>
/// 解析结果经 <see cref="AiMiddlewareContext.Properties"/>[<see cref="ResolvedPropertyKey"/>] 标记，
/// <see cref="HistoryMiddleware"/> 见到标记就不再重复解析（否则新线程会被建两次）；没装本中间件的自定义管线里
/// History 仍自己解析，行为与以前一致。归属校验的唯一所在地仍是
/// <see cref="IAgentThreadInternalService.GetOrCreateThreadAsync"/>：它的拒绝必须原样传播，前移不能变成放行。
/// </para>
/// </remarks>
public class ThreadResolutionMiddleware : IAiMiddleware
{
    /// <summary><see cref="AiMiddlewareContext.Properties"/> 里「线程已由本中间件解析并校验」的标记键。</summary>
    public const string ResolvedPropertyKey = "ThreadResolution.Resolved";

    private readonly IAgentThreadInternalService _threadService;
    private readonly ILogger<ThreadResolutionMiddleware> _logger;

    public int Order => AiMiddlewareOrders.ThreadResolution;

    public ThreadResolutionMiddleware(IAgentThreadInternalService threadService, ILogger<ThreadResolutionMiddleware> logger)
    {
        _threadService = Check.NotNull(threadService);
        _logger = Check.NotNull(logger);
    }

    public async Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
    {
        await ResolveAsync(context, cancellationToken);
        return await next(context, cancellationToken);
    }

    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(
        AiMiddlewareContext context, AiStreamingMiddlewareDelegate next,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await ResolveAsync(context, cancellationToken);
        await foreach (var chunk in next(context, cancellationToken))
        {
            yield return chunk;
        }
    }

    /// <summary>
    /// 解析并回写线程；已解析过（同一上下文重入）或临时运行（<see cref="AgentRunRequest.Ephemeral"/>）则跳过。
    /// 与 <see cref="HistoryMiddleware"/> 共用同一份语义：<see cref="BusinessException"/>（归属不符 / 不存在）原样向上传播，
    /// 其它失败只记日志、把线程留给下游（History 会再试一次）。
    /// </summary>
    /// <remarks>
    /// ★ Ephemeral 守卫必须在这里也有一份：History 在 300 跳过建线程，而本中间件在 40 先跑，
    /// 少了它评估用例照样每条留下一条空 AgentThread，ThreadData / Sandbox 还会为每个一次性用例各开一份目录与沙箱。
    /// </remarks>
    public static async Task ResolveAsync(
        IAgentThreadInternalService threadService, AiMiddlewareContext context, ILogger logger, CancellationToken ct)
    {
        if (context.Request.Ephemeral || context.Properties.ContainsKey(ResolvedPropertyKey))
        {
            return;
        }

        try
        {
            var (_, resolvedThreadId, isNewThread) = await threadService.GetOrCreateThreadAsync(
                context.Request.ThreadId, context.Request.AgentId, ct);
            context.Request.ThreadId = resolvedThreadId;
            context.IsNewThread = isNewThread;
            context.Properties[ResolvedPropertyKey] = true;
            logger.LogDebug("Resolved thread {ThreadId} for agent {AgentId} (new: {IsNew})", resolvedThreadId, context.Request.AgentId, isNewThread);
        }
        catch (BusinessException)
        {
            // 归属不符 / 线程不存在 / Agent 不存在都走这里，必须向上传播 ——
            // 吞掉它就等于放行一个未经校验的 threadId 继续跑完整条管线。
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to resolve thread for agent {AgentId}", context.Request.AgentId);
        }
    }

    private Task ResolveAsync(AiMiddlewareContext context, CancellationToken ct)
        => ResolveAsync(_threadService, context, _logger, ct);
}
