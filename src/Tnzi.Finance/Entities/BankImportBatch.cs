namespace Tnzi.Finance.Entities;

/// <summary>
/// 银行流水导入批次（一次文件导入或 feed 拉取）
/// </summary>
/// <remarks>
/// 撤销 = 软删批次及其全部流水行，仅当批内无任何已匹配（Matched）行时允许。
/// </remarks>
public class BankImportBatch : MultiTenantAuditedEntity<Guid>
{
    /// <summary>对应银行账户档案挂载的资金科目</summary>
    public Guid AccountId { get; set; }

    /// <summary>来源</summary>
    public BankTransactionSource Source { get; set; }

    /// <summary>文件名（文件导入时）</summary>
    public string? FileName { get; set; }

    /// <summary>对账单区间起</summary>
    public DateTime? PeriodFrom { get; set; }

    /// <summary>对账单区间止</summary>
    public DateTime? PeriodTo { get; set; }

    /// <summary>成功导入行数</summary>
    public int ImportedCount { get; set; }

    /// <summary>去重跳过行数</summary>
    public int SkippedCount { get; set; }

    /// <summary>对账单期末余额（LEDGERBAL / 文件提供时）</summary>
    public decimal? StatementEndBalance { get; set; }
}
