namespace Tnzi.Notification.Services;

/// <inheritdoc cref="IFaxConfirmationService" />
public class FaxConfirmationService : ApplicationService, IFaxConfirmationService
{
    private readonly IRepository<Recipient, Guid> _recipientRepository;
    private readonly IRepository<Message, Guid> _messageRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IOptionsMonitor<NotificationOptions> _options;
    private readonly ICurrentTenant? _currentTenant;

    /// <summary>
    /// 按号码对号时最多捞多少条候选。窗口内的传真通常远少于这个数；
    /// 真要超过，取最近的那些也比拉全表合理。
    /// </summary>
    private const int NumberMatchCandidateLimit = 200;

    /// <summary>初始化一个 <see cref="FaxConfirmationService"/> 实例。</summary>
    public FaxConfirmationService(
        IServiceProvider serviceProvider,
        IRepository<Recipient, Guid> recipientRepository,
        IRepository<Message, Guid> messageRepository,
        IUnitOfWork unitOfWork,
        IOptionsMonitor<NotificationOptions> options,
        ICurrentTenant? currentTenant = null)
        : base(serviceProvider)
    {
        _recipientRepository = Check.NotNull(recipientRepository);
        _messageRepository = Check.NotNull(messageRepository);
        _unitOfWork = Check.NotNull(unitOfWork);
        _options = Check.NotNull(options);
        _currentTenant = currentTenant;
    }

    /// <inheritdoc />
    public async Task<Result<bool>> ApplyAsync(FaxConfirmation confirmation, CancellationToken cancellationToken = default)
    {
        Check.NotNull(confirmation);

        // ★ 只降级不升级。报"已送达"的回执没有新信息（那份传真本来就是 Sent），
        // 读不懂的回执更不该拿来推翻一次成功的投递。理由完整写在 IFaxConfirmationService 上。
        if (confirmation.Outcome != FaxDeliveryOutcome.Failed)
        {
            Logger.LogDebug(
                "Fax confirmation reports {Outcome}; nothing to record (CarrierMessageId={CarrierMessageId}, FaxNumber={FaxNumber})",
                confirmation.Outcome, confirmation.CarrierMessageId, confirmation.FaxNumber);
            return Ok(false);
        }

        var recipient = await FindRecipientAsync(confirmation, cancellationToken);
        if (recipient == null)
        {
            // 不是错误：回执收件箱里本来就有别人的信，也可能这份传真是另一个环境发的。
            // 但记 Warning 不记 Information：一份「没送到」的回执对不上号，那份传真就永远显示成已送达，
            // 而回执邮件已被标已读、不会再来 —— 对号失败的规模只有在这一行看得见。
            Logger.LogWarning(
                "Fax failure confirmation could not be matched to any recipient (CarrierMessageId={CarrierMessageId}, FaxNumber={FaxNumber})",
                confirmation.CarrierMessageId, confirmation.FaxNumber);
            return Ok(false);
        }

        // ★ 对号是跨租户找的（见 FindRecipientAsync），写回要切进收件人自己的租户：
        // RecountAsync 按 Id 取消息并 Include 收件人，那两条查询走的是全局过滤器，
        // 在轮询作用域（无租户）里会把租户的消息过滤掉，计数与状态就落不下去。
        using (_currentTenant?.Change(recipient.TenantId))
        {
            return await ApplyToRecipientAsync(recipient, confirmation, cancellationToken);
        }
    }

    /// <summary>在收件人所属租户里落库并发事件；调用方已经切进了那个租户。</summary>
    private async Task<Result<bool>> ApplyToRecipientAsync(Recipient recipient, FaxConfirmation confirmation, CancellationToken cancellationToken)
    {
        // ★ 降级**只从 Sent 出发**，这一句同时兜住两件事：
        // ① 幂等 —— 同一封回执被处理两次（重启、标已读失败、人工补录撞上轮询）没有第二次效果；
        // ② <c>Cancelled</c> 不许被改成 <c>Failed</c> —— 那两个状态在本模块是刻意分开的
        //    ("我们没发" vs "发了没成"），而 Failed 会被 ResendToFailedRecipientsAsync 捞回来重发，
        //    把退订过的地址改成 Failed 等于给退订开一条后门。
        if (recipient.Status != NotificationStatus.Sent)
            return Ok(false);

        recipient.Status = NotificationStatus.Failed;
        // ★ 收敛到列宽，理由见 NotificationFieldLimits。内置解析器已经把 Reason 截到 300，
        // 但 IFaxConfirmationParser 是可替换契约，长度纪律不能只长在内置实现上。
        recipient.FailureReason = NotificationFieldLimits.TruncateFailureReason(
            string.IsNullOrWhiteSpace(confirmation.Reason)
                ? "The fax gateway reported that this fax was not delivered."
                : $"The fax gateway reported a delivery failure: {confirmation.Reason}");

        await _recipientRepository.UpdateAsync(recipient, cancellationToken);
        var message = await RecountAsync(recipient.MessageId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        Logger.LogWarning(
            "Fax to {Address} was reported undelivered by the gateway: {Reason}",
            recipient.Address, recipient.FailureReason);

        // ★ 必须补一条失败事件。发送那一刻这份传真是成功的，下游收到的是 NotificationSentEvent ——
        // 回执几分钟后推翻了它，不发事件的话下游永远停在"成功"那一版：告警不响、统计对不上、
        // 业务侧也不会知道该改用别的方式联系对方。
        if (EventBus != null && message != null)
        {
            await EventBus.PublishAsync(new NotificationFailedEvent
            {
                MessageId = message.Id,
                Type = message.Type,
                FailureReason = recipient.FailureReason!,
                RetryCount = message.RetryCount,
                MaxRetryCount = message.MaxRetryCount
            });
        }

        return Ok(true);
    }

    /// <summary>
    /// 把回执对回一个收件人：先按承载邮件的 Message-ID 精确匹配，拿不到再按号码在时间窗口里找。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ 两条路的可靠程度差很多，所以待遇也不同：Message-ID 是那封信自己带的、不会认错，
    /// 因此不受时间窗口约束；号码是**从回执文字里读出来的**，同一个号码这个月可能发过好几份，
    /// 所以只在 <see cref="FaxConfirmationOptions.LookbackHours"/> 内、且只认还是 <c>Sent</c> 的那些。
    /// </para>
    /// <para>
    /// ★★ <b>两条查询都跨租户找</b>（<c>IgnoreQueryFilters</c>）：回执来自一个共享收件箱，
    /// 轮询作用域里没有任何租户，而 <c>Recipient</c> / <c>Message</c> 都是多租户实体 ——
    /// 走全局过滤器就只剩 <c>TenantId == null</c>，租户发出去的传真永远对不上号，
    /// 那份传真就一直显示成已送达，回执邮件却已被标已读、不会再来。对号条件本身已经足够精确
    /// （Message-ID 等值，或号码 + Sent + 时间窗 + Fax），不靠租户过滤缩小范围。
    /// 代价是软删过滤也一起关掉了，所以显式排除已删除的消息。
    /// </para>
    /// </remarks>
    private async Task<Recipient?> FindRecipientAsync(FaxConfirmation confirmation, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(confirmation.CarrierMessageId))
        {
            var byMessageId = await _recipientRepository
                .AsQueryable(withTracking: true)
                .IgnoreQueryFilters()
                .Include(r => r.Message)
                .FirstOrDefaultAsync(
                    r => r.ExternalMessageId == confirmation.CarrierMessageId
                         && r.Message.Type == NotificationType.Fax
                         && !r.Message.IsDeleted,
                    cancellationToken);

            if (byMessageId != null)
                return byMessageId;
        }

        var number = confirmation.FaxNumber;
        if (string.IsNullOrWhiteSpace(number))
            return null;

        var lookback = Math.Max(1, _options.CurrentValue.FaxSender?.Confirmation?.LookbackHours ?? 72);

        // ★ 没有可信时间戳时按"刚刚收到"算。缺 Date 信头的邮件在 MimeKit 里是
        // DateTimeOffset.MinValue，webhook 来源也可能干脆不填 —— 拿它去减 72 小时
        // 抛的是 ArgumentOutOfRangeException，而那会掀掉整轮轮询，
        // 连同那一批已经被标成已读的回执一起永久丢失。
        var reference = confirmation.ReceivedAt > DateTimeOffset.UnixEpoch
            ? confirmation.ReceivedAt.UtcDateTime
            : DateTime.UtcNow;
        var since = reference.AddHours(-lookback);

        var candidates = await _recipientRepository
            .AsQueryable(withTracking: true)
            .IgnoreQueryFilters()
            .Include(r => r.Message)
            .Where(r => r.Message.Type == NotificationType.Fax
                        && !r.Message.IsDeleted
                        && r.Status == NotificationStatus.Sent
                        && r.SentTime != null
                        && r.SentTime >= since)
            .OrderByDescending(r => r.SentTime)
            .Take(NumberMatchCandidateLimit)
            .ToListAsync(cancellationToken);

        // 归一化后比对，而不是字符串等值：库里存的是人写的号码（`+1 (905) 555-1234`），
        // 回执里读出来的是归一化后的数字串 —— 直接比永远对不上。
        return candidates.Find(r =>
            FaxNumber.TryNormalize(r.Address, out var normalized, out _)
            && string.Equals(normalized, number, StringComparison.Ordinal));
    }

    /// <summary>
    /// 重算这条消息的成功/失败计数与状态。
    /// </summary>
    /// <remarks>
    /// ★ 不重算的话，投递报告会自相矛盾：收件人是 <c>Failed</c>，而消息还写着"1/1 成功"。
    /// ★★ <b>刻意不动 <c>SentTime</c> 与消息级 <c>FailureReason</c></b>：那份传真确实在那个时刻发出去过，
    /// 回执推翻的是"送到了"，不是"发出过"。发送路径上的那两处汇总看着与这里重复，
    /// 其实各有各的语义（一处要盖 <c>SentTime</c>、一处连全失败分支都刻意没有），
    /// 合并只会让三个不同的决定挤在一个带标志位的函数里。
    /// </remarks>
    private async Task<Message?> RecountAsync(Guid messageId, CancellationToken cancellationToken)
    {
        var message = await _messageRepository
            .AsQueryable(withTracking: true)
            .Include(m => m.Recipients)
            .FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);

        if (message == null)
            return null;

        message.SuccessCount = message.Recipients.Count(r => r.Status == NotificationStatus.Sent);
        message.FailureCount = message.Recipients.Count(r => r.Status == NotificationStatus.Failed);

        if (message.FailureCount == 0)
            message.Status = NotificationStatus.Sent;
        else if (message.SuccessCount > 0)
            message.Status = NotificationStatus.PartiallySent;
        else
            message.Status = NotificationStatus.Failed;

        await _messageRepository.UpdateAsync(message, cancellationToken);
        return message;
    }
}
