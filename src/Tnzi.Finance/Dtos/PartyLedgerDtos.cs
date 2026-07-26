namespace Tnzi.Finance.Dtos;

/// <summary>
/// 往来方工作面的概览数字：这个人现在欠我们多少、逾期多少、这期做了多少生意。
/// </summary>
/// <remarks>
/// <b>为什么要一个专门的端点</b>：呈现端拿分页列表自己求和是**错的** —— 它只能加总当前这一页，
/// 而"未清余额"必须是全量口径且要与总账 AR/AP 控制科目对得上。未清与逾期直接复用账龄的同一段
/// 计算（时点已核销额按 <c>PaymentApplication.CreationTime &lt;= asOf</c> 重建，含未核销收付款与
/// 贷项的负行），因此**客户页显示的余额与账龄报表逐分相等**，不会出现两个屏幕两个数字。
/// </remarks>
public class PartyLedgerSummaryDto
{
    public Guid PartyId { get; set; }
    public string PartyName { get; set; } = string.Empty;
    public FinancePartyType PartyType { get; set; }

    /// <summary>本位币（概览数字一律折本位币，口径与账龄一致）。</summary>
    public string BaseCurrency { get; set; } = string.Empty;

    /// <summary>未清余额（正=对方欠我们 / 我们欠对方，取决于 <see cref="PartyType"/>）。</summary>
    public decimal OpenBalance { get; set; }

    /// <summary>其中已逾期的部分（账龄的非 Current 桶之和）。</summary>
    public decimal Overdue { get; set; }

    /// <summary>账龄分桶（与账龄报表同一口径）。</summary>
    public AgingBucketsDto Buckets { get; set; } = new();

    /// <summary>期间发生额：客户为销售额，供应商为采购额（已过账单据的本位币合计）。</summary>
    public decimal PeriodTotal { get; set; }

    /// <summary>期间起（含）。</summary>
    public DateTime PeriodFrom { get; set; }

    /// <summary>期间止（含）。</summary>
    public DateTime PeriodTo { get; set; }

    /// <summary>未清单据张数。</summary>
    public int OpenDocumentCount { get; set; }

    /// <summary>最近一笔交易的单据日期（从未有过为 null）。</summary>
    public DateTime? LastTransactionDate { get; set; }
}

/// <summary>
/// 往来方交易流水的一行（跨单据类型的统一视图）。
/// </summary>
/// <remarks>
/// 把发票 / 贷项 / 收付款（或账单 / 费用 / 付款）铺成一条按日期排列的流水，是"这个客户到底
/// 发生了什么"最直接的回答。<see cref="Outstanding"/> 仅对可核销的单据（发票 / 账单）有意义，
/// 其余为 0 —— 呈现端据此决定要不要显示未清列。
/// </remarks>
public class PartyLedgerEntryDto
{
    /// <summary>来源令牌（<see cref="Metadata.FinanceSourceTypes"/>），呈现端据此决定跳转到哪个单据页。</summary>
    public string DocType { get; set; } = string.Empty;

    public Guid DocId { get; set; }
    public string? Number { get; set; }
    public DateTime DocDate { get; set; }
    public DateTime? DueDate { get; set; }
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// 单据金额（交易币）。
    /// </summary>
    /// <remarks>
    /// <b>带符号</b>：增加对方欠款的为正（发票/账单），减少的为负（收付款/贷项）。
    /// 这样一条流水直接读得出方向，呈现端不必按 DocType 分支去猜正负。
    /// </remarks>
    public decimal Amount { get; set; }

    /// <summary>未清金额（仅发票/账单，其余为 0）。</summary>
    public decimal Outstanding { get; set; }

    public FinanceDocumentStatus Status { get; set; }

    /// <summary>逾期天数（未清且已过到期日时为正，否则为 0）。</summary>
    public int OverdueDays { get; set; }
}

/// <summary>往来方交易流水查询。</summary>
public class PartyLedgerQueryDto : PagedQueryDto
{
    /// <summary>起始单据日期（含）。</summary>
    public DateTime? From { get; set; }

    /// <summary>截止单据日期（含）。</summary>
    public DateTime? To { get; set; }

    /// <summary>只看未清（发票/账单里还没付完的）。</summary>
    public bool OpenOnly { get; set; }
}
