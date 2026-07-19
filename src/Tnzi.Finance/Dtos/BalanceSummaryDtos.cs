namespace Tnzi.Finance.Dtos;

/// <summary>
/// 余额汇总重建结果
/// </summary>
public class BalanceSummaryRebuildDto
{
    /// <summary>重建后写入的桶数量</summary>
    public int Buckets { get; set; }

    /// <summary>参与聚合的已过账明细行数</summary>
    public long Lines { get; set; }

    /// <summary>重建耗时（毫秒）</summary>
    public long DurationMs { get; set; }
}

/// <summary>
/// 余额汇总校验结果（诊断汇总桶与总账的一致性，不修复）
/// </summary>
public class BalanceSummaryVerifyDto
{
    /// <summary>是否一致（无任何差异）</summary>
    public bool IsConsistent { get; set; }

    /// <summary>核对的桶数量（总账期望的桶数）</summary>
    public int CheckedBuckets { get; set; }

    /// <summary>差异总数（可能超过 <see cref="Differences"/> 的截断上限）</summary>
    public int TotalDifferences { get; set; }

    /// <summary>差异明细（最多前 100 条，防超大响应）</summary>
    public List<BalanceSummaryDifferenceDto> Differences { get; set; } = new();
}

/// <summary>
/// 单条余额汇总差异（期望值来自总账聚合、存量值来自汇总桶）
/// </summary>
public class BalanceSummaryDifferenceDto
{
    public Guid AccountId { get; set; }

    /// <summary>会计期间（yyyyMM）</summary>
    public int Period { get; set; }

    /// <summary>币种</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>差异类型</summary>
    public BalanceSummaryDifferenceKind Kind { get; set; }

    public decimal ExpectedDebit { get; set; }
    public decimal ExpectedCredit { get; set; }
    public decimal StoredDebit { get; set; }
    public decimal StoredCredit { get; set; }
}
