namespace Tnzi.Payment.Billing.Events;

/// <summary>
/// 发票生成事件：一张发票落库（从支付自动开出或手工开出）。
/// </summary>
/// <remarks>
/// 发出与否看 <see cref="InvoiceSentEvent"/>。此前事件类在这里，发布方一个都没有，订阅它的消费方永远收不到。
/// </remarks>
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
    /// 应付总额（<c>Invoice.DueAmount</c>：原价 − 折扣 + 税额）
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// 客户邮箱
    /// </summary>
    public string CustomerEmail { get; set; } = string.Empty;
}

/// <summary>
/// 发票发送事件：发票作为附件成功交给了通知模块（自动发送、管理端补发、用户自己重发都算）。
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
    /// 发票文件在 Storage 里的下载地址（站内、按会话鉴权；未加载 Storage 时为 null）
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
