namespace Tnzi.Finance.Dtos;

/// <summary>
/// 期末重估请求（预览与运行共用）
/// </summary>
public class RunRevaluationDto
{
    /// <summary>重估基准日（重估至该日的余额）</summary>
    public DateTime AsOf { get; set; }

    /// <summary>限定重估的科目子集（null = 全部符合条件的外币科目）</summary>
    public List<Guid>? AccountIds { get; set; }

    /// <summary>凭证摘要（仅 Run 使用；null = 自动生成）</summary>
    public string? Memo { get; set; }
}

/// <summary>
/// 期末重估预览/结果（Run 成功时 JournalEntryId 非空）
/// </summary>
public class RevaluationPreviewDto
{
    public DateTime AsOf { get; set; }
    public string BaseCurrency { get; set; } = string.Empty;

    /// <summary>过账凭证（仅 Run 成功且有增量时非空）</summary>
    public Guid? JournalEntryId { get; set; }

    /// <summary>逐科目明细</summary>
    public List<RevaluationRowDto> Rows { get; set; } = new();

    /// <summary>净调整额（本位币；可过账行的调整之和）</summary>
    public decimal TotalAdjustment { get; set; }
}

/// <summary>
/// 期末重估逐科目行
/// </summary>
public class RevaluationRowDto
{
    public Guid AccountId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>科目限定币种</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>交易币余额（Σ TxnDebit − TxnCredit WHERE 行币种 == 科目币种）</summary>
    public decimal TxnBalance { get; set; }

    /// <summary>重估汇率（币种 → 本位币，基准日生效）</summary>
    public decimal Rate { get; set; }

    /// <summary>目标本位币价值（Round(TxnBalance × Rate)）</summary>
    public decimal TargetBase { get; set; }

    /// <summary>账面本位币余额（Σ Debit − Credit，含历次重估调整）</summary>
    public decimal BookBase { get; set; }

    /// <summary>本次调整额（TargetBase − BookBase；正 = 增记本位价值）</summary>
    public decimal Adjustment { get; set; }

    /// <summary>跳过原因（非空则不过账，如科目停用）</summary>
    public string? SkipReason { get; set; }
}
