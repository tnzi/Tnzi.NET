namespace Tnzi.Notification.Services;

/// <summary>
/// 回执收件箱：把网关回来的那些邮件取出来。
/// </summary>
/// <remarks>
/// <para>
/// 默认实现 <see cref="ImapFaxConfirmationMailbox"/> 走 IMAP，只取未读、处理完标记已读 ——
/// 无需另建一张"处理过哪些邮件"的表，重启也不会重复处理。
/// </para>
/// <para>
/// ★ <b>只有配了收件箱账号才会被注册</b>。没配就是这个部署不收回执，整条链一个后台线程都不起。
/// 这一条与 <see cref="IFaxSender"/> 的取舍相反是有理由的：发不出去的传真必须当场报错，
/// 而收不收回执是一个附加能力，没有它一切照旧。
/// </para>
/// </remarks>
public interface IFaxConfirmationMailbox
{
    /// <summary>
    /// 取一批待判读的邮件，并把它们标记为已处理（IMAP 实现里是标已读）。
    /// </summary>
    /// <param name="maxMessages">这一轮最多取多少封。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IReadOnlyList<FaxConfirmationMessage>> FetchAsync(int maxMessages, CancellationToken cancellationToken = default);
}
