namespace Tnzi.Payment.Billing.Metadata;

/// <summary>
/// 发送票据所用的邮件模板名（<c>Templates/Notification/Email/</c> 下，随本模块复制到输出目录，部署可按名覆盖）。
/// 模板渲染不出来时退回内置的一句纯文本。
/// </summary>
public static class InvoiceEmailTemplates
{
    /// <summary>发票。</summary>
    public const string Invoice = "InvoiceIssued";

    /// <summary>收据。</summary>
    public const string Receipt = "ReceiptIssued";

    /// <summary>贷项通知单。</summary>
    public const string CreditNote = "CreditNoteIssued";
}
