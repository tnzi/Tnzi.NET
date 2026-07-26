namespace Tnzi.Finance.Dtos;

/// <summary>
/// 客户对账单
/// </summary>
public class CustomerStatementDto
{
    public Guid PartyId { get; set; }
    public string PartyName { get; set; } = string.Empty;
    public FinancePartyType PartyType { get; set; }
    public StatementStyle Style { get; set; }
    public string Currency { get; set; } = string.Empty;

    public DateTime PeriodFrom { get; set; }
    public DateTime PeriodTo { get; set; }

    /// <summary>期初余额（Activity 形态才有意义；OpenItem 恒 0）</summary>
    public decimal OpeningBalance { get; set; }

    /// <summary>期末余额 / 未清合计</summary>
    public decimal ClosingBalance { get; set; }

    /// <summary>逾期部分</summary>
    public decimal Overdue { get; set; }

    /// <summary>账龄分桶（与账龄报表同源）</summary>
    public AgingBucketsDto Buckets { get; set; } = new();

    /// <summary>催收强度建议</summary>
    public DunningLevel DunningLevel { get; set; }

    public List<StatementLineDto> Lines { get; set; } = new();
}

/// <summary>
/// 对账单的一行
/// </summary>
public class StatementLineDto
{
    public DateTime DocDate { get; set; }
    public DateTime? DueDate { get; set; }

    /// <summary>来源令牌</summary>
    public string DocType { get; set; } = string.Empty;

    public Guid DocId { get; set; }
    public string? Number { get; set; }

    /// <summary>增加欠款的金额（发票、账单）</summary>
    public decimal Charge { get; set; }

    /// <summary>减少欠款的金额（收款、贷项）</summary>
    public decimal Payment { get; set; }

    /// <summary>本行之后的累计余额</summary>
    public decimal Balance { get; set; }

    /// <summary>未清金额（OpenItem 形态用）</summary>
    public decimal Outstanding { get; set; }

    /// <summary>逾期天数（仍未清且已过期时为正）</summary>
    public int OverdueDays { get; set; }
}

/// <summary>
/// 出对账单的请求
/// </summary>
public class CustomerStatementQueryDto
{
    public StatementStyle Style { get; set; } = StatementStyle.OpenItem;

    /// <summary>期间起（Activity 形态必需；OpenItem 忽略）</summary>
    public DateTime? From { get; set; }

    /// <summary>截止日（默认今天）</summary>
    public DateTime? To { get; set; }
}

/// <summary>
/// 催收工作台的一行：谁该被催、催到什么程度
/// </summary>
public class DunningCandidateDto
{
    public Guid PartyId { get; set; }
    public string PartyName { get; set; } = string.Empty;
    public decimal OpenBalance { get; set; }
    public decimal Overdue { get; set; }

    /// <summary>最久那笔逾期了多少天</summary>
    public int OldestOverdueDays { get; set; }

    public DunningLevel Level { get; set; }
    public AgingBucketsDto Buckets { get; set; } = new();
}

/// <summary>
/// 已装载的申报表
/// </summary>
public class TaxReturnFormDto
{
    public string Country { get; set; } = string.Empty;
    public string FormCode { get; set; } = string.Empty;
}
