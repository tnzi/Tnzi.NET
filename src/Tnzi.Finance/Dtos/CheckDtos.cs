namespace Tnzi.Finance.Dtos;

/// <summary>
/// 支票记录 DTO
/// </summary>
public class BankCheckDto
{
    public Guid Id { get; set; }
    public Guid BankAccountId { get; set; }

    /// <summary>银行账户档案名称（服务层补齐）</summary>
    public string? BankAccountName { get; set; }

    public long CheckNumber { get; set; }
    public CheckStatus Status { get; set; }
    public Guid? PaymentEntryId { get; set; }

    /// <summary>关联付款单编号（服务层补齐）</summary>
    public string? PaymentNumber { get; set; }

    public string? PayeeName { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTime IssueDate { get; set; }
    public DateTime? PrintedTime { get; set; }
    public bool IsManual { get; set; }
    public string? VoidReason { get; set; }
    public Guid? ReplacedByCheckId { get; set; }
    public string ConcurrencyStamp { get; set; } = string.Empty;
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 打印队列项（待打印的 Posted Outbound 支票付款单）
/// </summary>
public class CheckQueueItemDto
{
    /// <summary>付款单ID</summary>
    public Guid PaymentEntryId { get; set; }

    /// <summary>付款单编号</summary>
    public string? PaymentNumber { get; set; }

    /// <summary>出款银行账户档案</summary>
    public Guid BankAccountId { get; set; }

    /// <summary>出款银行账户名称</summary>
    public string? BankAccountName { get; set; }

    /// <summary>收款人（供应商名称）</summary>
    public string? PayeeName { get; set; }

    public DateTime DocDate { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Memo { get; set; }
    public string? Reference { get; set; }
}

/// <summary>
/// 打印支票请求（逐张按分配号打印并合并为一份 PDF）
/// </summary>
public class PrintChecksDto
{
    /// <summary>待打印的付款单ID（须均在打印队列内且共享同一银行账户）</summary>
    public List<Guid> PaymentEntryIds { get; set; } = null!;

    /// <summary>签发日期（null 用付款单日期）</summary>
    public DateTime? IssueDate { get; set; }
}

/// <summary>
/// 登记手工支票（手写票入登记簿，显式号撞号 409）
/// </summary>
public class RegisterManualCheckDto
{
    public Guid BankAccountId { get; set; }
    public long CheckNumber { get; set; }
    public string? PayeeName { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTime IssueDate { get; set; }

    /// <summary>可选关联付款单</summary>
    public Guid? PaymentEntryId { get; set; }
}

/// <summary>
/// 作废支票
/// </summary>
public class VoidCheckDto
{
    public string? Reason { get; set; }
}

/// <summary>
/// 毁票登记（损坏/对齐失败的空白票占号留痕，可推进 NextCheckNumber）
/// </summary>
public class SpoilCheckDto
{
    public Guid BankAccountId { get; set; }
    public long CheckNumber { get; set; }
    public string? Reason { get; set; }
}

/// <summary>
/// 生成的支票文件（PDF；由服务在事务内渲染，控制器落地为 File 下载）
/// </summary>
public class CheckFileDto
{
    public string FileName { get; set; } = string.Empty;
    public byte[] Content { get; set; } = Array.Empty<byte>();
}

/// <summary>
/// 支票记录查询
/// </summary>
public class CheckQueryDto : PagedQueryDto
{
    public Guid? BankAccountId { get; set; }
    public CheckStatus? Status { get; set; }

    /// <summary>关键字（收款人/作废原因模糊匹配）</summary>
    public string? Keyword { get; set; }
}
