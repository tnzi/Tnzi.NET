namespace Tnzi.Finance.Entities;

/// <summary>
/// 科目期间余额汇总（派生自 <see cref="JournalLine"/> 的月粒度毛额桶）
/// </summary>
/// <remarks>
/// 键 = (TenantId, AccountId, Period, Currency)；Period = yyyyMM 整数（取自行 <see cref="JournalLine.PostingDate"/>）。
/// 双口径毛额（非净额）：本位币 <see cref="Debit"/>/<see cref="Credit"/> + 交易币 <see cref="TxnDebit"/>/<see cref="TxnCredit"/>。
/// 由 <c>BalanceSummaryMaintainer</c> 在过账/冲销的同一事务内正向累加维护——Posted 行永不改删
/// （修正 = 冲销），因此汇总只增不减、永不需要负增量。报表读路径由
/// <see cref="Options.FinanceOptions.UseBalanceSummary"/> 门控消费（维护无条件启用）。
/// 镜像 <see cref="JournalLine"/> 的派生事实形态：无审计、无软删。
/// <see cref="LineCount"/> 仅供校验诊断（重建/校验时对齐明细行数）。
/// </remarks>
public class AccountPeriodBalance : EntityBase<Guid>, IMultiTenant
{
    /// <summary>租户ID</summary>
    public Guid? TenantId { get; set; }

    /// <summary>科目ID（无导航属性；汇总桶是派生事实，不参与图遍历）</summary>
    public Guid AccountId { get; set; }

    /// <summary>会计期间（yyyyMM 整数，取自行 PostingDate）</summary>
    public int Period { get; set; }

    /// <summary>行币种（多币种口径：交易币桶按此拆分）</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>借方合计（本位币）</summary>
    public decimal Debit { get; set; }

    /// <summary>贷方合计（本位币）</summary>
    public decimal Credit { get; set; }

    /// <summary>借方合计（交易币）</summary>
    public decimal TxnDebit { get; set; }

    /// <summary>贷方合计（交易币）</summary>
    public decimal TxnCredit { get; set; }

    /// <summary>本桶累计的明细行数（校验诊断用）</summary>
    public int LineCount { get; set; }
}
