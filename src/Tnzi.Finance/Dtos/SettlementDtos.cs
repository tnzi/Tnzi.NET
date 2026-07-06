namespace Tnzi.Finance.Dtos;

/// <summary>
/// 核销记录 DTO
/// </summary>
public class PaymentApplicationDto
{
    public Guid Id { get; set; }
    public SettlementDocType SourceType { get; set; }
    public Guid SourceId { get; set; }
    public string? SourceNumber { get; set; }
    public SettlementDocType TargetType { get; set; }
    public Guid TargetId { get; set; }
    public string? TargetNumber { get; set; }
    public decimal AppliedAmount { get; set; }
    public Guid? RealizedFxJournalEntryId { get; set; }
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 核销请求（一个源分配到多个目标）
/// </summary>
public class ApplySettlementDto
{
    /// <summary>核销源类型（PaymentEntry / CreditMemo）</summary>
    public SettlementDocType SourceType { get; set; }

    /// <summary>核销源ID</summary>
    public Guid SourceId { get; set; }

    /// <summary>目标分配</summary>
    public List<ApplySettlementTargetDto> Targets { get; set; } = null!;
}

/// <summary>
/// 核销目标分配
/// </summary>
public class ApplySettlementTargetDto
{
    /// <summary>目标类型（Invoice / Bill）</summary>
    public SettlementDocType TargetType { get; set; }

    /// <summary>目标ID</summary>
    public Guid TargetId { get; set; }

    /// <summary>核销金额（交易币）</summary>
    public decimal Amount { get; set; }
}

/// <summary>
/// 可核销的未清单据 DTO
/// </summary>
public class OpenDocumentDto
{
    public SettlementDocType DocType { get; set; }
    public Guid DocId { get; set; }
    public string? Number { get; set; }
    public DateTime DocDate { get; set; }
    public DateTime? DueDate { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public decimal AppliedTotal { get; set; }

    /// <summary>未清金额（交易币）</summary>
    public decimal Outstanding => Total - AppliedTotal;
}

/// <summary>
/// 外部收款摄取请求（幂等：SourceType + SourceId 唯一）
/// </summary>
public class ExternalPaymentIngestDto
{
    /// <summary>外部来源类型（如 "Payment.Order"）</summary>
    public string SourceType { get; set; } = null!;

    /// <summary>外部来源ID</summary>
    public string SourceId { get; set; } = null!;

    /// <summary>客户ID</summary>
    public Guid CustomerId { get; set; }

    public DateTime DocDate { get; set; }
    public decimal Amount { get; set; }

    /// <summary>交易币种（null 表示本位币）</summary>
    public string? Currency { get; set; }

    /// <summary>汇率（null 时过账按汇率表解析）</summary>
    public decimal? ExchangeRate { get; set; }

    /// <summary>存入科目（null 回退 PostToUndepositedFunds 设置）</summary>
    public Guid? DepositToAccountId { get; set; }

    public string? Reference { get; set; }
    public string? Memo { get; set; }

    /// <summary>是否立即过账（默认 true）</summary>
    public bool AutoPost { get; set; } = true;
}

// ── Aging 报表 ───────────────────────────────────────────────

/// <summary>
/// 账龄报表 DTO（AR/AP 通用；金额为本位币估算 = 未清交易币 × 捕获汇率）
/// </summary>
public class AgingReportDto
{
    public DateTime AsOf { get; set; }
    public string BaseCurrency { get; set; } = string.Empty;
    public List<AgingRowDto> Rows { get; set; } = new();
    public AgingBucketsDto Totals { get; set; } = new();
}

/// <summary>
/// 账龄行（按往来方分组）
/// </summary>
public class AgingRowDto : AgingBucketsDto
{
    public Guid PartyId { get; set; }
    public string PartyName { get; set; } = string.Empty;
}

/// <summary>
/// 账龄桶（Current / 1-30 / 31-60 / 61-90 / 90+，按到期日与 asOf 的逾期天数）
/// </summary>
public class AgingBucketsDto
{
    public decimal Current { get; set; }
    public decimal Days1To30 { get; set; }
    public decimal Days31To60 { get; set; }
    public decimal Days61To90 { get; set; }
    public decimal Over90 { get; set; }
    public decimal Total { get; set; }
}
