namespace Tnzi.Notification.Services;

/// <summary>
/// 发送前把已退订的收件人从待发列表里择出去的判定（纯函数，便于单测）。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>这是本模块此前完全没接上的一环</b>：退订表、一键退订令牌、
/// <see cref="INotificationOptOutService.FilterAllowedAsync"/>（其文档白纸黑字写着
/// "群发前应当调这个"）全都建好了，但<b>发送路径从不询问它们</b>。于是收件人点了退订、
/// 拿到 200、库里多了一行，然后照收不误 —— 这比没有退订功能更糟，
/// 因为它是一个会被当真的承诺。
/// </para>
/// <para>
/// <b>判定放在发送那一刻</b>而不是创建那一刻：定时与排队的消息可能几天后才发出去，
/// 退订随时可能发生在这中间，最后一刻才是唯一正确的时刻。
/// </para>
/// </remarks>
internal static class OptOutRecipientFilter
{
    /// <summary>被退订拦下时写进 <c>Recipient.FailureReason</c> 的说明。</summary>
    internal const string OptedOutReason = "Recipient has opted out of this channel/category";

    /// <summary>这一批到底要不要去问退订名单。</summary>
    /// <remarks>
    /// 事务性消息不受退订约束：退订按钮管的是营销邮件，不该让人再也收不到验证码或密码重置链接。
    /// 空批次也不必问 —— 省一次无谓的往返。
    /// </remarks>
    internal static bool ShouldConsultOptOutList(Message notification, int candidateCount)
    {
        Check.NotNull(notification);
        return !notification.IsTransactional && candidateCount > 0;
    }

    /// <summary>
    /// 按放行名单剔除收件人；被剔除者<b>就地</b>标记为
    /// <see cref="NotificationStatus.Cancelled"/> 并写明原因。返回仍应当发送的那些。
    /// </summary>
    /// <param name="candidates">本轮待发的收件人。</param>
    /// <param name="allowedAddresses">
    /// <see cref="INotificationOptOutService.FilterAllowedAsync"/> 返回的放行地址。
    /// 它会归一化地址（大小写 / 空白），所以这里按不区分大小写的集合反查，
    /// 而不是拿原始字符串做等值比较。
    /// </param>
    /// <remarks>
    /// ★ 被剔除的人标 <c>Cancelled</c> 并留下原因，而<b>不是</b>从列表里抹掉 ——
    /// 合规场景要的是"证明没有发过"，删掉记录恰恰证明不了。
    /// ★★ 刻意<b>不</b>标 <c>Failed</c>：那会让 <c>ResendToFailedRecipientsAsync</c>
    /// 把已退订的地址重新捞出来再发一遍，等于开了一条绕过退订的后门。
    /// </remarks>
    internal static List<Recipient> Apply(List<Recipient> candidates, IEnumerable<string> allowedAddresses)
    {
        Check.NotNull(candidates);
        Check.NotNull(allowedAddresses);

        var allowed = new HashSet<string>(allowedAddresses, StringComparer.OrdinalIgnoreCase);
        if (allowed.Count >= candidates.Count && candidates.TrueForAll(r => allowed.Contains(r.Address)))
            return candidates;

        var remaining = new List<Recipient>(candidates.Count);
        foreach (var recipient in candidates)
        {
            if (allowed.Contains(recipient.Address))
            {
                remaining.Add(recipient);
                continue;
            }

            recipient.Status = NotificationStatus.Cancelled;
            recipient.FailureReason = OptedOutReason;
        }

        return remaining;
    }
}
