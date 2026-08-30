namespace Tnzi.Authorization.Dtos;

/// <summary>
/// 发起一个双人授权请求。
/// </summary>
/// <param name="Operation">业务动作标识，例如 <c>finance.payrun.void</c>。</param>
/// <param name="TargetId">动作目标的标识；与业务记录无关的动作可为空。</param>
/// <param name="PayloadJson">
/// 请求参数快照。审批人看到的是它，执行时比对的也是它。
/// ★ 不要放密钥、口令或完整的个人敏感信息 —— 它是给人看的。
/// </param>
/// <param name="Description">供审批人一眼看懂的说明。</param>
/// <param name="Lifetime">许可有效期；不给则取模块默认值。</param>
public record DualControlRequestInput(
    string Operation,
    string? TargetId = null,
    string? PayloadJson = null,
    string? Description = null,
    TimeSpan? Lifetime = null);

/// <summary>
/// 一个双人授权请求。
/// </summary>
public class DualControlRequestDto
{
    /// <summary>请求标识。</summary>
    public Guid Id { get; set; }

    /// <summary>业务动作标识。</summary>
    public string Operation { get; set; } = null!;

    /// <summary>动作目标的标识。</summary>
    public string? TargetId { get; set; }

    /// <summary>请求参数快照。</summary>
    public string? PayloadJson { get; set; }

    /// <summary>说明。</summary>
    public string? Description { get; set; }

    /// <summary>当前状态。</summary>
    public DualControlStatus Status { get; set; }

    /// <summary>发起人。</summary>
    public Guid RequesterId { get; set; }

    /// <summary>
    /// 发起人的显示名；Identity 模块未加载或该用户已被删除时为空。
    /// </summary>
    /// <remarks>
    /// ★ <b>读取时解析，不落库</b>。审批人要回答的是「<b>这个人</b>要做这件事，我同不同意」，
    /// 而一列 Guid 回答不了它 —— 少了这一列，四眼原则的第二只眼睛看不见第一只是谁。
    /// <para>
    /// 与 <c>Tnzi.Audit</c> 的做法（写入时把 <c>UserName</c> 盖进审计行）刻意不同：审计行是
    /// <b>当时</b>的快照，姓名跟着定格才对；而待办面要的是<b>现在</b>联系得上谁，况且读时解析
    /// 不需要加列、不需要迁移，也不会在改名后留下一堆对不上的旧名字。
    /// </para>
    /// </remarks>
    public string? RequesterName { get; set; }

    /// <summary>批准或拒绝的人。</summary>
    public Guid? ApproverId { get; set; }

    /// <summary>批准或拒绝的人的显示名；同 <see cref="RequesterName"/>，读取时解析。</summary>
    public string? ApproverName { get; set; }

    /// <summary>发起时间。</summary>
    public DateTime CreationTime { get; set; }

    /// <summary>决定时间。</summary>
    public DateTime? DecidedAt { get; set; }

    /// <summary>决定时留下的说明。</summary>
    public string? DecisionComment { get; set; }

    /// <summary>失效时间。</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>许可是否已被取用。</summary>
    public bool IsConsumed { get; set; }

    /// <summary>
    /// 此刻是否还能取用。
    /// </summary>
    /// <remarks>
    /// 现算不落库：一条 <c>Approved</c> 的记录会随时间自己变成不可用，
    /// 而没有任何写操作会在那一刻发生 —— 落库的那一位永远是过期的。
    /// </remarks>
    public bool IsUsable => Status == DualControlStatus.Approved && !IsConsumed && ExpiresAt > DateTime.UtcNow;
}

/// <summary>
/// 双人授权请求的查询条件。
/// </summary>
public class DualControlQueryDto : PagedQueryDto
{
    /// <summary>按动作标识过滤。</summary>
    public string? Operation { get; set; }

    /// <summary>按目标标识过滤。</summary>
    public string? TargetId { get; set; }

    /// <summary>按状态过滤。</summary>
    public DualControlStatus? Status { get; set; }

    /// <summary>按发起人过滤。</summary>
    public Guid? RequesterId { get; set; }

    /// <summary>只看还能取用的。</summary>
    public bool? UsableOnly { get; set; }
}

/// <summary>
/// 批准或拒绝时附带的说明。
/// </summary>
public class DualControlDecisionDto
{
    /// <summary>说明或理由。</summary>
    public string? Comment { get; set; }
}
