namespace Tnzi.Authorization.Entities;

/// <summary>
/// 双人授权请求：一个「必须由第二个人点头才能执行」的动作。
/// </summary>
/// <remarks>
/// <para>
/// <b>发起、批准、执行是三步，不是两步。</b>批准只产生一张许可，动作仍然由发起方回来执行 ——
/// 这样框架不必知道每种业务动作怎么做，也不会出现「批准按钮顺手把款打出去了」这种
/// 让审批人承担执行后果的形态。
/// </para>
/// <para>
/// ★ <b>批准的是一份参数，不是一个操作名。</b><see cref="PayloadJson"/> 在发起时定格，
/// 执行时逐字比对。少了这一比，「转账 100 元」批下来之后可以改成「转账 100 万」再执行，
/// 而审计上看到的是一次合规的双人授权。
/// </para>
/// </remarks>
public class DualControlRequest : MultiTenantAuditedEntity<Guid>
{
    /// <summary>
    /// 业务动作标识，例如 <c>finance.payrun.void</c>。
    /// </summary>
    /// <remarks>
    /// 由应用自定，框架不预设。它同时是「谁能批准」的判据来源：服务层按
    /// <c>{Operation}</c> 加上 <c>DualControlOptions.ApprovalPermissionSuffix</c>
    /// （默认 <c>".approve"</c>）查权限，因此这个字符串应当与应用声明的权限码对得上。
    /// </remarks>
    public string Operation { get; set; } = null!;

    /// <summary>动作目标的标识（单据号、记录主键的字符串形式）；与业务无关的动作可为空。</summary>
    public string? TargetId { get; set; }

    /// <summary>
    /// 请求参数快照。审批人看到的是它，执行时比对的也是它。
    /// </summary>
    /// <remarks>
    /// ★ 存原文而不是哈希：审批人得看得见自己在批什么。<b>因此不要往里放密钥、口令或
    /// 完整的个人敏感信息</b> —— 放摘要与足以做判断的字段即可。
    /// </remarks>
    public string? PayloadJson { get; set; }

    /// <summary>供审批人一眼看懂的说明。</summary>
    public string? Description { get; set; }

    /// <summary>当前状态。</summary>
    public DualControlStatus Status { get; set; } = DualControlStatus.Pending;

    /// <summary>发起人。</summary>
    public Guid RequesterId { get; set; }

    /// <summary>批准或拒绝的人；未决时为空。</summary>
    public Guid? ApproverId { get; set; }

    /// <summary>批准或拒绝的时间；未决时为空。</summary>
    public DateTime? DecidedAt { get; set; }

    /// <summary>批准或拒绝时留下的说明。</summary>
    public string? DecisionComment { get; set; }

    /// <summary>许可的失效时间。</summary>
    /// <remarks>
    /// 过期不是清理策略，是安全边界：一张放着三个月还能用的许可，
    /// 与「这个动作需要两个人当场同意」不是同一件事。
    /// </remarks>
    public DateTime ExpiresAt { get; set; }

    /// <summary>许可是否已被执行方取用。</summary>
    /// <remarks>★ 一次批准只能换一次执行，否则「批一次、跑十遍」。</remarks>
    public bool IsConsumed { get; set; }

    /// <summary>取用时间。</summary>
    public DateTime? ConsumedAt { get; set; }
}

/// <summary>
/// 双人授权请求的状态。
/// </summary>
public enum DualControlStatus
{
    /// <summary>等待第二个人决定。</summary>
    Pending = 0,

    /// <summary>已批准，可在有效期内执行一次。</summary>
    Approved = 1,

    /// <summary>已拒绝。</summary>
    Rejected = 2,

    /// <summary>已被发起方撤回。</summary>
    Cancelled = 3
}
