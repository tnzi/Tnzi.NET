namespace Tnzi.Finance.Banking.Events;

/// <summary>
/// 银行对账单已导入事件
/// </summary>
public class BankStatementImportedEvent : EventBase
{
    /// <summary>导入批次</summary>
    public Guid BatchId { get; set; }

    /// <summary>银行科目</summary>
    public Guid AccountId { get; set; }

    /// <summary>来源</summary>
    public BankTransactionSource Source { get; set; }

    /// <summary>成功导入行数</summary>
    public int ImportedCount { get; set; }

    /// <summary>去重跳过行数</summary>
    public int SkippedCount { get; set; }
}

/// <summary>
/// 银行流水已匹配事件（确认生成对账勾选行）
/// </summary>
public class BankTransactionMatchedEvent : EventBase
{
    /// <summary>银行流水行</summary>
    public Guid BankTransactionId { get; set; }

    /// <summary>银行科目</summary>
    public Guid AccountId { get; set; }

    /// <summary>匹配到的总账行</summary>
    public Guid JournalLineId { get; set; }

    /// <summary>生成的对账勾选行</summary>
    public Guid ReconciliationLineId { get; set; }
}

/// <summary>
/// 支票已开具事件（打印/登记/重打生成新票）
/// </summary>
public class CheckIssuedEvent : EventBase
{
    /// <summary>支票记录</summary>
    public Guid CheckId { get; set; }

    /// <summary>银行账户档案</summary>
    public Guid BankAccountId { get; set; }

    /// <summary>支票号</summary>
    public long CheckNumber { get; set; }

    /// <summary>关联付款单（毁票为 null）</summary>
    public Guid? PaymentEntryId { get; set; }
}

/// <summary>
/// 支票已作废事件（人工作废 / 付款作废联动 / 重打作废原票）
/// </summary>
public class CheckVoidedEvent : EventBase
{
    /// <summary>支票记录</summary>
    public Guid CheckId { get; set; }

    /// <summary>银行账户档案</summary>
    public Guid BankAccountId { get; set; }

    /// <summary>支票号</summary>
    public long CheckNumber { get; set; }

    /// <summary>作废原因</summary>
    public string? Reason { get; set; }
}

/// <summary>
/// EFT 批次已生成事件（文件已固化加密）
/// </summary>
public class EftBatchGeneratedEvent : EventBase
{
    /// <summary>批次</summary>
    public Guid BatchId { get; set; }

    /// <summary>批次编号</summary>
    public string? Number { get; set; }

    /// <summary>出款银行账户档案</summary>
    public Guid BankAccountId { get; set; }

    /// <summary>文件格式</summary>
    public EftFileFormat Format { get; set; }

    /// <summary>笔数</summary>
    public int TotalCount { get; set; }

    /// <summary>总金额</summary>
    public decimal TotalAmount { get; set; }
}

/// <summary>
/// 收据已提取事件
/// </summary>
public class ReceiptExtractedEvent : EventBase
{
    /// <summary>收据记录</summary>
    public Guid ReceiptId { get; set; }

    /// <summary>供应商名称（提取结果）</summary>
    public string? VendorName { get; set; }

    /// <summary>合计（提取结果）</summary>
    public decimal? Total { get; set; }

    /// <summary>提取置信度</summary>
    public decimal? Confidence { get; set; }
}

/// <summary>
/// 收据已转换为单据草稿事件
/// </summary>
public class ReceiptConvertedEvent : EventBase
{
    /// <summary>收据记录</summary>
    public Guid ReceiptId { get; set; }

    /// <summary>目标单据类型（实体名）</summary>
    public string DocType { get; set; } = string.Empty;

    /// <summary>目标单据ID</summary>
    public Guid DocId { get; set; }
}
