namespace Tnzi.Notification.Services;

/// <summary>
/// 发送前把「本人此刻正在静默时段里」的收件人<b>延后</b>的判定（纯函数，便于单测）。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ <b>延后，不是丢弃 —— 这是本过滤与另外三道的根本差别。</b>
/// 退订说的是「一个都别发」，渠道开关说的是「这条渠道别发」，每小时上限说的是
/// <b>音量</b>（多出来的那些，用户要的就是别发）。而静默时段说的是<b>时机</b>：
/// 「现在别吵我」。到点丢掉等于把「晚点再说」执行成「再也不说」，
/// 那比不实现这个功能更糟 —— 界面上那两个字段一直可以编辑、保存返回 200，
/// 用户据此以为自己只是把通知挪到了早上。
/// </para>
/// <para>
/// ★ 被延后的收件人标记为 <see cref="NotificationStatus.Scheduled"/> 并写下
/// <see cref="Recipient.DeferredUntil"/>（静默时段结束的<b>绝对时刻</b>）。
/// 恢复扫描到点把它们接着发完；<c>SendAsync</c> 的待发筛选也认这一条。
/// </para>
/// <para>
/// ★ <b>事务性消息一律豁免</b>，判据与另外三道逐字一致：把营销邮件挪到早上的人，
/// 不该因此在凌晨收不到密码重置码。
/// </para>
/// <para>
/// ★ <b>没有 <see cref="Recipient.UserId"/> 的收件人不受影响</b> —— 静默时段按人设，
/// 而群发名单里的纯地址收件人没有、也不可能有偏好行。
/// </para>
/// <para>
/// ★ <b>刻意不看 <see cref="NotificationPriority"/></b>：本模块表达「这条必须现在送到」
/// 的方式是 <c>IsTransactional</c>，再引入第二条豁免轴，两者迟早会在某个调用点上
/// 各说各话。要让一条高优先级的运营消息穿透静默时段，就把它标成事务性的。
/// </para>
/// </remarks>
internal static class QuietHoursRecipientFilter
{
    /// <summary>这一批到底要不要去查静默时段。</summary>
    /// <remarks>三个条件缺一不查，与 <see cref="FrequencyCapFilter.ShouldConsultCaps"/> 同款。</remarks>
    internal static bool ShouldConsultQuietHours(Message notification, List<Recipient> candidates)
    {
        Check.NotNull(notification);
        Check.NotNull(candidates);

        return !notification.IsTransactional
            && candidates.Count > 0
            && candidates.Exists(r => r.UserId.HasValue);
    }

    /// <summary>
    /// 把此刻处于静默时段的收件人就地标记为延后，返回<b>现在就该发</b>的那些。
    /// </summary>
    /// <param name="candidates">本轮待发的收件人。</param>
    /// <param name="windowsByUser">设了静默时段的用户及其窗口（没设的人不在字典里）。</param>
    /// <param name="utcNow">判定时刻。</param>
    /// <param name="deferred">本次被延后的收件人。</param>
    internal static List<Recipient> Apply(
        List<Recipient> candidates,
        IReadOnlyDictionary<Guid, QuietHoursWindow> windowsByUser,
        DateTime utcNow,
        out List<Recipient> deferred)
    {
        Check.NotNull(candidates);
        Check.NotNull(windowsByUser);

        var sendable = new List<Recipient>(candidates.Count);
        deferred = [];

        foreach (var recipient in candidates)
        {
            var until = ResolveDeferral(recipient, windowsByUser, utcNow);
            if (until == null)
            {
                sendable.Add(recipient);
                continue;
            }

            recipient.Status = NotificationStatus.Scheduled;
            recipient.DeferredUntil = until;
            // ★ 刻意不写 FailureReason：这不是一次失败，而投递报告拿那一列当失败说明读。
            //   「为什么还没送到」由 DeferredUntil 自己说清楚。
            deferred.Add(recipient);
        }

        return sendable;
    }

    /// <summary>
    /// 这个收件人现在该被延后到什么时候；不该延后时返回 <see langword="null"/>。
    /// </summary>
    private static DateTime? ResolveDeferral(
        Recipient recipient,
        IReadOnlyDictionary<Guid, QuietHoursWindow> windowsByUser,
        DateTime utcNow)
    {
        if (recipient.UserId is not { } userId)
            return null;

        if (!windowsByUser.TryGetValue(userId, out var window))
            return null;

        return window.EndsAfter(utcNow);
    }
}
