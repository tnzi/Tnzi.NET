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

    /// <summary>
    /// 取消一次运行。已在跑的会整树终止子进程。只取消<b>调用者看得见</b>的运行（自己派出的，或持管理端查看码），
    /// 其余一律 404：这是用户发起的取消，用户端与管理端的控制器都调它。
    /// </summary>
    Task<Result> CancelAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 以系统身份取消一次运行：<b>不判定调用者能否看见它</b>，也不需要登录用户或租户上下文，后台任务里可以直接调。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 给服务端自己的流程用：访问权到期（临时通行证、委派会话结束）、账号停用、内容审核要求停下一条线程。
    /// 这些场景里没有「看得见这条运行的用户」可用，而 <see cref="CancelAsync"/> 只为看得见它的用户取消。
    /// </para>
    /// <para>
    /// ★ <b>绝不能挂到任何 HTTP 端点上</b>：它不看调用者是谁，接到一个用户够得着的入口上，
    /// 就成了「凭一个 runId 取消任何人（任何租户）的运行」。用户发起的取消一律走 <see cref="CancelAsync"/>。
    /// </para>
    /// <para>
    /// 按 Id 跨租户定位（后台作用域没有当前租户）。不存在答 404，已结束答 409。排队中的当场结束；
    /// 在跑的写下取消标记，执行它的宿主（本副本立刻，别的副本在下一个续租周期）停掉整棵进程树并落终态。
    /// 结局与用户取消完全一样：<c>Cancelled</c>、发布 <see cref="CliRunFinishedEvent"/>、已写出的回复照常写进线程。
    /// </para>
    /// <para>
    /// 默认实现答 501：没有覆写它的自定义调度器不提供这项能力，而不是假装成功。
    /// </para>
    /// </remarks>
    /// <param name="runId">运行。</param>
    /// <param name="reason">
    /// 为什么停（如 "Temporary access ended."）。写进运行的错误信息与结束事件，让记录读得出这是系统停的而不是用户按的；
    /// 运行已经带着一条原因时保留原来的。null = 用通用的取消说明。
    /// </param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<Result> CancelAsSystemAsync(Guid runId, string? reason = null, CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Failure(
            "This ICliAgentDispatcher implementation does not support system cancellation.", 501,
            ErrorCodes.CliSystemCancelNotSupported));

    /// <summary>
    /// 以系统身份取消一条线程上所有还没结束的运行，返回被要求停下的条数（0 = 这条线程上没有在跑的）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="CancelAsSystemAsync"/> 同一套规则（不判定可见性、跨租户、<b>绝不挂到 HTTP 端点上</b>），
    /// 已经结束的运行跳过而不是报错：调用方要的是「这条线程上不再有东西在跑」，不是逐条核对。
    /// 只管外部运行：内建 agent 的一轮挂在发起它的那次请求上，请求一断它就停，没有可以从服务端停的句柄。
    /// </remarks>
    /// <param name="threadId">线程。</param>
    /// <param name="reason">同 <see cref="CancelAsSystemAsync"/>。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<Result<int>> CancelThreadRunsAsSystemAsync(Guid threadId, string? reason = null, CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Failure<int>(
            "This ICliAgentDispatcher implementation does not support system cancellation.", 501,
            ErrorCodes.CliSystemCancelNotSupported));

    /// <summary>
    /// 回答一次运行的一个权限请求（<see cref="CliAgentEventType.PermissionRequest"/> 事件里的 <c>permission.requestId</c>）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 只有派出这次运行的人、或持 <c>ai.cliRun.execute</c> 的人能回答；其余一律 404（与运行不存在同一个答案）。
    /// 选项不在请求给出的那些里 400；请求已经有了结局（别人先答了、超时、运行已结束）409。
    /// </para>
    /// <para>
    /// 回答先落库，执行这次运行的服务端副本（可能是另一个）随即把它写给 CLI，并在事件流里发 <see cref="CliAgentEventType.PermissionResolved"/>。
    /// </para>
    /// <para>默认实现答 501：没有覆写它的自定义调度器不转发权限请求。</para>
    /// </remarks>
    Task<Result> AnswerPermissionAsync(
        Guid runId, Guid requestId, CliPermissionAnswerDto answer, CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Failure(
            "This ICliAgentDispatcher implementation does not relay permission requests.", 501,
            ErrorCodes.CliPermissionNotSupported));
}
