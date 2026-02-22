namespace Tnzi.Payment.Events;

/// <summary>
/// 发票生成事件
/// </summary>
public class InvoiceGeneratedEvent : EventBase
{
    /// <summary>
    /// 发票ID
    /// </summary>
    public Guid InvoiceId { get; set; }

    /// <summary>
    /// 发票号码
    /// </summary>
    public string InvoiceNo { get; set; } = string.Empty;

    /// <summary>
    /// 关联支付ID
    /// </summary>
    public Guid? PaymentId { get; set; }

    /// <summary>
    /// 发票金额
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// 客户邮箱
    /// </summary>
    public string CustomerEmail { get; set; } = string.Empty;

    /// <summary>
    /// 是否自动发送
    /// </summary>
    public bool AutoSent { get; set; }
}

/// <summary>
/// 发票发送事件
/// </summary>
public class InvoiceSentEvent : EventBase
{
    /// <summary>
    /// 发票ID
    /// </summary>
    public Guid InvoiceId { get; set; }

    /// <summary>
    /// 发票号码
    /// </summary>
    public string InvoiceNo { get; set; } = string.Empty;

    /// <summary>
    /// 收件人邮箱
    /// </summary>
    public string RecipientEmail { get; set; } = string.Empty;

    /// <summary>
    /// 发送时间
    /// </summary>
    public DateTime SentTime { get; set; }

    /// <summary>
    /// PDF URL
    /// </summary>
    public string? PdfUrl { get; set; }
}

/// <summary>
/// 发票支付事件
/// </summary>
public class InvoicePaidEvent : EventBase
{
    /// <summary>
    /// 发票ID
    /// </summary>
    public Guid InvoiceId { get; set; }

    /// <summary>
    /// 发票号码
    /// </summary>
    public string InvoiceNo { get; set; } = string.Empty;

    /// <summary>
    /// 支付金额
    /// </summary>
    public decimal PaidAmount { get; set; }

    /// <summary>
    /// 支付时间
    /// </summary>
    public DateTime PaidTime { get; set; }

    /// <summary>
    /// 关联支付ID
    /// </summary>
    public Guid? PaymentId { get; set; }
}
