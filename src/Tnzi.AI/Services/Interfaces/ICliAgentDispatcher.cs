namespace Tnzi.AI.Services;

/// <summary>
/// 外部 agent 调度入口。与 <see cref="IAgentRuntime"/> <b>平级</b>，不是它的一个模式。
/// </summary>
/// <remarks>
/// <para>
/// 内建执行里，框架掌控工具循环、上下文注入、guardrail、RAG；外部执行里 CLI 掌控这一切，
/// 框架只负责布置环境、投递提示、回收结果。这是两种执行模型 —— 把它们塞进同一条中间件管线，
/// 结果必然是散落各处的「跳过这个中间件」开关（本框架已经为此付过一次代价，
/// 见 <c>archive/pre-ai-client-removal</c>：15 个中间件里 28 处 skip 补丁）。
/// </para>
/// <para>
/// 可复用的是**结果侧**能力：<c>IBudgetService</c> / <c>ICostCalculator</c> / <c>UsageLog</c> / 审计事件。
/// 请求侧的 RAG 注入、工具装配、guardrail 对外部 agent 本就不适用，不该靠 skip 绕过。
/// </para>
/// <para>未加载 <c>Tnzi.AI.Cli</c> 时由 NoOp 实现返回 501。</para>
/// </remarks>
public interface ICliAgentDispatcher
{
    /// <summary>
    /// 入队一次运行，立即返回 runId。执行是异步的 —— 外部 agent 的任务可能跑数小时，
    /// 不能挂在 HTTP 请求生命周期上。
    /// </summary>
    Task<Result<Guid>> EnqueueAsync(
        CliRunRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 订阅一次运行的实时事件流。可在运行中途接入：从 <paramref name="fromSequence"/>
    /// 起先补发已持久化的历史事件，再接上实时流。
    /// </summary>
    IAsyncEnumerable<CliAgentEvent> StreamAsync(
        Guid runId,
        int fromSequence = 0,
        CancellationToken cancellationToken = default);

    /// <summary>查询一次运行。</summary>
    Task<Result<CliRunDto>> GetAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>分页查询运行记录。</summary>
    Task<Result<IPagedList<CliRunDto>>> GetListAsync(
        CliRunQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>取历史事件（用于详情页回放）。</summary>
    Task<Result<List<CliRunMessageDto>>> GetMessagesAsync(
        Guid runId, int fromSequence = 0, CancellationToken cancellationToken = default);

    /// <summary>取消一次运行。已在跑的会整树终止子进程。</summary>
    Task<Result> CancelAsync(Guid runId, CancellationToken cancellationToken = default);
}
