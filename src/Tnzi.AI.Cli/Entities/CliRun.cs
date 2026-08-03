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

    /// <summary>关联的框架 AgentRun —— 外部执行同样出现在统一 Run 视图里。</summary>
    public Guid? AgentRunId { get; set; }

    /// <summary>关联对话线程（聊天场景）。</summary>
    public Guid? ThreadId { get; set; }

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

    /// <summary>本次工作目录绝对路径。</summary>
    public string? WorkDirectory { get; set; }

    /// <summary>本次是否意图续接既有会话（用于向用户披露上下文丢失）。</summary>
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

    /// <summary>按模型分组的 token 用量 JSON。</summary>
    public string? UsageJson { get; set; }

    /// <summary>估算或 provider 自报的成本（USD）。</summary>
    public decimal? EstimatedCostUsd { get; set; }

    /// <summary>已产生的事件条数（下一个 Sequence 的来源）。</summary>
    public int MessageCount { get; set; }

    /// <summary>取消请求标记。执行中的宿主看到它就整树终止子进程。</summary>
    public bool CancelRequested { get; set; }

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
