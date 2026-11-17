namespace Tnzi.AI.Events;

/// <summary>
/// 一次外部 CLI 运行（<c>ICliAgentDispatcher.EnqueueAsync</c> 入队的那种）到达终态时发布：
/// 正常完成、失败、超时、被取消，无论由哪条路径落下、落在哪个宿主上。
/// </summary>
/// <remarks>
/// <para>
/// <b>每次转换恰好一条</b>：终态只由 <c>Tnzi.AI.Cli</c> 的一个入口写下，写入以「这条运行还没结束」为条件，
/// 只有真正完成这次转换的一方发布；两个宿主为同一条运行赛跑时，输掉的一方什么都不写、什么都不发。
/// 还在排队、执行中的运行不发；打回队列（宿主停机、租约回收）不是终态，也不发。
/// 线程被删除时连同记录一起删掉的运行（排队中的、删除之后才被认领的）没有终态，不发，那一侧的信号是 <see cref="ThreadDeletedEvent"/>。
/// </para>
/// <para>
/// <b>发布在终态提交之后</b>：处理器按 <see cref="RunId"/> 回读，看到的就是已经结束的运行。
/// 只走本地总线（没有发件箱），在记下这次转换的那个宿主上派发，那不一定是入队的宿主；
/// 终态提交之后、发布之前进程死掉，这一次的事件就没有了。
/// </para>
/// <para>
/// 同步处理器跑在终态转换里面（例如执行器在清理工作区、结算配额之前），发通知这类慢操作请标
/// <see cref="BackgroundEventHandlerAttribute"/>。后台处理器没有当前用户：<c>ICliAgentDispatcher.GetAsync</c>
/// 按调用者可见性判定，在那里答 404；通知需要的字段都在事件上，要更多就直接读 <c>CliRun</c> 仓储（处理器运行在
/// <see cref="EventBase.TenantId"/> 指定的租户下，已设为运行自己的租户）。
/// </para>
/// <para>
/// 不复用 <see cref="AgentRunCompletedEvent"/> / <see cref="AgentRunFailedEvent"/> / <see cref="AgentRunCancelledEvent"/>：
/// 那组的 <c>RunId</c> 是内建执行的 <c>AgentRun</c> 主键，外部执行的记录是另一张表的 <c>CliRun</c>（不建 <c>AgentRun</c>），
/// 按那组的约定拿 RunId 去查一律落空，已有的订阅者还会开始收到他们查不到的 id；那组按结局拆成三个互斥类型，
/// 超时并进 Failed，没有稳定的失败分类，<c>IsStreaming</c> / <c>ExecutionMode</c> 是内建管线的概念。
/// 这里一个类型覆盖全部结局，由 <see cref="Status"/> 区分。
/// </para>
/// </remarks>
public class CliRunFinishedEvent : EventBase
{
    /// <summary>运行 ID（<c>EnqueueAsync</c> 返回的那个）</summary>
    public Guid RunId { get; init; }

    /// <summary>调度方给的框架 AgentRun 关联 id（只对受信任的调度方采纳，多数运行为 null）</summary>
    public Guid? AgentRunId { get; init; }

    /// <summary>执行的 Agent</summary>
    public Guid AgentId { get; init; }

    /// <summary>关联的对话线程（一次性任务为 null）</summary>
    public Guid? ThreadId { get; init; }

    /// <summary>运行归属的用户（运行记录的 <c>CreatorId</c>：入队时的当前用户；后台入队时为 null）</summary>
    public Guid? UserId { get; init; }

    /// <summary>终态：Completed / Failed / Cancelled / TimedOut</summary>
    public CliRunStatus Status { get; init; }

    /// <summary>失败分类（Completed 时为 null；取消为 <see cref="CliRunFailureReason.Cancelled"/>）</summary>
    public CliRunFailureReason? FailureReason { get; init; }

    /// <summary>失败或取消的原因文本（含 stderr 尾部），没有时为 null</summary>
    public string? Error { get; init; }

    /// <summary>最终交付物（与运行记录的 <c>Output</c> 相同，不含过程叙述），只有 Completed 才有</summary>
    public string? Output { get; init; }

    /// <summary>执行耗时（毫秒），进程没有跑起来的运行为 0</summary>
    public long DurationMs { get; init; }

    /// <summary>这次运行回答的用户消息（聊天路径入队的运行才有）</summary>
    public Guid? UserMessageId { get; init; }

    /// <summary>
    /// 运行结束时写进线程的回复消息：完成的运行，以及被停下或失败之前已经写出内容的运行。没写时为 null。
    /// 回复先于终态写入，所以收到事件时它已经在线程里了。
    /// </summary>
    public Guid? AssistantMessageId { get; init; }

    /// <summary>
    /// <see cref="FailureReason"/> 为 <see cref="CliRunFailureReason.UsageLimit"/> 时，账号用量上限的重置时间（UTC）；
    /// CLI 没报出来时为 null。据此告诉用户「大约几点之后再试」。
    /// </summary>
    public DateTime? UsageLimitResetsAt { get; init; }
}

/// <summary>
/// 同一线程的下一条运行接过了一条等后台任务的运行的进程（<c>Tnzi.AI.Cli</c>，文档 <c>ai-cli.md</c> §6.18「等待期间的下一条消息」）：
/// 从这一刻起，那个进程的模型调用、回写调用与用量都属于 <see cref="ToRunId"/>。
/// </summary>
/// <remarks>
/// <para>
/// <b>在新提示写进进程的 stdin 之前同步发布</b>（交出它的 <see cref="FromRunId"/> 已经落了终态）：消费方按运行签进进程环境的东西（模型网关令牌之类）
/// 绑在起进程的那条运行（<see cref="LaunchedByRunId"/>）上、改不了，处理器在这里把授权与记账挪给 <see cref="ToRunId"/>，接下来的请求就不会被拒。
/// 处理器的异常只记 Warning，这一轮照常进行：权威的答案是持久化的那条链接（<c>CliRun.ProcessLaunchedByRunId</c>，
/// 经 <c>ICliProcessOwnership.GetCurrentOwnerAsync(LaunchedByRunId)</c> 随时可查，别的副本也看得见），事件只是让处理器不必轮询。
/// </para>
/// <para>
/// 只走本地总线，在执行这条运行的副本上派发。同步处理器跑完之后提示才写进去，所以挪授权要放在同步处理器里；
/// 标了 <see cref="BackgroundEventHandlerAttribute"/> 的处理器可能晚于进程的第一次模型调用，只适合通知这类旁路工作。
/// </para>
/// </remarks>
public class CliRunProcessHandedOverEvent : EventBase
{
    /// <summary>交出进程的运行（已经落了终态）。</summary>
    public Guid FromRunId { get; init; }

    /// <summary>接过进程的运行（Running）：此刻拥有进程。</summary>
    public Guid ToRunId { get; init; }

    /// <summary>起这个进程的运行：进程的启动环境绑在它上面。一路交接下去也是最初起它的那条（第一次交接时等于 <see cref="FromRunId"/>）。</summary>
    public Guid LaunchedByRunId { get; init; }

    /// <summary>接过进程的运行的 Agent。</summary>
    public Guid AgentId { get; init; }

    /// <summary>线程（两条运行同一条）。</summary>
    public Guid? ThreadId { get; init; }

    /// <summary>接过进程的运行归属的用户（它的 <c>CreatorId</c>）。</summary>
    public Guid? UserId { get; init; }
}
