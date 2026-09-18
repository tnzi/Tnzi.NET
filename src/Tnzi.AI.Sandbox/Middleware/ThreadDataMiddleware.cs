namespace Tnzi.AI.Sandbox.Middleware;

/// <summary>
/// 线程数据中间件 - 按本轮 <c>ThreadId</c> 算出线程目录布局（<see cref="ThreadDataState"/>）并放进上下文。
/// </summary>
/// <remarks>
/// <para>
/// 只算路径，<b>不碰磁盘</b>。目录与技能副本由 <see cref="IThreadDataProvisioner"/> 在沙箱第一次被用到时布置
/// （见 <see cref="SandboxMiddleware"/>）。此前这里在每一次带线程的运行入口就建目录、复制约 2 MB 技能文件，
/// 而多数运行的 Agent 根本没有沙箱工具，某消费方一天里 863 次外呼 bot 运行留下了 92,000 个文件。
/// </para>
/// <para>
/// 下游读这份布局的除了 <see cref="SandboxMiddleware"/>，还有核心的 <c>FileUploadMiddleware</c>（60），
/// 它按 <see cref="ThreadDataState.UploadsPath"/> 检查已上传的文件是否在磁盘上；只读不建目录，所以路径够用。
/// </para>
/// </remarks>
public class ThreadDataMiddleware : IAiMiddleware
{
    private readonly IVirtualPathTranslator _translator;
    private readonly ILogger<ThreadDataMiddleware> _logger;

    public int Order => AiMiddlewareOrders.ThreadData;

    public ThreadDataMiddleware(IVirtualPathTranslator translator, ILogger<ThreadDataMiddleware> logger)
    {
        _translator = Check.NotNull(translator);
        _logger = Check.NotNull(logger);
    }

    public async Task<AgentRunResult> InvokeAsync(
        AiMiddlewareContext context, AiMiddlewareDelegate next, CancellationToken cancellationToken = default)
    {
        PublishThreadData(context);
        return await next(context, cancellationToken);
    }

    public async IAsyncEnumerable<AgentStreamChunk> InvokeStreamingAsync(
        AiMiddlewareContext context, AiStreamingMiddlewareDelegate next,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        PublishThreadData(context);
        await foreach (var chunk in next(context, cancellationToken))
            yield return chunk;
    }

    /// <summary>
    /// 没有线程就没有目录：正常管线里线程在本中间件之前已由 <c>ThreadResolutionMiddleware</c>（40）解析，
    /// 这里的跳过只是自定义管线没装它时的兜底（以及 Ephemeral 运行刻意不带线程）。
    /// </summary>
    private void PublishThreadData(AiMiddlewareContext context)
    {
        var threadId = context.Request.ThreadId;
        if (threadId is null) return;

        var state = ThreadDataState.FromThreadDirectory(_translator.GetThreadDirectory(threadId.Value));
        context.Properties[SandboxPropertyKeys.ThreadData] = state;
        _logger.LogDebug("Thread data layout published for {ThreadId} at {ThreadDirectory}", threadId, state.ThreadDirectory);
    }
}
