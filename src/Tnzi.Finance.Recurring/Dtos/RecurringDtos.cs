namespace Tnzi.Finance.Recurring.Dtos;

/// <summary>
/// 周期性单据模板
/// </summary>
public class RecurringDocumentDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public RecurringDocKind Kind { get; set; }
    public RecurringStatus Status { get; set; }

    public Guid PartyId { get; set; }

    /// <summary>往来方名称（客户或供应商，服务端解析）</summary>
    public string? PartyName { get; set; }

    public Guid? PaidFromAccountId { get; set; }
    public string? Currency { get; set; }
    public string? PaymentMethod { get; set; }
    public string? Memo { get; set; }

    public RecurrenceFrequency Frequency { get; set; }
    public int Interval { get; set; }
    public int? AnchorDay { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int? MaxOccurrences { get; set; }
    public int? DueDays { get; set; }

    /// <summary>null = 跟随全局默认</summary>
    public bool? AutoPost { get; set; }

    /// <summary>本模板生效中的过账行为（已把全局默认解析进来，供呈现端直接显示）</summary>
    public bool EffectiveAutoPost { get; set; }

    public DateTime NextRunDate { get; set; }
    public DateTime? LastRunDate { get; set; }
    public int OccurrenceCount { get; set; }

    /// <summary>行金额合计（不含税，按模板行现价估算）</summary>
    public decimal EstimatedTotal { get; set; }

    public List<RecurringLineDto> Lines { get; set; } = [];
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

/// <summary>
/// 模板行
/// </summary>
public class RecurringLineDto
{
    public Guid Id { get; set; }
    public int LineNumber { get; set; }
    public Guid? ItemId { get; set; }
    public string? Description { get; set; }
    public Guid? AccountId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public Guid? TaxCodeId { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>
/// 创建模板
/// </summary>
public class CreateRecurringDocumentDto
{
    public string Name { get; set; } = null!;
    public RecurringDocKind Kind { get; set; } = RecurringDocKind.Invoice;
    public Guid PartyId { get; set; }
    public Guid? PaidFromAccountId { get; set; }
    public string? Currency { get; set; }
    public string? PaymentMethod { get; set; }
    public string? Memo { get; set; }

    public RecurrenceFrequency Frequency { get; set; } = RecurrenceFrequency.Monthly;
    public int Interval { get; set; } = 1;
    public int? AnchorDay { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int? MaxOccurrences { get; set; }
    public int? DueDays { get; set; }
    public bool? AutoPost { get; set; }

    public List<CreateRecurringLineDto> Lines { get; set; } = null!;
}

/// <summary>
/// 模板行请求
/// </summary>
public class CreateRecurringLineDto
{
    public Guid? ItemId { get; set; }
    public string? Description { get; set; }
    public Guid? AccountId { get; set; }
    public decimal Quantity { get; set; } = 1m;
    public decimal UnitPrice { get; set; }
    public Guid? TaxCodeId { get; set; }
}

/// <summary>
/// 更新模板
/// </summary>
/// <remarks>
/// <see cref="RecurringDocument.Kind"/> 不在此列：改生成什么单据等于换一个模板，而已生成的历史会
/// 指向另一种单据类型 —— 停用旧的、建一个新的，比让一条模板前后不是同一件东西干净。
/// </remarks>
public class UpdateRecurringDocumentDto
{
    public string Name { get; set; } = null!;
    public Guid PartyId { get; set; }
    public Guid? PaidFromAccountId { get; set; }
    public string? Currency { get; set; }
    public string? PaymentMethod { get; set; }
    public string? Memo { get; set; }

    public RecurrenceFrequency Frequency { get; set; }
    public int Interval { get; set; } = 1;
    public int? AnchorDay { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int? MaxOccurrences { get; set; }
    public int? DueDays { get; set; }
    public bool? AutoPost { get; set; }

    public List<CreateRecurringLineDto> Lines { get; set; } = null!;
    public string ConcurrencyStamp { get; set; } = null!;
}

/// <summary>
/// 模板查询
/// </summary>
public class RecurringDocumentQueryDto : PagedQueryDto
{
    public string? Keyword { get; set; }
    public RecurringDocKind? Kind { get; set; }
    public RecurringStatus? Status { get; set; }
    public Guid? PartyId { get; set; }

    /// <summary>只看这个日期之前到期的（排期工作台的"该跑了"）</summary>
    public DateTime? DueBefore { get; set; }
}

/// <summary>
/// 一次生成的记录
/// </summary>
public class RecurringRunDto
{
    public Guid Id { get; set; }
    public Guid RecurringDocumentId { get; set; }
    public string? RecurringDocumentName { get; set; }
    public DateTime PeriodDate { get; set; }
    public RecurringRunStatus Status { get; set; }
    public string? DocType { get; set; }
    public Guid? DocId { get; set; }
    public string? DocNumber { get; set; }
    public bool Posted { get; set; }
    public string? FailReason { get; set; }
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 生成记录查询
/// </summary>
public class RecurringRunQueryDto : PagedQueryDto
{
    public Guid? RecurringDocumentId { get; set; }
    public RecurringRunStatus? Status { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

/// <summary>
/// 排期预览：接下来几期分别落在哪天
/// </summary>
/// <remarks>
/// 存在的理由很实际：锚点 31 号、每季度、跳过二月这些规则在脑子里算不清楚，
/// 而算错的代价是给客户在错误的日子开票。让人**先看见日期再保存**。
/// </remarks>
public class RecurrencePreviewDto
{
    public List<DateTime> Dates { get; set; } = [];
}

/// <summary>
/// 到期扫描结果
/// </summary>
public class RecurringSweepResultDto
{
    /// <summary>扫到的到期模板数</summary>
    public int TemplatesDue { get; set; }

    /// <summary>成功生成的单据数</summary>
    public int Generated { get; set; }

    /// <summary>按补齐策略跳过的期次数</summary>
    public int Skipped { get; set; }

    /// <summary>失败的期次数（各自留有记录，下次重试）</summary>
    public int Failed { get; set; }

    /// <summary>本次生成的记录明细</summary>
    public List<RecurringRunDto> Runs { get; set; } = [];
}
