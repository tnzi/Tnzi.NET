namespace Tnzi.AI.Middleware;

/// <summary>
/// 新线程的第一条用户消息一被接受就起标题：写回退标题，开了自动标题时在后台请求 AI 标题，然后照常往下走。
/// </summary>
/// <remarks>
/// <para>
/// 排在输入守卫（200）与用量上限（250）之后：被守卫拒绝或被上限拦下的消息不算被接受；
/// 标题取的是守卫改写之后、模型实际收到的文本（<see cref="UserTurn.EffectiveText"/>），被脱敏的内容不会以线程标题的形式留下来。
/// 排在历史（300）之前，所以回复还没开始，侧栏里的线程就已经有名字了。
/// </para>
/// <para>
/// 只给顶层对话起标题：子 Agent 的运行（<see cref="AgentRunRequest.ParentRunId"/>）与临时运行不起。
/// </para>
/// </remarks>
public class ThreadTitleMiddleware : IAiMiddleware
{
    private readonly IThreadTitleService _titles;

    public int Order => AiMiddlewareOrders.Title;

    public ThreadTitleMiddleware(IThreadTitleService titles)
    {
        _titles = Check.NotNull(titles);
    }

    public async Task<AgentRunResult> InvokeAsync(AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
    {
        await StartAsync(context, cancellationToken);
        return await next(context, cancellationToken);
    }

    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(
        AiMiddlewareContext context, AiStreamingMiddlewareDelegate next,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await StartAsync(context, cancellationToken);
        await foreach (var chunk in next(context, cancellationToken))
        {
            yield return chunk;
        }
    }

    private Task StartAsync(AiMiddlewareContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        if (!context.IsNewThread || request.ThreadId is not { } threadId || request.Ephemeral || request.ParentRunId.HasValue)
        {
            return Task.CompletedTask;
        }

        var text = UserTurn.EffectiveText(context);
        return string.IsNullOrWhiteSpace(text)
            ? Task.CompletedTask
            : _titles.StartAsync(new ThreadTitleRequest(threadId, text, request.UserId, request.AgentId), cancellationToken);
    }
}
