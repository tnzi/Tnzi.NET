namespace Tnzi.Notification.Services;

/// <summary>
/// 回答「这条消息现在还应该发给谁」：退订、渠道偏好、每小时上限、静默时段四道过滤，按顺序跑一遍。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="NotificationService"/> 分开的理由与 <see cref="RecipientChannelDispatcher"/> 相同：
/// 那边管流程（创建、入队、落状态、重试、统计），这里只回答收件人资格。新增一道过滤只动这一个文件。
/// </para>
/// <para>
/// ★★ <b>顺序表在这里只有一份。</b>此前两条调用路径（<c>SendAsync</c> 与
/// <c>ResendToFailedRecipientsAsync</c>）各自把三道过滤列了一遍 —— 那是一张被抄了两份的清单，
/// 加第四道时要同时记得改两处。加每小时上限那次正好撞上这个形态。
/// </para>
/// <para>
/// ★ <b>四道并列而不是二选一</b>：退订按<b>地址</b>（收件人未必是注册用户）、偏好与上限与
/// 静默时段按<b>人</b>，管的是不同的东西 —— 任何一道说「别发」就不发。
/// </para>
/// <para>
/// ★★ <b>第四道的处置不同：延后，不是丢弃。</b>前三道把人标成
/// <see cref="NotificationStatus.Cancelled"/>（这条消息对他到此为止），
/// 静默时段把人标成 <see cref="NotificationStatus.Scheduled"/> 并写下
/// <see cref="Recipient.DeferredUntil"/> —— 它表达的是时机而不是意愿。
/// 所以它<b>排在最后</b>：先被退订/关掉渠道/超出上限拦下的人根本不该进入排期。
/// </para>
/// <para>
/// ★ <b>每一道都就地落库自己的标记</b>，不指望调用方：两条调用路径都有「过滤完什么都不剩 →
/// 提前 return」的分支，不在这里保存，标记就只活在被跟踪的实体里、随请求一起消失。
/// </para>
/// </remarks>
internal sealed class RecipientEligibility
{
    private readonly IRepository<Message, Guid> _notificationRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationOptOutService _optOutService;
    private readonly INotificationPreferenceService _preferenceService;
    private readonly ILogger _logger;

    public RecipientEligibility(
        IRepository<Message, Guid> notificationRepository,
        IUnitOfWork unitOfWork,
        INotificationOptOutService optOutService,
        INotificationPreferenceService preferenceService,
        ILogger logger)
    {
        _notificationRepository = Check.NotNull(notificationRepository);
        _unitOfWork = Check.NotNull(unitOfWork);
        _optOutService = Check.NotNull(optOutService);
        _preferenceService = Check.NotNull(preferenceService);
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 依次跑完四道过滤。被拦下的已就地标记并落库。
    /// </summary>
    /// <returns>
    /// <c>Sendable</c> = 现在就该发的那些；<c>DeferredCount</c> = 本轮因静默时段被延后的人数
    /// （调用方据此分辨「谁也没剩下」到底是「都被拦掉了」还是「都挪到晚点了」——
    /// 前者这条消息到此为止，后者它还有下文）。
    /// </returns>
    public async Task<(List<Recipient> Sendable, int DeferredCount)> FilterAsync(
        Message notification, List<Recipient> candidates, CancellationToken cancellationToken)
    {
        var remaining = await ExcludeOptedOutAsync(notification, candidates, cancellationToken);
        remaining = await ExcludePreferenceDisabledAsync(notification, remaining, cancellationToken);
        remaining = await ExcludeOverFrequencyCapAsync(notification, remaining, cancellationToken);
        return await DeferQuietHoursAsync(notification, remaining, cancellationToken);
    }

    /// <summary>
    /// 把「本人此刻正在免打扰时段里」的收件人<b>延后</b>到时段结束，返回现在就该发的那些。
    /// </summary>
    /// <remarks>
    /// 判定规则在 <see cref="QuietHoursRecipientFilter"/> 与 <see cref="QuietHoursWindow"/>
    /// （纯函数，含「为什么是延后而不是丢弃」的完整说明）；这里只负责问一次偏好表、
    /// 把结果套上去、并把标记落库。
    /// <para>
    /// ★ 查询只在真有人设了静默时段时才发生（<c>ShouldConsultQuietHours</c> 先挡一道），
    /// 与另外两道按人的过滤同款。
    /// </para>
    /// </remarks>
    private async Task<(List<Recipient> Sendable, int DeferredCount)> DeferQuietHoursAsync(
        Message notification, List<Recipient> candidates, CancellationToken cancellationToken)
    {
        if (!QuietHoursRecipientFilter.ShouldConsultQuietHours(notification, candidates))
            return (candidates, 0);

        var raw = await _preferenceService.GetQuietHoursAsync(
            PreferenceRecipientFilter.UserIdsToCheck(candidates),
            notification.Type,
            cancellationToken);

        if (raw.Count == 0)
            return (candidates, 0);

        var windows = raw.ToDictionary(kv => kv.Key, kv => new QuietHoursWindow(kv.Value.Start, kv.Value.End));
        var remaining = QuietHoursRecipientFilter.Apply(candidates, windows, DateTime.UtcNow, out var deferred);

        if (deferred.Count == 0)
            return (candidates, 0);

        _logger.LogInformation(
            "Notification {NotificationId}: deferred {DeferredCount} of {TotalCount} recipient(s) who are inside the quiet hours they set for this channel",
            notification.Id, deferred.Count, candidates.Count);

        // ★ 就地落库，理由与另外三道逐字相同 —— 只是这一次落的不是「不发了」而是
        //   「什么时候再发」，而没有它，到期扫描根本看不见这些人。
        await _notificationRepository.UpdateAsync(notification, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return (remaining, deferred.Count);
    }

    /// <summary>
    /// 从待发列表里剔除已退订的收件人，并把他们就地标记为
    /// <see cref="NotificationStatus.Cancelled"/>。返回仍应当发送的那些。
    /// </summary>
    /// <remarks>
    /// 判定规则在 <see cref="OptOutRecipientFilter"/>（纯函数，含"为什么这么定"的完整说明）；
    /// 这里只负责问一次退订名单并把结果套上去。
    /// </remarks>
    private async Task<List<Recipient>> ExcludeOptedOutAsync(
        Message notification, List<Recipient> candidates, CancellationToken cancellationToken)
    {
        if (!OptOutRecipientFilter.ShouldConsultOptOutList(notification, candidates.Count))
            return candidates;

        var allowed = await _optOutService.FilterAllowedAsync(
            candidates.Select(r => r.Address),
            notification.Type,
            notification.Category,
            cancellationToken);

        var remaining = OptOutRecipientFilter.Apply(candidates, allowed);
        if (remaining.Count != candidates.Count)
        {
            _logger.LogInformation(
                "Notification {NotificationId}: skipped {SkippedCount} of {TotalCount} recipient(s) that opted out",
                notification.Id, candidates.Count - remaining.Count, candidates.Count);

            // ★ 就地落库，不指望调用方。「因退订而未发」是要拿去交差的记录，
            // 而两条调用路径都有「过滤完就什么都不剩 → 提前 return」的分支：
            // SendAsync 那条只 UpdateAsync 不 SaveChanges，ResendToFailedRecipientsAsync
            // 那条**两样都没有** —— 标记就只活在被跟踪的实体里，随请求一起消失。
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return remaining;
    }

    /// <summary>
    /// 把「该渠道已被本人在偏好里关掉」的收件人择出去，就地标记为
    /// <see cref="NotificationStatus.Cancelled"/>。返回仍应当发送的那些。
    /// </summary>
    /// <remarks>
    /// 判定规则在 <see cref="PreferenceRecipientFilter"/>（纯函数，含"为什么这么定"的完整说明）；
    /// 这里只负责问一次偏好表并把结果套上去。
    /// <para>
    /// ★ <b>与退订并列而不是二选一</b>：退订按地址（收件人未必是注册用户），
    /// 偏好按人（同一个人在多个渠道上的开关）—— 两者管的是不同的东西，
    /// 任一说「别发」就不发。
    /// </para>
    /// </remarks>
    private async Task<List<Recipient>> ExcludePreferenceDisabledAsync(
        Message notification, List<Recipient> candidates, CancellationToken cancellationToken)
    {
        if (!PreferenceRecipientFilter.ShouldConsultPreferences(notification, candidates))
            return candidates;

        var enabled = await _preferenceService.FilterEnabledUsersAsync(
            PreferenceRecipientFilter.UserIdsToCheck(candidates),
            notification.Type,
            notification.Category,
            cancellationToken);

        var remaining = PreferenceRecipientFilter.Apply(candidates, enabled);
        if (remaining.Count != candidates.Count)
        {
            _logger.LogInformation(
                "Notification {NotificationId}: skipped {SkippedCount} of {TotalCount} recipient(s) who disabled this channel in their preferences",
                notification.Id, candidates.Count - remaining.Count, candidates.Count);

            // ★ 就地落库，理由与 ExcludeOptedOutAsync 逐字相同：两条调用路径都有
            // 「过滤完就什么都不剩 → 提前 return」的分支，不在这里落库标记就只活在
            // 被跟踪的实体里、随请求一起消失。
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return remaining;
    }

    /// <summary>
    /// 把「本人这一小时在本渠道已经收够了」的收件人择出去，就地标记为
    /// <see cref="NotificationStatus.Cancelled"/>。返回仍应当发送的那些。
    /// </summary>
    /// <remarks>
    /// 判定规则在 <see cref="FrequencyCapFilter"/>（纯函数，含「为什么是丢弃而不是延后」的
    /// 完整说明）；这里负责问一次上限、数一次窗口内的实发条数，再把结果套上去。
    /// <para>
    /// ★ <b>两次查询都只在真有人设了上限时才发生</b>：绝大多数部署一个上限都没设，
    /// 那种情况下这一步只多一次很窄的偏好表查询，第二次计数查询根本不会发出去。
    /// </para>
    /// </remarks>
    private async Task<List<Recipient>> ExcludeOverFrequencyCapAsync(
        Message notification, List<Recipient> candidates, CancellationToken cancellationToken)
    {
        if (!FrequencyCapFilter.ShouldConsultCaps(notification, candidates))
            return candidates;

        var caps = await _preferenceService.GetFrequencyCapsAsync(
            PreferenceRecipientFilter.UserIdsToCheck(candidates),
            notification.Type,
            notification.Category,
            cancellationToken);

        if (caps.Count == 0)
            return candidates;

        var sentInWindow = await CountSentInWindowAsync(notification.Type, caps.Keys, cancellationToken);

        var remaining = FrequencyCapFilter.Apply(candidates, caps, sentInWindow);
        if (remaining.Count != candidates.Count)
        {
            _logger.LogInformation(
                "Notification {NotificationId}: skipped {SkippedCount} of {TotalCount} recipient(s) who reached their hourly limit for this channel",
                notification.Id, candidates.Count - remaining.Count, candidates.Count);

            // ★ 就地落库，理由与另外两道过滤逐字相同：调用方两条路径都有「过滤完什么都不剩
            // → 提前 return」的分支，不在这里保存标记就只活在被跟踪的实体里。
            await _notificationRepository.UpdateAsync(notification, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return remaining;
    }

    /// <summary>
    /// 数一数这些人在窗口内、在这个渠道上<b>真的收到</b>了多少条。
    /// </summary>
    /// <remarks>
    /// ★ 判据是 <see cref="NotificationStatus.Sent"/> 且有 <c>SentTime</c>：上限管的是
    /// 「进到收件箱的条数」，被退订/偏好/上限自身拦下的那些从来没有到达过任何人，
    /// 算进去等于让一次被拦截替真正的投递占了名额。
    /// </remarks>
    private async Task<IReadOnlyDictionary<Guid, int>> CountSentInWindowAsync(
        NotificationType channel, IEnumerable<Guid> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.ToList();
        var since = DateTime.UtcNow - FrequencyCapFilter.Window;

        var counts = await _notificationRepository
            .AsQueryable()
            .AsNoTracking()
            .Where(m => m.Type == channel)
            .SelectMany(m => m.Recipients)
            .Where(r => r.UserId != null
                        && ids.Contains(r.UserId.Value)
                        && r.Status == NotificationStatus.Sent
                        && r.SentTime != null
                        && r.SentTime >= since)
            .GroupBy(r => r.UserId!.Value)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(c => c.UserId, c => c.Count);
    }
}
