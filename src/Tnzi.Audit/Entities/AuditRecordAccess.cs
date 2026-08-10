namespace Tnzi.Audit.Entities;

/// <summary>
/// 记录级读取审计条目：谁在什么时候打开了<strong>哪一条</strong>数据。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="AuditOperation"/> 的分工：后者回答「谁调用了哪个端点」，
/// 是请求级的；本实体回答「谁读了哪一条记录」，是数据级的。
/// 隐私合规追问的通常是后者 —— 「上个月谁看过这位举报人的材料」，
/// 端点级日志答不了这个问题。
/// </para>
/// <para>
/// <strong>条目构成防篡改哈希链</strong>（按用户分链）：每条记录的
/// <see cref="Hash"/> 覆盖了上一条的 <see cref="PreviousHash"/>，
/// 因此删改中间任意一条都会让其后所有条目的校验失败。
/// 这不能阻止有库权限的人改数据，但能让改动<strong>无法不留痕迹</strong>。
/// </para>
/// <para>
/// <strong>本实体只在启用该能力时才映射为数据表</strong>（见 <c>Audit:RecordAccess:Enabled</c>），
/// 不使用的应用不会多出一张空表。
/// </para>
/// </remarks>
[AuditIgnore]
public class AuditRecordAccess : CreationAuditedEntity<Guid>
{
    /// <summary>
    /// 该用户链条内的递增序号，从 1 开始。
    /// </summary>
    /// <remarks>
    /// 与 <c>UserId</c> 组成唯一索引：并发写入抢到同一序号时由数据库拒绝，
    /// 服务层重试重新读取链尾。这是刻意用唯一约束而不是分布式锁来保证链完整，
    /// 审计写入是高频路径，不值得为它引入锁竞争。
    /// </remarks>
    public long Sequence { get; set; }

    /// <summary>
    /// 被读取的资源类型，建议用实体全名（如 <c>Tnzi.Identity.Entities.User</c>）。
    /// </summary>
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>
    /// 被读取记录的主键，统一以字符串保存以兼容 Guid / long / 复合键。
    /// </summary>
    public string ResourceId { get; set; } = string.Empty;

    /// <summary>
    /// 读取用途或场景（如 <c>case-review</c>、<c>export</c>），便于事后区分正常业务与异常访问。
    /// </summary>
    public string? Purpose { get; set; }

    /// <summary>读取者用户 ID。</summary>
    public Guid? UserId { get; set; }

    /// <summary>读取者用户名（冗余保存，避免用户改名后追不回当时是谁）。</summary>
    public string? UserName { get; set; }

    /// <summary>链上一条的哈希；本用户第一条为空串。</summary>
    public string PreviousHash { get; set; } = string.Empty;

    /// <summary>本条哈希，覆盖链上一条的哈希与本条的全部关键字段。</summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>租户 ID（未启用多租户时不映射为列）。</summary>
    public Guid? TenantId { get; set; }
}
