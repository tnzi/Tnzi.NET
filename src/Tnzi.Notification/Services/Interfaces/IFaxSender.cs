using System.Net.Mime;

namespace Tnzi.Notification.Services;

/// <summary>
/// 传真发送服务接口。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="IEmailSender"/> / <see cref="ISmsSender"/> / <see cref="IPushSender"/> 并列的第四条渠道。
/// 商用传真今天基本都是 email-to-fax 网关（把号码拼成邮箱地址发过去），所以默认实现
/// <see cref="EmailToFaxSender"/> 架在 <see cref="IEmailSender"/> 之上 —— 已经配了 SMTP 的部署不需要任何新设施。
/// </para>
/// <para>
/// ★ <b>网关的约束由本契约兜住，不交给调用方</b>：恰好一个 PDF 附件、正文为空、号码不带北美长途前缀。
/// 三条都属于"错了没有症状"那一类 —— 网关照收，然后那份传真不会到达。所以签名只收
/// <b>一个</b>文档（多份要合并成一个 PDF 再来，用 <c>Tnzi.Documents</c> 的 <c>IPdfCombiner</c>），
/// 正文根本不出现在参数表里（发不出去正文，也就无从"顺手带一句"）。
/// </para>
/// <para>
/// <b>发送与投递确认是分开的两件事。</b>本接口只负责把传真交给网关；网关几分钟后回的那封确认邮件
/// 由 <see cref="IFaxConfirmationService"/> 那条链处理（<b>可选</b>，收件箱不配就整条不启用）。
/// ★ <see cref="SendResult.ExternalMessageId"/> 里给的是承载这次传真的那封邮件**真正的
/// <c>Message-ID</c>**（信头里的那一个，随信发出去），回执正是靠它的 <c>In-Reply-To</c> 对上号 ——
/// 换成任何一个自造的追踪号，这条链就永远对不上，而且只有真去接回执时才会发现。
/// </para>
/// </remarks>
public interface IFaxSender
{
    /// <summary>
    /// 把一份文档发到一个传真号码。
    /// </summary>
    /// <param name="faxNumber">传真号码，可带 <c>+ - ( ) . /</c> 与空白；归一化规则见 <see cref="FaxNumber"/>。</param>
    /// <param name="document">
    /// 要发的文档，必须是 PDF。用 <see cref="EmailAttachment"/> 而不是另造一个传真专用类型：
    /// 它本来就是本模块"一份待投递的文件"的形态（字节 / 本地路径 / URL 三选一），再造一个只会多一层转换。
    /// </param>
    /// <param name="subject">
    /// 可选的主题，通常用作对账参考号。<b>部分网关会把它印在封面页上</b>，不确定就别传。
    /// </param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>与其它三条渠道同形的 <see cref="SendResult"/>；号码或文档不合规时是失败而不是静默丢弃。</returns>
    Task<SendResult> SendToAsync(string faxNumber, EmailAttachment document, string? subject = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 直接用内存里的 PDF 字节发一份传真。
    /// </summary>
    /// <remarks>
    /// 默认接口方法，只是把字节包成 <see cref="EmailAttachment"/> 再走 <see cref="SendToAsync"/> ——
    /// 实现者不必也不该重写它，否则两条路径会在"约束怎么校验"上各自漂移。
    /// </remarks>
    /// <param name="faxNumber">传真号码。</param>
    /// <param name="pdf">PDF 字节（多份文档先合并成一份）。</param>
    /// <param name="fileName">附件文件名，例如 <c>letter.pdf</c>。</param>
    /// <param name="subject">可选的主题。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<SendResult> SendPdfAsync(string faxNumber, byte[] pdf, string fileName, string? subject = null, CancellationToken cancellationToken = default)
    {
        return SendToAsync(faxNumber, EmailAttachment.FromBytes(pdf, fileName, MediaTypeNames.Application.Pdf), subject, cancellationToken);
    }
}
