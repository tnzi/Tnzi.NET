using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;

namespace Tnzi.Notification.Services;

/// <summary>
/// 默认的回执收件箱：IMAP，只取未读，处理完标已读。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>"未读"就是全部的去重机制</b>：不必另建一张"处理过哪些邮件"的表，进程重启也不会重来一遍。
/// 代价是这个邮箱最好专用 —— 与人共用会互相把对方的信标掉。
/// </para>
/// <para>
/// <b>每轮连一次、断一次</b>，不留长连接。回执是几分钟一轮的低频活，
/// 而一条挂了几小时的 IMAP 连接会被中间设备静默掐断，症状是"某一天起就再没收到回执"。
/// </para>
/// <para>
/// 正文只取纯文本部分：判读器要的是措辞，HTML 标签只会让关键词匹配更难。
/// </para>
/// </remarks>
public class ImapFaxConfirmationMailbox : IFaxConfirmationMailbox
{
    private readonly IOptionsMonitor<NotificationOptions> _options;
    private readonly ILogger<ImapFaxConfirmationMailbox> _logger;

    /// <summary>初始化一个 <see cref="ImapFaxConfirmationMailbox"/> 实例。</summary>
    public ImapFaxConfirmationMailbox(
        IOptionsMonitor<NotificationOptions> options,
        ILogger<ImapFaxConfirmationMailbox> logger)
    {
        _options = Check.NotNull(options);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FaxConfirmationMessage>> FetchAsync(
        int maxMessages, CancellationToken cancellationToken = default)
    {
        var confirmation = _options.CurrentValue.FaxSender?.Confirmation;
        if (confirmation is not { IsUsable: true })
        {
            // 正常情况下不会走到这里：没配就不会有人注册后台服务。
            _logger.LogDebug("Fax confirmation mailbox is not configured; nothing to fetch");
            return [];
        }

        using var client = new ImapClient();

        await client.ConnectAsync(
            confirmation.Host!,
            confirmation.Port,
            confirmation.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable,
            cancellationToken);

        // IsUsable 已经保证这两个非空（上面提前返回过），编译器看不出来。
        await client.AuthenticateAsync(confirmation.UserName!, confirmation.Password!, cancellationToken);

        var folder = await client.GetFolderAsync(confirmation.Folder, cancellationToken);
        await folder.OpenAsync(confirmation.MarkAsRead ? FolderAccess.ReadWrite : FolderAccess.ReadOnly, cancellationToken);

        var unread = await folder.SearchAsync(SearchQuery.NotSeen, cancellationToken);

        var results = new List<FaxConfirmationMessage>();
        foreach (var uid in unread.Take(maxMessages))
        {
            // ★★ 一封读不出来的信不能掀掉整批。这里少了 try 的后果不是"丢一封"而是**永久卡住**：
            // 取信失败 → 整批没被标已读 → 下一轮又从同一封畸形邮件开始 → 再失败。
            // 回执链从此停摆，而症状只是"传真的失败状态一直没更新"。
            try
            {
                var mime = await folder.GetMessageAsync(uid, cancellationToken);
                results.Add(ToConfirmationMessage(mime));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // ★ 刻意**不**标已读：读不出来可能只是这一次的网络问题，标掉就把一封本可恢复的回执
                // 永久丢了。代价是畸形邮件每轮都会再报一次错 —— 那是**看得见**的噪音，
                // 有人能去把那封信处理掉；静默丢弃则没有任何人会发现。
                _logger.LogError(ex, "Could not read message {Uid} from the fax confirmation mailbox", uid);
                continue;
            }

            if (confirmation.MarkAsRead)
            {
                await folder.AddFlagsAsync(uid, MessageFlags.Seen, silent: true, cancellationToken);
            }
        }

        await client.DisconnectAsync(true, cancellationToken);

        return results;
    }

    private static FaxConfirmationMessage ToConfirmationMessage(MimeKit.MimeMessage mime)
    {
        return new FaxConfirmationMessage
        {
            Subject = mime.Subject,
            Body = mime.TextBody ?? mime.HtmlBody,
            From = mime.From.ToString(),
            InReplyTo = mime.InReplyTo,
            References = [.. mime.References],

            // 收到时间用信头里的日期。服务器时间与本机时间不一致时，按号码对号的那个
            // 时间窗口会整体偏移 —— 用信自己带的时间，窗口才和"这封回执什么时候产生的"对齐。
            ReceivedAt = mime.Date
        };
    }
}
