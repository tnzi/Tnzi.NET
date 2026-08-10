namespace Tnzi.Audit.Entities;

/// <summary>
/// 销毁证明：某条保留策略在某一时刻销毁了哪一批数据。
/// </summary>
/// <remarks>
/// <para>
/// <strong>证明里没有被销毁的数据本身。</strong>那是显然的要求却容易做错：
/// 一份把内容摘录进去的「销毁证明」等于没销毁。这里只有条数、
/// 标识集合的哈希摘要，以及处置方式。
/// </para>
/// <para>
/// <strong>条目构成全局防篡改哈希链。</strong>与按用户分链的
/// <see cref="AuditRecordAccess"/> 不同，销毁是低频的系统级动作，
/// 不存在写入争抢，因此用单条全局链——它更强：删掉中间任何一条，
/// 其后所有条目的校验都会失败，而按用户分链只能证明单个用户那一条链的完整性。
/// </para>
/// <para>
/// <strong>本实体只在启用该能力时才映射为数据表</strong>（见 <c>Audit:DataDestruction:Enabled</c>）。
/// </para>
/// </remarks>
[AuditIgnore]
public class AuditDataDestruction : CreationAuditedEntity<Guid>
{
    /// <summary>
    /// 全局链内的递增序号，从 1 开始。
    /// </summary>
    /// <remarks>
    /// 建了唯一索引：并发写入抢到同一序号时由数据库拒绝，服务层重读链尾后重试。
    /// </remarks>
    public long Sequence { get; set; }

    /// <summary>触发本次销毁的策略标识。</summary>
    public string PolicyName { get; set; } = string.Empty;

    /// <summary>被销毁记录的实体类型全名。</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// 本次销毁的截止时间：时间戳早于它的记录被视为到期。
    /// </summary>
    /// <remarks>
    /// 记下它而不是只记「保留期是多少天」，是因为保留期本身可能被改过。
    /// 截止时间是当时实际执行的判据，事后可复算。
    /// </remarks>
    public DateTime Cutoff { get; set; }

    /// <summary>实际销毁的条数。</summary>
    public int DestroyedCount { get; set; }

    /// <summary>
    /// 已到期但因诉讼保全而<strong>未</strong>销毁的条数。
    /// </summary>
    /// <remarks>
    /// 这一列是必需的：没有它，一份「本轮销毁 3 条」的证明无法自证
    /// 那到底是「只有 3 条到期」还是「到期 30 条但 27 条被漏掉了」。
    /// </remarks>
    public int HeldCount { get; set; }

    /// <summary>
    /// 被销毁记录标识的集合摘要（SHA-256）。
    /// </summary>
    /// <remarks>
    /// 标识排序后连接再哈希，因此与销毁顺序无关。
    /// 持有原始标识清单的人可以复算它来验证这批确实是那些记录，
    /// 而摘要本身不会泄漏「曾经存在过哪些记录」。
    /// </remarks>
    public string IdentifierDigest { get; set; } = string.Empty;

    /// <summary>
    /// 被销毁记录的标识清单（仅在 <c>StoreIdentifiers</c> 开启时写入）。
    /// </summary>
    /// <remarks>
    /// 默认为空。要逐条回答「我的第 X 号记录销毁了吗」才需要它，
    /// 代价是库里永久留下一份「曾经存在过哪些记录」的清单。
    /// </remarks>
    public string? Identifiers { get; set; }

    /// <summary>处置方式（来自 <c>IDataDestroyer.Mode</c>，如 <c>hard-delete</c>）。</summary>
    public string Mode { get; set; } = string.Empty;

    /// <summary>这批数据所用的字段加密密钥标识（策略声明了才有）。</summary>
    public string? EncryptionKeyId { get; set; }

    /// <summary>
    /// 该密钥是否已确认不在当前密钥环里。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 只有它为真，「已销毁」才对<strong>备份介质上的副本</strong>同样成立：
    /// 删除线上的行不能让磁带上的那份消失，而密钥没了，磁带上的密文就永远打不开了。
    /// </para>
    /// <para>
    /// 框架不销毁密钥（密钥在配置或 KMS 里，那是部署方的领地），
    /// 只在每次销毁时回查并如实记录。为 <c>false</c> 说明还差运维那一步。
    /// </para>
    /// </remarks>
    public bool IsKeyDestroyed { get; set; }

    /// <summary>
    /// 本条是否为空跑（<c>Audit:DataDestruction:DryRun</c>）产生的。
    /// </summary>
    /// <remarks>
    /// 空跑照常出证明但不真的删数据。不把它标出来，事后就没人说得清
    /// 那批数据到底还在不在。
    /// </remarks>
    public bool IsDryRun { get; set; }

    /// <summary>手动触发时的执行者用户 ID；定时触发为空。</summary>
    public Guid? ExecutedByUserId { get; set; }

    /// <summary>链上一条的哈希；第一条为空串。</summary>
    public string PreviousHash { get; set; } = string.Empty;

    /// <summary>本条哈希，覆盖链上一条的哈希与本条的全部关键字段。</summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>租户 ID（未启用多租户时不映射为列）。</summary>
    public Guid? TenantId { get; set; }
}
