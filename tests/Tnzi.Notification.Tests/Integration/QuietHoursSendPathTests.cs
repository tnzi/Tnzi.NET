using Tnzi.Domain.Entities;

namespace Tnzi.Notification.Tests.Integration;

/// <summary>
/// 静默时段在<b>真实发送路径</b>上确实生效，而且它的处置是<b>延后</b>不是丢弃 ——
/// 真库、真 <see cref="NotificationPreferenceService"/>、真 <see cref="NotificationService.SendAsync"/>、
/// 真恢复扫描。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ <b>补的是哪个洞。</b><c>IsInQuietHoursAsync</c> 全仓零调用方、零测试，
/// <c>Recipient</c> 上没有任何「延后到什么时候」的字段，而管理端订阅页把
/// <c>quietHoursStart/End</c> 做成了可编辑字段 —— 保存返回 200、界面显示正常、
/// 发送路径永远读不到。这是本模块第四条「建好了没接上」
/// （退订 2026-08-08 / 渠道开关 2026-08-09 / 每小时上限 2026-08-29 之后）。
/// </para>
/// <para>
/// ★★ <b>处置必须是延后。</b>到点丢掉等于把「晚点再说」执行成「再也不说」，
/// 那比不实现更糟 —— 用户据界面以为自己只是把通知挪到了早上。所以这里的每一条
/// 「现在不该发」都配一条「到点要发出去」，两半合起来才是这个功能。
/// </para>
/// <para>
/// ★ 判定规则本身由 <c>Services/QuietHoursTests</c>（纯函数）覆盖。只写那一层
/// 就是把本模块已经踩过两次的坑再挖一遍：一个考究、正确、无人问津的过滤器。
/// </para>
/// </remarks>
public class QuietHoursSendPathTests : IntegrationTestBase
{
    private static readonly Guid Alice = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly Mock<IEmailSender> _emailSender = new();
    private readonly AdjustableClock _clock = new();
    private readonly DispatchOptions _dispatch = new()
    {
        EnableRecovery = true,
        StuckAfterMinutes = 15,
        RecoveryBatchSize = 50,
        RatePerMinute = 0,
    };

    protected override void ConfigureServices(IServiceCollection services)
    {
        AddRepo<Message>(services);
        AddRepo<Recipient>(services);
        AddRepo<OptOut>(services);
        AddRepo<Preference>(services);
        services.AddSingleton<TimeProvider>(_clock);

        var entityManagerMock = new Mock<IEntityManager>();
        entityManagerMock.Setup(m => m.GetAllDbContextTypes()).Returns(new[] { typeof(NotificationTestDbContext) });
        entityManagerMock.Setup(m => m.Initialize());
        services.AddSingleton(_ => entityManagerMock.Object);
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();
        services.AddScoped<IUnitOfWork, EFCoreUnitOfWork<NotificationTestDbContext>>();

        var options = new Mock<IOptionsMonitor<NotificationOptions>>();
        options.Setup(o => o.CurrentValue).Returns(new NotificationOptions { MaxConcurrency = 4, Dispatch = _dispatch });
        services.AddSingleton(_ => options.Object);

        _emailSender
            .Setup(s => s.SendToAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<List<EmailAttachment>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SendResult.CreateSuccess("stub-id"));
        services.AddSingleton(_ => _emailSender.Object);
        services.AddSingleton(_ => new Mock<ISmsSender>().Object);
        services.AddSingleton(_ => new Mock<IPushSender>().Object);
        services.AddSingleton(_ => new Mock<IFaxSender>().Object);

        services.AddScoped<INotificationOptOutService, NotificationOptOutService>();
        services.AddScoped<INotificationPreferenceService, NotificationPreferenceService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<INotificationProviderResolver, NotificationProviderResolver>();
        services.AddScoped<INotificationProviderSelector, DefaultNotificationProviderSelector>();
    }

    private static void AddRepo<TEntity>(IServiceCollection services) where TEntity : class, IEntity<Guid>
    {
        services.AddScoped<IRepository<TEntity, Guid>>(sp =>
            new EFCoreRepository<NotificationTestDbContext, TEntity, Guid>(
                sp.GetRequiredService<NotificationTestDbContext>(), serviceProvider: sp));
        services.AddScoped<IReadOnlyRepository<TEntity, Guid>>(sp =>
            sp.GetRequiredService<IRepository<TEntity, Guid>>());
    }

    // ── 现在不该发 ───────────────────────────────────────────────────────────

    /// <summary>★★★ 免打扰时段里的收件人不会被投递。</summary>
    [Fact]
    public async Task ARecipientInsideQuietHours_IsNotSentToNow()
    {
        await SetQuietHoursAsync(AroundNow());
        var messageId = await SeedMessageAsync(Alice);

        var result = await SendAsync(messageId);

        result.Succeeded.ShouldBeTrue(result.Message);
        VerifySent(Times.Never());
    }

    /// <summary>
    /// ★★★ 他被<b>延后</b>，不是被取消 —— 库里写着「什么时候再发」。
    /// </summary>
    /// <remarks>
    /// 标成 <c>Cancelled</c>（另外三道过滤的处置）会让这条投递永远不再发生，
    /// 而用户在界面上表达的是「晚点再说」。
    /// </remarks>
    [Fact]
    public async Task TheDeferral_IsPersistedAsASchedule_NotACancellation()
    {
        await SetQuietHoursAsync(AroundNow());
        var messageId = await SeedMessageAsync(Alice);

        await SendAsync(messageId);

        var recipient = await ReloadRecipientAsync(messageId);
        recipient.Status.ShouldBe(NotificationStatus.Scheduled);
        recipient.DeferredUntil.ShouldNotBeNull();
        recipient.DeferredUntil!.Value.ShouldBeGreaterThan(DateTime.UtcNow);
        recipient.FailureReason.ShouldBeNull("a deferral is not a failure");
    }

    /// <summary>
    /// ★★ 一条谁也没收到的消息不能显示成「已发送」。它还有下文，状态要留在 Scheduled，
    /// 而且不能盖 <c>SentTime</c>。
    /// </summary>
    [Fact]
    public async Task AFullyDeferredMessage_IsNotReportedAsSent()
    {
        await SetQuietHoursAsync(AroundNow());
        var messageId = await SeedMessageAsync(Alice);

        await SendAsync(messageId);

        var message = await ReloadMessageAsync(messageId);
        message.Status.ShouldBe(NotificationStatus.Scheduled);
        message.SentTime.ShouldBeNull();
    }

    // ── 到点要发出去 ─────────────────────────────────────────────────────────

    /// <summary>
    /// ★★★ 静默时段结束后，恢复扫描把那条消息接着发完。<b>这一半才让「延后」成立</b> ——
    /// 少了它，延后与到点丢掉在收件人看来一模一样。
    /// </summary>
    [Fact]
    public async Task WhenTheQuietHoursEnd_TheRecoveryScanDeliversIt()
    {
        // 时段已经过去（凌晨那段免打扰结束了），而收件人还排在延后队列里。
        await SetQuietHoursAsync(InThePast());
        var messageId = await SeedDeferredMessageAsync(Alice, deferredUntil: DateTime.UtcNow.AddMinutes(-1));

        await RecoverOnceAsync();

        VerifySent(Times.Once());
        (await ReloadMessageAsync(messageId)).Status.ShouldBe(NotificationStatus.Sent);
    }

    /// <summary>发完之后那一行不再是延后状态，下一轮扫描不会再取它一次。</summary>
    [Fact]
    public async Task ADeliveredRecipient_IsNotPickedUpAgainByTheNextScan()
    {
        await SetQuietHoursAsync(InThePast());
        await SeedDeferredMessageAsync(Alice, deferredUntil: DateTime.UtcNow.AddMinutes(-1));

        await RecoverOnceAsync();
        await RecoverOnceAsync();

        VerifySent(Times.Once());
    }

    /// <summary>还没到点的延后不会被提前取走。</summary>
    [Fact]
    public async Task ADeferralThatHasNotElapsed_IsLeftAlone()
    {
        await SetQuietHoursAsync(AroundNow());
        await SeedDeferredMessageAsync(Alice, deferredUntil: DateTime.UtcNow.AddHours(2));

        await RecoverOnceAsync();

        VerifySent(Times.Never());
    }

    /// <summary>
    /// 到点了但人还在免打扰里（用户把窗口改长了），再延后一次而不是硬发出去。
    /// </summary>
    [Fact]
    public async Task ADeferralThatElapsedIntoAnotherQuietWindow_IsDeferredAgain()
    {
        await SetQuietHoursAsync(AroundNow());
        var messageId = await SeedDeferredMessageAsync(Alice, deferredUntil: DateTime.UtcNow.AddMinutes(-1));

        await RecoverOnceAsync();

        VerifySent(Times.Never());
        var recipient = await ReloadRecipientAsync(messageId);
        recipient.Status.ShouldBe(NotificationStatus.Scheduled);
        recipient.DeferredUntil!.Value.ShouldBeGreaterThan(DateTime.UtcNow);
    }

    // ── 定时消息 + 延后 ──────────────────────────────────────────────────────

    /// <summary>
    /// ★★★ 一条<b>定时</b>消息在发送时有人被延后，到期扫描不能把它重新认领、发「没人可发」、然后记成 Sent。
    /// </summary>
    /// <remarks>
    /// 定时消息的 <c>ScheduledTime</c> 一直留在行上，延后又把消息退回 <c>Scheduled</c>：于是一个宽限期之后
    /// 「到期却没人发的定时消息」那一遍认得出它。此前它会被认领，发送路径找不到到期的收件人，写下
    /// <c>Sent</c> + <c>SentTime</c>；而延后那一遍只看 <c>Scheduled</c> 的消息，到点再也看不见它：
    /// 被延后的人永远收不到，消息显示已发送。
    /// </remarks>
    [Fact]
    public async Task AScheduledMessageWaitingOnADeferral_IsNotReclaimedAndMarkedSent()
    {
        await SetQuietHoursAsync(AroundNow());
        var messageId = await SeedDeferredMessageAsync(
            Alice, deferredUntil: DateTime.UtcNow.AddHours(2), scheduledTime: DateTime.UtcNow.AddMinutes(-60));

        await RecoverOnceAsync();

        VerifySent(Times.Never());
        var message = await ReloadMessageAsync(messageId);
        message.Status.ShouldBe(NotificationStatus.Scheduled);
        message.SentTime.ShouldBeNull();
        message.RetryCount.ShouldBe(0);
    }

    /// <summary>
    /// ★★ 同一条消息，延后到点之后照样被延后那一遍发出去：上一条的另一半。
    /// </summary>
    [Fact]
    public async Task AScheduledMessageWaitingOnADeferral_IsDeliveredWhenTheDeferralEnds()
    {
        await SetQuietHoursAsync(AroundNow());
        var messageId = await SeedDeferredMessageAsync(
            Alice, deferredUntil: DateTime.UtcNow.AddHours(2), scheduledTime: DateTime.UtcNow.AddMinutes(-60));
        await RecoverOnceAsync();

        // 免打扰结束、延后时刻到了。
        await SetQuietHoursAsync(InThePast());
        await DbContext.Set<Recipient>().Where(r => r.MessageId == messageId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.DeferredUntil, DateTime.UtcNow.AddMinutes(-1)));
        DbContext.ChangeTracker.Clear();

        await RecoverOnceAsync();

        VerifySent(Times.Once());
        (await ReloadRecipientAsync(messageId)).Status.ShouldBe(NotificationStatus.Sent);
        (await ReloadMessageAsync(messageId)).Status.ShouldBe(NotificationStatus.Sent);
    }

    /// <summary>
    /// ★★ 发送路径自己的那一半：被发送时没有到期的收件人、但还有人排在延后里，消息留在 <c>Scheduled</c>，
    /// 不盖 <c>SentTime</c>。管理端对它点「发送」就是这条路径。
    /// </summary>
    [Fact]
    public async Task SendingAMessageWhoseOnlyRecipientsAreStillDeferred_KeepsItScheduled()
    {
        await SetQuietHoursAsync(AroundNow());
        var messageId = await SeedDeferredMessageAsync(
            Alice, deferredUntil: DateTime.UtcNow.AddHours(2), scheduledTime: DateTime.UtcNow.AddMinutes(-60));

        var result = await SendAsync(messageId);

        result.Succeeded.ShouldBeTrue(result.Message);
        VerifySent(Times.Never());
        var message = await ReloadMessageAsync(messageId);
        message.Status.ShouldBe(NotificationStatus.Scheduled);
        message.SentTime.ShouldBeNull();
    }

    // ── 谁不受影响（对照）───────────────────────────────────────────────────

    /// <summary>★ 事务性消息穿透免打扰：凌晨的密码重置码必须现在就到。</summary>
    [Fact]
    public async Task ATransactionalMessage_IsSentDuringQuietHours()
    {
        await SetQuietHoursAsync(AroundNow());
        var messageId = await SeedMessageAsync(Alice, isTransactional: true);

        await SendAsync(messageId);

        VerifySent(Times.Once());
    }

    /// <summary>没设免打扰的人照常收到（不能只认「有偏好行就延后」）。</summary>
    [Fact]
    public async Task AUserWithNoQuietHours_IsSentToNormally()
    {
        var messageId = await SeedMessageAsync(Alice);

        await SendAsync(messageId);

        VerifySent(Times.Once());
    }

    /// <summary>时段之外照常收到。</summary>
    [Fact]
    public async Task AUserOutsideTheirQuietHours_IsSentToNormally()
    {
        await SetQuietHoursAsync(InThePast());
        var messageId = await SeedMessageAsync(Alice);

        await SendAsync(messageId);

        VerifySent(Times.Once());
    }

    /// <summary>
    /// ★ 纯地址收件人不受任何人的免打扰影响 —— 免打扰按人设，而导入名单里的地址没有人。
    /// </summary>
    [Fact]
    public async Task AnAddressOnlyRecipient_IsUnaffected()
    {
        await SetQuietHoursAsync(AroundNow());
        var messageId = await SeedMessageAsync(userId: null);

        await SendAsync(messageId);

        VerifySent(Times.Once());
    }

    // ── 按偏好上的时区解释 ───────────────────────────────────────────────────

    /// <summary>
    /// ★★ 设了时区的窗口按那个时区的本地时间判定：东京此刻落在东京本地的窗口里，于是被延后，
    /// 延到东京本地的结束时刻。
    /// </summary>
    [Fact]
    public async Task AZonedWindowAroundLocalNow_DefersUntilItsLocalEnd()
    {
        var tokyoNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Tokyo);
        await SetQuietHoursAsync(
            (TimeOnly.FromDateTime(tokyoNow.AddHours(-1)), TimeOnly.FromDateTime(tokyoNow.AddHours(1))), "Asia/Tokyo");
        var messageId = await SeedMessageAsync(Alice);

        await SendAsync(messageId);

        VerifySent(Times.Never());
        var recipient = await ReloadRecipientAsync(messageId);
        recipient.Status.ShouldBe(NotificationStatus.Scheduled);
        recipient.DeferredUntil!.Value.ShouldBe(DateTime.UtcNow.AddHours(1), TimeSpan.FromMinutes(2));
    }

    /// <summary>
    /// ★★ 同样的两个墙上时刻，带了时区就不再按 UTC 读：此刻在 UTC 的窗口里，
    /// 而东京已经是九个小时之后，于是照常发出。时区在取数到判定的任何一环被丢掉，这条就会变红。
    /// </summary>
    [Fact]
    public async Task AZonedWindow_IsNotReadAsUtc()
    {
        await SetQuietHoursAsync(AroundNow(), "Asia/Tokyo");
        var messageId = await SeedMessageAsync(Alice);

        await SendAsync(messageId);

        VerifySent(Times.Once());
    }

    /// <summary>
    /// ★★★ 秋季拨慢那一夜（多伦多 2026-11-01）：窗口在 EDT 开始、在 EST 结束，结束在本地 07:00 EST。
    /// </summary>
    /// <remarks>
    /// 这条钉住的是<b>发送路径问的是带时区的那个读取口</b>。只认 UTC 时刻的老读取口在当天换算
    /// 永远正确，差别只在切换那一夜出现：它按某一个偏移把窗口折成固定的 UTC 时刻，
    /// 在这一刻（22:30 EDT）要么还没进窗口、要么在 11:00 UTC 就放行，无论测试在哪个季节跑。
    /// </remarks>
    [Fact]
    public async Task OnTheFallBackNight_TheDeferralEndsAtSevenOnTheNewOffset()
    {
        _clock.Now = new DateTimeOffset(2026, 11, 1, 2, 30, 0, TimeSpan.Zero); // 10 月 31 日 22:30 EDT
        await SetQuietHoursAsync((new TimeOnly(22, 0), new TimeOnly(7, 0)), "America/Toronto");
        var messageId = await SeedMessageAsync(Alice);

        await SendAsync(messageId);

        VerifySent(Times.Never());
        (await ReloadRecipientAsync(messageId)).DeferredUntil
            .ShouldBe(new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc)); // 07:00 EST
    }

    // ── 与旧那个零调用方法共用一份判定 ───────────────────────────────────────

    /// <summary>
    /// <c>IsInQuietHoursAsync</c> 与批量那条必须给出同一个答案：这个模块的规则
    /// 曾经就是靠「同一条判定抄两遍」漂开的。
    /// </summary>
    [Fact]
    public async Task TheSingleUserCheckAgreesWithTheBatchLookup()
    {
        await SetQuietHoursAsync(AroundNow());
        var preferences = ServiceProvider.GetRequiredService<INotificationPreferenceService>();

        (await preferences.IsInQuietHoursAsync(Alice, "Email")).ShouldBeTrue();
        (await preferences.GetQuietHoursAsync([Alice], NotificationType.Email)).ShouldContainKey(Alice);
    }

    // ── 重发失败项 ───────────────────────────────────────────────────────────

    /// <summary>
    /// ★★ "Resend failed" runs the same quiet-hours step and defers the recipient. The message is then not
    /// finished: it has to sit in Scheduled, the only state the deferred pass looks at. Before, the resend
    /// answered "no failed recipients", left the message Failed, and the deferred recipient was never sent to.
    /// </summary>
    [Fact]
    public async Task ResendingAFailedRecipientInsideQuietHours_HandsTheMessageToTheDeferredPass()
    {
        await SetQuietHoursAsync(AroundNow());
        var messageId = await SeedAsync(Alice, false, NotificationStatus.Failed, NotificationStatus.Failed, null);

        var result = await ServiceProvider.GetRequiredService<INotificationService>().ResendToFailedRecipientsAsync(messageId);

        result.Succeeded.ShouldBeTrue(result.Message);
        VerifySent(Times.Never());
        (await ReloadRecipientAsync(messageId)).Status.ShouldBe(NotificationStatus.Scheduled);
        (await ReloadMessageAsync(messageId)).Status.ShouldBe(NotificationStatus.Scheduled);

        // The quiet hours end: the deferred pass sends it.
        await SetQuietHoursAsync(InThePast());
        await DbContext.Set<Recipient>().Where(r => r.MessageId == messageId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.DeferredUntil, DateTime.UtcNow.AddMinutes(-1)));
        DbContext.ChangeTracker.Clear();

        await RecoverOnceAsync();

        VerifySent(Times.Once());
        (await ReloadMessageAsync(messageId)).Status.ShouldBe(NotificationStatus.Sent);
    }

    // ── 夹具 ─────────────────────────────────────────────────────────────────

    private static readonly TimeZoneInfo Tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");

    /// <summary>默认走真实时钟；个别用例把「现在」钉在某一刻。</summary>
    private sealed class AdjustableClock : TimeProvider
    {
        public DateTimeOffset? Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now ?? base.GetUtcNow();
    }

    /// <summary>一个必然包含"现在"的窗口（前后各一小时）。</summary>
    private static (TimeOnly Start, TimeOnly End) AroundNow()
    {
        var now = DateTime.UtcNow;
        return (TimeOnly.FromDateTime(now.AddHours(-1)), TimeOnly.FromDateTime(now.AddHours(1)));
    }

    /// <summary>一个必然<b>不</b>包含"现在"的窗口（三小时前那一小时）。</summary>
    private static (TimeOnly Start, TimeOnly End) InThePast()
    {
        var now = DateTime.UtcNow;
        return (TimeOnly.FromDateTime(now.AddHours(-3)), TimeOnly.FromDateTime(now.AddHours(-2)));
    }

    private void VerifySent(Times times) => _emailSender.Verify(s => s.SendToAsync(
        It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(),
        It.IsAny<bool>(), It.IsAny<List<EmailAttachment>?>(), It.IsAny<CancellationToken>()), times);

    private Task<Result> SendAsync(Guid messageId)
        => ServiceProvider.GetRequiredService<INotificationService>().SendAsync(messageId);

    private Task RecoverOnceAsync()
        => new NotificationDispatchBackgroundService(
                ServiceProvider,
                ServiceProvider.GetRequiredService<IOptionsMonitor<NotificationOptions>>(),
                new Mock<ILogger<NotificationDispatchBackgroundService>>().Object)
            .RecoverOnceAsync(_dispatch, CancellationToken.None);

    private async Task SetQuietHoursAsync((TimeOnly Start, TimeOnly End) window, string? timeZone = null)
    {
        var result = await ServiceProvider.GetRequiredService<INotificationPreferenceService>()
            .SetPreferenceAsync(Alice, new SetNotificationPreferenceDto
            {
                Channel = "Email",
                IsEnabled = true,
                QuietHoursStart = window.Start,
                QuietHoursEnd = window.End,
                QuietHoursTimeZone = timeZone,
            });
        result.Succeeded.ShouldBeTrue(result.Message);
        DbContext.ChangeTracker.Clear();
    }

    private async Task<Recipient> ReloadRecipientAsync(Guid messageId)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Set<Recipient>().AsNoTracking().FirstAsync(r => r.MessageId == messageId);
    }

    private async Task<Message> ReloadMessageAsync(Guid messageId)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Set<Message>().AsNoTracking().FirstAsync(m => m.Id == messageId);
    }

    private Task<Guid> SeedMessageAsync(Guid? userId, bool isTransactional = false)
        => SeedAsync(userId, isTransactional, NotificationStatus.Pending, NotificationStatus.Pending, null);

    private Task<Guid> SeedDeferredMessageAsync(Guid userId, DateTime deferredUntil, DateTime? scheduledTime = null)
        => SeedAsync(userId, false, NotificationStatus.Scheduled, NotificationStatus.Scheduled, deferredUntil, scheduledTime);

    private async Task<Guid> SeedAsync(
        Guid? userId,
        bool isTransactional,
        NotificationStatus messageStatus,
        NotificationStatus recipientStatus,
        DateTime? deferredUntil,
        DateTime? scheduledTime = null)
    {
        var message = new Message
        {
            Id = Guid.NewGuid(),
            Subject = "Quarterly newsletter",
            Content = "Body",
            Type = NotificationType.Email,
            Category = "General",
            IsTransactional = isTransactional,
            Status = messageStatus,
            ScheduledTime = scheduledTime,
            TotalRecipientCount = 1,
            Recipients =
            [
                new Recipient
                {
                    Address = "alice@example.com",
                    UserId = userId,
                    Status = recipientStatus,
                    DeferredUntil = deferredUntil,
                }
            ],
        };

        await DbContext.Messages.AddAsync(message);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return message.Id;
    }
}
