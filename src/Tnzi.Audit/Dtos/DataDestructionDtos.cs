namespace Tnzi.Audit.Dtos;

/// <summary>
/// 一次销毁扫描的汇总结果。
/// </summary>
public class DataDestructionRunDto
{
    /// <summary>本次扫描涉及的策略结果。</summary>
    public List<DataDestructionPolicyResultDto> Policies { get; set; } = [];

    /// <summary>本次扫描实际销毁的总条数。</summary>
    public int TotalDestroyed => Policies.Sum(p => p.DestroyedCount);

    /// <summary>本次扫描因诉讼保全而跳过的总条数。</summary>
    public int TotalHeld => Policies.Sum(p => p.HeldCount);

    /// <summary>是否为空跑（不真的删除数据）。</summary>
    public bool IsDryRun { get; set; }
}

/// <summary>
/// 单条策略在本次扫描中的结果。
/// </summary>
public class DataDestructionPolicyResultDto
{
    /// <summary>策略标识。</summary>
    public string PolicyName { get; set; } = string.Empty;

    /// <summary>实体类型全名。</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>本次使用的截止时间。</summary>
    public DateTime Cutoff { get; set; }

    /// <summary>实际销毁的条数。</summary>
    public int DestroyedCount { get; set; }

    /// <summary>已到期但被诉讼保全跳过的条数。</summary>
    public int HeldCount { get; set; }

    /// <summary>
    /// 本批是否可能还有更多到期数据（候选数达到了 <c>BatchSize</c> 上限）。
    /// </summary>
    /// <remarks>
    /// 为真说明剩下的会在下一个周期继续销毁。首次启用时通常为真。
    /// </remarks>
    public bool HasMore { get; set; }

    /// <summary>写出的销毁证明 ID；本轮无到期数据时为空。</summary>
    public Guid? CertificateId { get; set; }

    /// <summary>该策略本轮失败时的原因；成功为空。</summary>
    public string? Error { get; set; }
}

/// <summary>
/// 销毁证明条目。
/// </summary>
public class DataDestructionDto
{
    /// <summary>证明 ID。</summary>
    public Guid Id { get; set; }

    /// <summary>全局链内的序号。</summary>
    public long Sequence { get; set; }

    /// <summary>策略标识。</summary>
    public string PolicyName { get; set; } = string.Empty;

    /// <summary>实体类型全名。</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>本次销毁使用的截止时间。</summary>
    public DateTime Cutoff { get; set; }

    /// <summary>实际销毁的条数。</summary>
    public int DestroyedCount { get; set; }

    /// <summary>因诉讼保全跳过的条数。</summary>
    public int HeldCount { get; set; }

    /// <summary>被销毁记录标识的集合摘要。</summary>
    public string IdentifierDigest { get; set; } = string.Empty;

    /// <summary>处置方式。</summary>
    public string Mode { get; set; } = string.Empty;

    /// <summary>这批数据所用的加密密钥标识。</summary>
    public string? EncryptionKeyId { get; set; }

    /// <summary>该密钥是否已确认不在密钥环里。</summary>
    public bool IsKeyDestroyed { get; set; }

    /// <summary>是否为空跑产生的证明。</summary>
    public bool IsDryRun { get; set; }

    /// <summary>手动触发时的执行者用户 ID。</summary>
    public Guid? ExecutedByUserId { get; set; }

    /// <summary>本条哈希。</summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>写出时间。</summary>
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 销毁证明查询条件。
/// </summary>
public class DataDestructionQueryDto : PagedQueryDto
{
    /// <summary>按策略标识过滤。</summary>
    public string? PolicyName { get; set; }

    /// <summary>起始时间（含）。</summary>
    public DateTime? StartTime { get; set; }

    /// <summary>结束时间（含）。</summary>
    public DateTime? EndTime { get; set; }

    /// <summary>是否只看真实销毁（排除空跑）。</summary>
    public bool? IsDryRun { get; set; }
}
