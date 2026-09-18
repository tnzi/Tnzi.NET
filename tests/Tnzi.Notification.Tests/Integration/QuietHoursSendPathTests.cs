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

    // ── 夹具 ─────────────────────────────────────────────────────────────────

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

    private async Task SetQuietHoursAsync((TimeOnly Start, TimeOnly End) window)
    {
        var result = await ServiceProvider.GetRequiredService<INotificationPreferenceService>()
            .SetPreferenceAsync(Alice, new SetNotificationPreferenceDto
            {
                Channel = "Email",
                IsEnabled = true,
                QuietHoursStart = window.Start,
                QuietHoursEnd = window.End,
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

    private Task<Guid> SeedDeferredMessageAsync(Guid userId, DateTime deferredUntil)
        => SeedAsync(userId, false, NotificationStatus.Scheduled, NotificationStatus.Scheduled, deferredUntil);

    private async Task<Guid> SeedAsync(
        Guid? userId,
        bool isTransactional,
        NotificationStatus messageStatus,
        NotificationStatus recipientStatus,
        DateTime? deferredUntil)
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
