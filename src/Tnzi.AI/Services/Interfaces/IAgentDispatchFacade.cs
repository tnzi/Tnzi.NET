namespace Tnzi.AI.Services;

/// <summary>
/// 执行路由门面。<b>整个框架里唯一允许判断「这个 Agent 走内建还是外部」的地方。</b>
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RunAsync"/> / <see cref="RunStreamingAsync"/> 的签名刻意与 <see cref="IAgentRuntime"/> <b>逐字相同</b>，
/// 因此它是调用方的 drop-in 替换：<c>ChatService</c> / <c>AgentService</c> 只把注入的类型换掉，一行执行逻辑都不用改。
/// 这不是巧合而是设计目的 —— 只要调用方能感知到「底下走了哪条路」，分支就会开始向外扩散，
/// 而上一版 <c>Tnzi.AI.Cli</c> 正是这样长出 28 处 <c>ShouldSkipMiddleware</c> 的。
/// </para>
/// <para>
/// 另外几个成员操作<b>一次进行中的运行</b>（查、重新接上、取消）。它们也在这里，理由相同：一个运行 id 属于哪个执行域，
/// 只有门面知道。今天只有外部运行能被重新接上（内建运行绑在发起它的那个请求上，请求走了它也就停了），
/// 所以对内建运行它们回答「没有」，调用方照样不用知道底下是哪条路。
/// </para>
/// <para>
/// 外部路径的返回值由门面把归一化的 <see cref="CliAgentEvent"/> 流翻译成
/// <see cref="AgentStreamChunk"/> / <see cref="AgentRunResult"/>；未加载子模块时
/// <c>ICliAgentBindingService</c> 的回退恒返回 null，于是这里恒走内建路径。
/// </para>
/// <para>
/// 约定测试 <c>CliAgentRedLineTests</c> 守住这条红线：全仓只有本接口的实现里
/// 可以同时引用 <see cref="IAgentRuntime"/> 与 <see cref="ICliAgentDispatcher"/>。
/// </para>
/// </remarks>
public interface IAgentDispatchFacade
{
    /// <summary>执行一次 AI 运行（非流式）。按绑定路由到内建管线或外部 agent。</summary>
    Task<AgentRunResult> RunAsync(
        AgentRunRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>执行一次 AI 运行（流式）。按绑定路由到内建管线或外部 agent。</summary>
    /// <remarks>
    /// 外部路径的第一个块是 <see cref="RunStreamEventTypes.RunStarted"/>，带运行 id 与存下的用户消息 id；
    /// 之后每个块带它来自的事件序号（<see cref="AgentStreamChunk.Sequence"/>），工具调用以 <see cref="AgentStreamChunk.ToolCalls"/>
    /// 逐条给出（发起与结果各一次，按 id 合并）；终止块带运行写进线程的回复消息 id。
    /// </remarks>
    IAsyncEnumerable<AgentStreamChunk> RunStreamingAsync(
        AgentRunRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 一次运行的概况；调用者看不见（或它不是可重新接上的运行）时为 null。
    /// </summary>
    Task<ChatRunDto?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default)
        => Task.FromResult<ChatRunDto?>(null);

    /// <summary>
    /// 线程上还没结束的运行（最早的在前）。调用方负责先核线程归属。
    /// </summary>
    Task<IReadOnlyList<ChatRunDto>> GetActiveRunsAsync(Guid threadId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ChatRunDto>>([]);

    /// <summary>
    /// 重新接上一次运行的流：从头（<paramref name="fromSequence"/> = 0）或从某个事件序号之后，块的形状与
    /// <see cref="RunStreamingAsync"/> 相同；运行已经结束时补完剩下的事件后给出终止块。
    /// </summary>
    IAsyncEnumerable<AgentStreamChunk> AttachStreamingAsync(
        Guid runId,
        int fromSequence = 0,
        CancellationToken cancellationToken = default)
        => NotAttachable();

    /// <summary>在服务端取消一次运行（不只是断开连接）。</summary>
    Task<Result> CancelRunAsync(Guid runId, CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Failure("Run not found.", 404, ErrorCodes.ChatRunNotFound));

    /// <summary>
    /// 回答一次运行的一个权限请求（聊天流上的 <c>permission_request</c> 事件）。规则同 <see cref="ICliAgentDispatcher.AnswerPermissionAsync"/>。
    /// </summary>
    Task<Result> AnswerRunPermissionAsync(
        Guid runId, Guid requestId, CliPermissionAnswerDto answer, CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Failure("Run not found.", 404, ErrorCodes.ChatRunNotFound));

    private static async IAsyncEnumerable<AgentStreamChunk> NotAttachable()
    {
        await Task.CompletedTask;
        yield return new AgentStreamChunk { Error = "Run not found.", FinishReason = FinishReasons.Error };
    }
}
