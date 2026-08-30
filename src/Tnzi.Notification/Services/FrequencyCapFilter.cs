namespace Tnzi.Notification.Services;

/// <summary>
/// 发送前把「本人这一小时已经收够了」的收件人择出去的判定（纯函数，便于单测）。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>这是本模块第三条建好了却没接上的链</b>：<see cref="Preference.MaxFrequencyPerHour"/>
/// 收在偏好表里、写入时校验为正数、管理端表单里显示得好好的，而<b>发送路径从不读它</b> ——
/// 用户设了「每小时最多一封」，然后一小时收五十封。与退订（2026-08-08 接上）、
/// 渠道开关（2026-08-09 接上）是同一形态的第三例。
/// </para>
/// <para>
/// ★★ <b>超额的处理是丢弃，不是延后 —— 这与静默时段刻意不同。</b>
/// 「每小时最多 N 条」表达的是<b>音量</b>：多出来的那些，用户要的就是别发。
/// 而静默时段表达的是<b>时机</b>（「现在别吵我」），它的正确语义是延后投递 ——
/// 到点丢掉等于把「晚点再说」执行成了「再也不说」。两者的差别不在实现难度，
/// 在于用户设置它时想要的是什么，所以本模块接上了频率上限而把静默时段留着
/// （见 docs/modules/notification.md）。
/// </para>
/// <para>
/// ★ <b>事务性消息一律豁免</b>，判据与退订、渠道开关逐字一致：一个把营销邮件限到
/// 每小时一封的人，不该因此收不到密码重置。
/// </para>
/// <para>
/// ★ <b>没有 <see cref="Recipient.UserId"/> 的收件人不受本过滤影响</b> ——
/// 上限按人算，而群发名单里的纯地址收件人没有、也不可能有偏好行。
/// </para>
/// </remarks>
internal static class FrequencyCapFilter
{
    /// <summary>因本人的每小时上限而拦下时写进 <c>Recipient.FailureReason</c> 的说明。</summary>
    internal const string OverFrequencyCapReason = "Recipient reached the hourly limit they set for this channel in their notification preferences";

    /// <summary>统计窗口：字段名里的「PerHour」就是它的定义。</summary>
    internal static readonly TimeSpan Window = TimeSpan.FromHours(1);

    /// <summary>这一批到底要不要去查上限。</summary>
    /// <remarks>三个条件缺一不查，与 <see cref="PreferenceRecipientFilter.ShouldConsultPreferences"/> 同款。</remarks>
    internal static bool ShouldConsultCaps(Message notification, List<Recipient> candidates)
    {
        Check.NotNull(notification);
        Check.NotNull(candidates);

        return !notification.IsTransactional
            && candidates.Count > 0
            && candidates.Exists(r => r.UserId.HasValue);
    }

    /// <summary>
    /// 按每个人的上限与他这一小时已收到的条数剔除收件人；被剔除者<b>就地</b>标记为
    /// <see cref="NotificationStatus.Cancelled"/> 并写明原因。返回仍应当发送的那些。
    /// </summary>
    /// <param name="candidates">本轮待发的收件人。</param>
    /// <param name="capsByUser">设了上限的用户及其上限（没设的人不在字典里）。</param>
    /// <param name="sentInWindowByUser">每人在窗口内该渠道已成功送达的条数。</param>
    /// <remarks>
    /// ★ <b>同一批里同一个人出现多次也要各算一条</b>：预算从库里的条数起算，然后随着
    /// 本批放行的每一条递减。只看库里的条数会让「一次群发里给同一个人发五条」整批放行，
    /// 而那正是上限要拦的事。
    /// <para>
    /// ★ 标 <c>Cancelled</c> 而不是 <c>Failed</c>：后者会被 <c>ResendToFailedRecipientsAsync</c>
    /// 捞回来重发，等于开一条绕过上限的后门（与退订、渠道开关同一条理由）。
    /// </para>
    /// </remarks>
    internal static List<Recipient> Apply(
        List<Recipient> candidates,
        IReadOnlyDictionary<Guid, int> capsByUser,
        IReadOnlyDictionary<Guid, int> sentInWindowByUser)
    {
        Check.NotNull(candidates);
        Check.NotNull(capsByUser);
        Check.NotNull(sentInWindowByUser);

        var remaining = new List<Recipient>(candidates.Count);
        var admittedByUser = new Dictionary<Guid, int>();

        foreach (var recipient in candidates)
        {
            if (recipient.UserId is not { } userId || !capsByUser.TryGetValue(userId, out var cap))
            {
                remaining.Add(recipient);
                continue;
            }

            sentInWindowByUser.TryGetValue(userId, out var alreadySent);
            admittedByUser.TryGetValue(userId, out var admitted);

            if (alreadySent + admitted < cap)
            {
                admittedByUser[userId] = admitted + 1;
                remaining.Add(recipient);
                continue;
            }

            recipient.Status = NotificationStatus.Cancelled;
            recipient.FailureReason = OverFrequencyCapReason;
        }

        return remaining;
    }
}
