using System.Net.Mime;

namespace Tnzi.Notification.Services;

/// <summary>
/// 把「一个号码 + 一份 PDF」变成一封 email-to-fax 网关能收的邮件，并在此处把网关的硬约束验掉。
/// </summary>
/// <remarks>
/// <para>
/// 全是纯函数，一律返回新的 <see cref="EmailMessage"/> / <see cref="EmailAttachment"/>，
/// 绝不改动传入的对象 —— 与 <see cref="EmailEnvelope"/> 同一条理由：调用方可能在重试时复用同一份文档。
/// </para>
/// <para>
/// ★ <b>约束验在这里而不是发送时</b>：网关对不合规的传真的反应是"收下，然后什么也不发生"，
/// 所以一旦交出去就再没有人能告诉你哪里错了。三条硬约束（恰好一个 PDF、正文为空、号码不带前导 1）
/// 全部在把邮件交给 <see cref="IEmailSender"/> 之前判完。
/// </para>
/// </remarks>
internal static class FaxEnvelope
{
    /// <summary>PDF 的魔数。</summary>
    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    /// <summary>
    /// 找魔数时最多往后看多少字节。PDF 阅读器容忍文件头前有少量垃圾字节，这里照同一口径。
    /// </summary>
    private const int PdfMagicSearchWindow = 1024;

    private const string PdfExtension = ".pdf";

    /// <summary>
    /// 组装发往网关的那封邮件。
    /// </summary>
    /// <param name="faxNumber">人写的传真号码。</param>
    /// <param name="gatewayDomain">网关域名，可带前导 <c>@</c>。</param>
    /// <param name="document">要发的文档，必须是 PDF。</param>
    /// <param name="subject">可选主题。</param>
    /// <param name="message">组装好的邮件；失败时为 <c>null</c>。</param>
    /// <param name="error">失败原因（面向开发者，英文）；成功时为 <c>null</c>。</param>
    internal static bool TryBuild(
        string faxNumber,
        string gatewayDomain,
        EmailAttachment document,
        string? subject,
        out EmailMessage? message,
        out string? error)
    {
        message = null;

        if (!FaxNumber.TryNormalize(faxNumber, out var digits, out error))
            return false;

        if (string.IsNullOrWhiteSpace(gatewayDomain))
        {
            error = "Fax gateway domain is not configured; set Notification:FaxSender:GatewayDomain.";
            return false;
        }

        if (!TryNormalizeDocument(document, out var attachment, out error))
            return false;

        message = new EmailMessage
        {
            To = [new EmailAddress($"{digits}@{gatewayDomain.Trim().TrimStart('@')}")],
            Subject = subject ?? string.Empty,

            // ★ 正文必须为空。网关的说明书原文要求正文里不得有任何文字、图片或签名档 ——
            // 多出来的任何一样都会让这份传真变成"网关收下了，然后什么也没发生"。
            // 这里用空串而不是"发一句提示"：MimeKit 对空正文根本不建 body part，
            // 于是发出去的信里除了那个 PDF 什么都没有，签名档也就无从附加。
            Body = string.Empty,
            IsHtml = false,
            Attachments = [attachment!]
        };

        error = null;
        return true;
    }

    /// <summary>
    /// 校验并归一化那份文档：必须是 PDF，文件名带 <c>.pdf</c>，MIME 报 <c>application/pdf</c>。
    /// </summary>
    /// <remarks>
    /// 归一化而不是"照调用方给的发"：网关有的看 MIME、有的看扩展名，调用方给了
    /// <c>application/octet-stream</c> + <c>letter.pdf</c> 时两边说法不一致，
    /// 而不一致的那一次不会报错，只会不到达。
    /// </remarks>
    private static bool TryNormalizeDocument(EmailAttachment document, out EmailAttachment? attachment, out string? error)
    {
        attachment = null;

        if (document == null)
        {
            error = "A fax needs exactly one PDF document; none was given.";
            return false;
        }

        if (document.Content == null && string.IsNullOrWhiteSpace(document.FilePath))
        {
            error = $"Fax document '{document.FileName}' has neither content nor a file path.";
            return false;
        }

        // 没有文件名时下面补扩展名会得到一个叫 ".pdf" 的附件 —— 网关多半照收，
        // 但那不是任何人想寄出去的东西（有的网关会把文件名印在封面或回执上）。
        // 这是调用方漏填，说出来比替他编一个名字好。
        if (string.IsNullOrWhiteSpace(document.FileName))
        {
            error = "Fax document has no file name; give it one (for example 'letter.pdf').";
            return false;
        }

        var declared = MediaType(document.ContentType);
        var looksLikePdf = document.FileName.EndsWith(PdfExtension, StringComparison.OrdinalIgnoreCase);

        if (declared.Length > 0
            && !declared.Equals(MediaTypeNames.Application.Pdf, StringComparison.OrdinalIgnoreCase)
            && !declared.Equals(MediaTypeNames.Application.Octet, StringComparison.OrdinalIgnoreCase))
        {
            error = $"Only PDF can be sent by fax; document '{document.FileName}' is declared as '{document.ContentType}'.";
            return false;
        }

        if (!looksLikePdf && !declared.Equals(MediaTypeNames.Application.Pdf, StringComparison.OrdinalIgnoreCase))
        {
            error = $"Only PDF can be sent by fax; document '{document.FileName}' is not a .pdf. " +
                    "Convert it first (IDocumentConverter) and combine multiple documents into one PDF (IPdfCombiner).";
            return false;
        }

        // 字节在手就顺便看一眼魔数：文件名与 MIME 都是调用方说的，只有这一项是文档自己说的。
        // 抓的是"把 Word 另存成 letter.pdf"这一类 —— 网关照收，然后不到达。
        if (document.Content != null && !LooksLikePdfBytes(document.Content))
        {
            error = $"Fax document '{document.FileName}' is named like a PDF but its bytes are not a PDF.";
            return false;
        }

        attachment = new EmailAttachment
        {
            FileName = looksLikePdf ? document.FileName : document.FileName + PdfExtension,
            Content = document.Content,
            FilePath = document.FilePath,
            ContentType = MediaTypeNames.Application.Pdf
        };

        error = null;
        return true;
    }

    /// <summary>取 MIME 的主体部分（丢掉 <c>; charset=…</c> 这类参数）。</summary>
    private static string MediaType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return string.Empty;

        var separator = contentType.IndexOf(';');
        return (separator < 0 ? contentType : contentType[..separator]).Trim();
    }

    private static bool LooksLikePdfBytes(byte[] content)
    {
        var limit = Math.Min(content.Length, PdfMagicSearchWindow) - PdfMagic.Length;
        for (var offset = 0; offset <= limit; offset++)
        {
            if (content.AsSpan(offset, PdfMagic.Length).SequenceEqual(PdfMagic))
                return true;
        }

        return false;
    }
}
