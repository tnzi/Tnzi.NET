namespace Tnzi.AI.Cli.Entities;

/// <summary>
/// 一次外部 agent 执行。
/// </summary>
/// <remarks>
/// <para>
/// 这张表同时是<b>任务队列</b>：<see cref="Status"/> = <c>Queued</c> 的行等待被认领，
/// 认领靠一条带 <c>Status == Queued</c> 前置条件的原子更新（受影响行数 0 = 被别人抢先），
/// 而不是分布式锁。数据库无关是硬约束，所以不用 <c>FOR UPDATE SKIP LOCKED</c> 那种方言。
/// </para>
/// <para>
/// <see cref="LeaseExpiresAt"/> 是崩溃恢复的全部依据：认领方每隔一段时间续期，
/// 宿主进程消失后租约自然过期，回收扫描把行打回 <c>Queued</c>。没有它，一次进程崩溃
/// 就能让任务永久卡在 <c>Dispatched</c>。
/// </para>
/// </remarks>
public class CliRun : MultiTenantAuditedEntity<Guid>
{
    /// <summary>执行的 Agent。</summary>
    public Guid AgentId { get; set; }

    /// <summary>执行所在的外部运行时。</summary>
    public Guid CliRuntimeId { get; set; }

    /// <summary>
    /// 调用方给的框架 AgentRun 关联 id（只对受信任的调度方采纳，见 <c>CliRunRequestDto.AgentRunId</c>）。
    /// 只是关联键：外部执行不新建 AgentRun，也不出现在 AgentRun 的列表或视图里。
    /// </summary>
    public Guid? AgentRunId { get; set; }

    /// <summary>关联对话线程（聊天场景）。</summary>
    public Guid? ThreadId { get; set; }

    /// <summary>
    /// 这次运行回答的用户消息（聊天场景）。它的附件就是交给 CLI 的那些；它在线程里的位置决定这次运行从哪一轮续接会话：
    /// 编辑或重新生成删掉了它之后的轮次（或它本身）时，CLI 会话也从它之前的那一轮接着走。
    /// </summary>
    public Guid? UserMessageId { get; set; }

    /// <summary>运行结束时写进线程的回复消息（没写时为 null）。</summary>
    public Guid? AssistantMessageId { get; set; }

    /// <summary>
    /// 以 <see cref="CliRunFailureReason.UsageLimit"/> 结束时，账号用量上限的重置时间（UTC）；CLI 没报出来时为 null。
    /// </summary>
    public DateTime? UsageLimitResetsAt { get; set; }

    /// <summary>状态。</summary>
    public CliRunStatus Status { get; set; } = CliRunStatus.Queued;

    /// <summary>队列优先级，大者先。</summary>
    public int Priority { get; set; }

    /// <summary>提示词。</summary>
    public string Prompt { get; set; } = string.Empty;

    /// <summary>本轮易变上下文（追加到提示尾部，不进 brief）。</summary>
    public string? PerTurnContext { get; set; }

    /// <summary>最终交付物（不含过程叙述）。</summary>
    public string? Output { get; set; }

    /// <summary>错误信息（含 stderr 尾部）。</summary>
    public string? Error { get; set; }

    /// <summary>
    /// 失败原因分类。在**做出判断的那个分支**确定，不从错误字符串反推。
    /// </summary>
    public CliRunFailureReason? FailureReason { get; set; }

    /// <summary>provider 侧会话 ID，用于续接。</summary>
    public string? ProviderSessionId { get; set; }

    /// <summary>
    /// 这次运行结束时 provider 会话里的最后一个条目（claude：流里最后一个 assistant / user 帧的 uuid）。
    /// 之后要丢掉这次运行以后的轮次时（编辑、重新生成），从这里截断续接、分叉出一个新会话。不支持截断的 provider 为 null。
    /// </summary>
    public string? ProviderSessionEntryId { get; set; }

    /// <summary>
    /// 第一次执行时定下的截断点：非空时这次运行以 <c>--resume-session-at</c> 从 <see cref="ProviderSessionId"/> 的这个条目处
    /// 分叉续接（丢掉它之后被替换的轮次），与续接指针一起持久化，重新执行沿用它。
    /// </summary>
    public string? ResumeAtEntryId { get; set; }

    /// <summary>本次工作目录绝对路径。</summary>
    public string? WorkDirectory { get; set; }

    /// <summary>
    /// 使用方为这一轮指定的工作目录（执行目标上的绝对路径，<c>ICliRunPlacementResolver</c> 给出）；null = 按绑定的工作目录策略。
    /// </summary>
    /// <remarks>
    /// 按用户提供的目录对待：框架不往里写任何受管文件、永不删除它，占用标记按目录记。入队时定下，重新执行与重新附着都沿用它。
    /// 执行时必须是执行目标声明的某个工作目录根本身或位于其下（<c>ICliHostTarget.WorkDirectoryRoots</c>）。
    /// </remarks>
    public string? AssignedWorkDirectory { get; set; }

    /// <summary><see cref="AssignedWorkDirectory"/> 在执行目标上还不存在时由框架建出来（经 <c>ICliHostTarget.CreateWorkDirectoryAsync</c>）；false = 不存在就失败。</summary>
    public bool CreateAssignedWorkDirectory { get; set; }

    /// <summary>
    /// 本次执行是否续接了既有会话：第一次执行时与 <see cref="ProviderSessionId"/> 一起定下，续接被拒、开新会话重试时置回 false。
    /// 仅供排障；向用户披露上下文丢失靠的是续接被拒后的重试（在本轮上下文里明说），不读这一列。
    /// </summary>
    public bool ResumeExpected { get; set; }

    /// <summary>认领租约到期时间。防止宿主崩溃后任务永久卡在 Dispatched。</summary>
    public DateTime? LeaseExpiresAt { get; set; }

    /// <summary>认领本次运行的宿主标识。</summary>
    public string? ClaimedByHostId { get; set; }

    /// <summary>认领时间。</summary>
    public DateTime? DispatchedAt { get; set; }

    /// <summary>进程启动时间。</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>结束时间。</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>执行耗时（毫秒）。</summary>
    public long DurationMs { get; set; }

    /// <summary>这一轮按模型分组的 token 用量 JSON（续接被拒重试时两次相加）。</summary>
    /// <remarks>是这一轮的，不是会话累计：CLI 报的累计值记在 <see cref="SessionUsageJson"/>，这里存折算后的那份。</remarks>
    public string? UsageJson { get; set; }

    /// <summary>
    /// 这一轮结束时 CLI 报的会话累计用量 JSON（按模型）。下一轮续接同一会话时拿它当基线，从累计值里折算出那一轮。
    /// </summary>
    /// <remarks>
    /// 协议不报累计值时为 null。没有收到 result 帧的一轮（取消、崩溃）存的是估算值：上一份累计值加上这一轮逐帧看到的用量。
    /// </remarks>
    public string? SessionUsageJson { get; set; }

    /// <summary>这一轮的成本（USD）：各模型的 provider 自报成本，没报的按配置的单价估算。</summary>
    public decimal? EstimatedCostUsd { get; set; }

    /// <summary>
    /// 这一轮用户选的模型（<c>--model</c>）；null = 用绑定上配的模型或 provider 默认。
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// 当前这次进程的重新附着句柄：执行宿主在起进程时交出（<see cref="Hosting.ICliProcess.ReattachHandle"/>），进程结束即清空。
    /// 非空 = 服务端停机时进程留在宿主上继续跑，运行保持 <c>Running</c>，重启后经 <see cref="Hosting.ICliHostTarget.ReattachAsync"/> 接回来。
    /// 本机宿主不给句柄，这一列恒为 null。
    /// </summary>
    public string? ReattachHandle { get; set; }

    /// <summary>
    /// 这条运行从什么时候起没有服务端在驱动它（停机时离开、或租约过期后第一次被发现），接回来时清空。
    /// <c>AI:Cli:Reattach:Timeout</c> 从这里算起，过了还接不上就以 <see cref="CliRunFailureReason.Interrupted"/> 结束。
    /// </summary>
    public DateTime? DetachedAt { get; set; }

    /// <summary>已产生的事件条数（下一个 Sequence 的来源）。</summary>
    public int MessageCount { get; set; }

    /// <summary>取消请求标记。执行中的宿主看到它就整树终止子进程。</summary>
    public bool CancelRequested { get; set; }

    /// <summary>
    /// 这一轮有没有人在等（调用方在入队时给出，<c>CliRunRequestDto.Attended</c>）。启动档案的 <c>Attended</c> 可以改写它。
    /// </summary>
    /// <remarks>
    /// 只影响「先问我」档：有人值守时权限请求变成 <c>PermissionRequest</c> 事件等使用方回答，没有人时按档位直接拒绝。
    /// </remarks>
    public bool Attended { get; set; }

    /// <summary>
    /// 回写凭据的哈希。
    /// </summary>
    /// <remarks>
    /// <b>只存哈希，不存原文</b>：原文只在启动子进程的那一刻存在于内存与它的 MCP 配置文件里。
    /// 数据库泄漏不该等于「拿到一把还能用的钥匙」。用 <see cref="AuditIgnoreAttribute"/> 标记，
    /// 免得实体级审计把它抄进审计表（审计查看者与运行执行者未必是同一信任级）。
    /// </remarks>
    [AuditIgnore]
    public string? WriteBackTokenHash { get; set; }

    /// <summary>回写凭据的过期时间。运行到达终态时也一并失效，两者取先到的那个。</summary>
    public DateTime? WriteBackTokenExpiresAt { get; set; }
}
