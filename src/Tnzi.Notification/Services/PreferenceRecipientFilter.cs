namespace Tnzi.Notification.Services;

/// <summary>
/// 发送前把「该渠道已被本人关掉」的收件人择出去的判定（纯函数，便于单测）。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>这是本模块第二条建好了却没接上的链</b>：偏好表、
/// <see cref="INotificationPreferenceService.IsChannelEnabledAsync"/>、用户端与管理端两套
/// 端点全都在，但 <c>IsChannelEnabledAsync</c> <b>全仓零调用方</b> —— 用户在设置里把邮件
/// 通知关掉、界面显示已关闭，然后照收不误。与 2026-08-08 修掉的退订是同一形态。
/// </para>
/// <para>
/// <b>为什么此前判定为「不能接」，以及为什么现在能接了</b>：接上偏好的前置条件是一个
/// 「关键消息豁免」模型 —— 少了它，一个关掉邮件通道的用户会收不到密码重置，也就是
/// 再也登不进来。那个模型在 2026-08-08 随退订一起落地了（<see cref="Message.IsTransactional"/>），
/// 所以这里复用同一个豁免，判据与退订逐字一致。
/// </para>
/// <para>
/// ★★ <b>与退订的关键差别：偏好按<u>人</u>，退订按<u>地址</u></b>。群发收件人未必是注册用户
/// （客户名单、导入联系人、已注销账号），他们没有、也不可能有偏好行 ——
/// 所以 <see cref="Recipient.UserId"/> 为空的收件人<b>原样放行</b>，不受本过滤影响。
/// 把「查不到偏好」当成「已关闭」会让整份导入名单一条都发不出去。
/// </para>
/// <para>
/// ★ <b>渠道词汇刻意比 <see cref="NotificationType"/> 宽</b>（前端契约注明
/// <c>Email, Sms, InApp, Webhook</c>，后两者对应尚未实现的渠道）。因此匹配的是
/// 「当前正在发的这个渠道」而不是校验偏好行的合法性：一行 <c>InApp</c> 偏好对一封邮件
/// 不适用<b>是正确的</b>，不是匹配失败。
/// </para>
/// </remarks>
internal static class PreferenceRecipientFilter
{
    /// <summary>被本人关闭该渠道而拦下时写进 <c>Recipient.FailureReason</c> 的说明。</summary>
    internal const string DisabledByPreferenceReason = "Recipient disabled this channel/category in their notification preferences";

    /// <summary>这一批到底要不要去问偏好表。</summary>
    /// <remarks>
    /// 三个条件缺一不问：①事务性消息不受偏好约束（判据与退订逐字一致 —— 关掉营销邮件的人
    /// 不该因此收不到验证码）；②空批次省一次往返；③一个有 <c>UserId</c> 的收件人都没有时
    /// 无从谈偏好（纯外部地址的群发）。
    /// </remarks>
    internal static bool ShouldConsultPreferences(Message notification, List<Recipient> candidates)
    {
        Check.NotNull(notification);
        Check.NotNull(candidates);

        return !notification.IsTransactional
            && candidates.Count > 0
            && candidates.Exists(r => r.UserId.HasValue);
    }

    /// <summary>本批里需要去问偏好的那些用户（去重）。</summary>
    internal static List<Guid> UserIdsToCheck(List<Recipient> candidates)
    {
        Check.NotNull(candidates);

        var ids = new HashSet<Guid>();
        foreach (var recipient in candidates)
        {
            if (recipient.UserId.HasValue)
                ids.Add(recipient.UserId.Value);
        }

        return [.. ids];
    }

    /// <summary>
    /// 按「该渠道仍启用」的用户名单剔除收件人；被剔除者<b>就地</b>标记为
    /// <see cref="NotificationStatus.Cancelled"/> 并写明原因。返回仍应当发送的那些。
    /// </summary>
    /// <param name="candidates">本轮待发的收件人。</param>
    /// <param name="enabledUserIds">
    /// <see cref="INotificationPreferenceService.FilterEnabledUsersAsync"/> 返回的、
    /// 该渠道仍启用的用户 id。
    /// </param>
    /// <remarks>
    /// ★ 被剔除的人标 <c>Cancelled</c> 并留下原因，而<b>不是</b>从列表里抹掉：
    /// 「因本人关闭而未发」与「发失败了」是两件事，投递报告里要分得开。
    /// ★★ 刻意<b>不</b>标 <c>Failed</c>：那会让 <c>ResendToFailedRecipientsAsync</c>
    /// 把他重新捞出来再发一遍，等于开一条绕过偏好的后门（与退订同一条理由）。
    /// ★ <c>UserId</c> 为空的收件人原样放行 —— 见类注释里「偏好按人、退订按地址」。
    /// </remarks>
    internal static List<Recipient> Apply(List<Recipient> candidates, IEnumerable<Guid> enabledUserIds)
    {
        Check.NotNull(candidates);
        Check.NotNull(enabledUserIds);

        var enabled = new HashSet<Guid>(enabledUserIds);
        var remaining = new List<Recipient>(candidates.Count);

        foreach (var recipient in candidates)
        {
            if (recipient.UserId is not { } userId || enabled.Contains(userId))
            {
                remaining.Add(recipient);
                continue;
            }

            recipient.Status = NotificationStatus.Cancelled;
            recipient.FailureReason = DisabledByPreferenceReason;
        }

        return remaining;
    }
}
