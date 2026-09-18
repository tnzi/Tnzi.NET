using Tnzi.Domain.Entities;

namespace Tnzi.Notification.Tests.Integration;

/// <summary>
/// 到期却没人发的定时消息，由派发恢复扫描接手 —— 在真库上验证。
/// </summary>
/// <remarks>
/// <para>
/// ★★ <b>补的是哪个洞</b>：定时消息的行落库了（<c>Scheduled</c> + <c>ScheduledTime</c>），
/// 但真正会去发它的只有 <c>ChannelQueueService</c> 起的一个<b>进程内</b>定时器。进程一停它就没了；
/// 多实例部署里它从一开始就只存在于接下创建请求的那一个实例上。于是消息一直好端端地列在
/// "已排期"里，永远不发出去 —— 而恢复扫描此前只匹配 <c>Status == Sending</c>，看不见它。
/// </para>
/// <para>
/// ★ <b>这个后台服务此前一条测试都没有</b>，所以卡住批次那一遍也在这里补上对照 ——
/// 只测新加的那一遍，看不出自己有没有把旧的那一遍改坏。
/// </para>
/// </remarks>
public class ScheduledDispatchRecoveryTests : IntegrationTestBase
{
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

    // ── 该发的 ───────────────────────────────────────────────────────────────

    /// <summary>★★ 到期已久却仍停在 Scheduled 的消息，恢复扫描要把它发出去。</summary>
    [Fact]
    public async Task AnOverdueScheduledMessage_IsFinallySent()
    {
        var messageId = await SeedAsync(NotificationStatus.Scheduled, scheduledTime: MinutesAgo(60));

        await RecoverOnceAsync();

        VerifySent(Times.Once());
        (await ReloadAsync(messageId)).Status.ShouldBe(NotificationStatus.Sent);
    }

    /// <summary>对照：卡在 Sending 的批次照旧被接手（新加的那一遍没有把旧的挤掉）。</summary>
    [Fact]
    public async Task AStalledSendingBatch_IsStillResumed()
    {
        await SeedAsync(NotificationStatus.Sending, lastModified: MinutesAgo(60));

        await RecoverOnceAsync();

        VerifySent(Times.Once());
    }

    // ── 不该碰的 ─────────────────────────────────────────────────────────────

    /// <summary>还没到点的定时消息不能提前发出去。</summary>
    [Fact]
    public async Task AScheduledMessageThatIsNotDueYet_IsLeftAlone()
    {
        var messageId = await SeedAsync(NotificationStatus.Scheduled, scheduledTime: DateTime.UtcNow.AddHours(1));

        await RecoverOnceAsync();

        VerifySent(Times.Never());
        (await ReloadAsync(messageId)).Status.ShouldBe(NotificationStatus.Scheduled);
    }

    /// <summary>
    /// ★ 刚到期的那一条要留给本进程还活着的定时器 —— 宽限期就是为这件事存在的。
    /// 没有它，扫描会和定时器抢同一条消息，而两边都发就是重复投递。
    /// </summary>
    [Fact]
    public async Task AJustDueScheduledMessage_IsLeftToItsOwnTimer()
    {
        var messageId = await SeedAsync(NotificationStatus.Scheduled, scheduledTime: MinutesAgo(1));

        await RecoverOnceAsync();

        VerifySent(Times.Never());
        (await ReloadAsync(messageId)).Status.ShouldBe(NotificationStatus.Scheduled);
    }

    /// <summary>没有 ScheduledTime 的 Scheduled 行不是"到期的定时消息"，不能凭空发出去。</summary>
    [Fact]
    public async Task AScheduledRowWithNoScheduledTime_IsIgnored()
    {
        await SeedAsync(NotificationStatus.Scheduled, scheduledTime: null);

        await RecoverOnceAsync();

        VerifySent(Times.Never());
    }

    /// <summary>已取消的定时消息不能被恢复扫描复活。</summary>
    [Fact]
    public async Task ACancelledMessage_IsNotRevived()
    {
        await SeedAsync(NotificationStatus.Cancelled, scheduledTime: MinutesAgo(60));

        await RecoverOnceAsync();

        VerifySent(Times.Never());
    }

    // ── 认领是原子的 ─────────────────────────────────────────────────────────

    /// <summary>一轮扫描认领之后，同一条不会被第二轮再认领一次。</summary>
    [Fact]
    public async Task AMessageAlreadyClaimed_IsNotPickedUpAgain()
    {
        var messageId = await SeedAsync(NotificationStatus.Scheduled, scheduledTime: MinutesAgo(60));
        var service = NewService();
        var repository = ServiceProvider.GetRequiredService<IRepository<Message, Guid>>();
        var cutoff = MinutesAgo(15);

        var first = await service.ClaimDueScheduledAsync(repository, cutoff, 50, CancellationToken.None);
        DbContext.ChangeTracker.Clear();
        var second = await service.ClaimDueScheduledAsync(repository, cutoff, 50, CancellationToken.None);

        first.ShouldBe([messageId]);
        second.ShouldBeEmpty();
    }

    /// <summary>
    /// ★★ 真正的并发形态：两个实例各自选出了同一条，随后才轮到各自去 UPDATE。
    /// 只有条件写在 UPDATE 里，后到的那一次才会认领失败。
    /// </summary>
    /// <remarks>
    /// ★ 上面那条<b>验不出这件事</b>：第一次认领之后候选查询本身就把这条排除了，
    /// 于是把 UPDATE 里的条件删掉它照样绿。所以这里直接在"已选出、未 UPDATE"这个
    /// 中间态上做文章 —— 先让别人把行改掉，再来认领。
    /// </remarks>
    [Fact]
    public async Task AMessageAnotherScannerGrabbedFirst_CannotBeClaimed()
    {
        var messageId = await SeedAsync(NotificationStatus.Scheduled, scheduledTime: MinutesAgo(60));
        var repository = ServiceProvider.GetRequiredService<IRepository<Message, Guid>>();

        // 另一个实例抢先一步：行已经不是 Scheduled 了，而我们手上还拿着刚才选出的 id。
        await DbContext.Set<Message>().Where(m => m.Id == messageId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, NotificationStatus.Sending));
        DbContext.ChangeTracker.Clear();

        var claimed = await NotificationDispatchBackgroundService.TryClaimScheduledAsync(
            repository, messageId, CancellationToken.None);

        claimed.ShouldBeFalse();
    }

    /// <summary>
    /// ★★★ 一条**正在发送**的定时消息不能被到期扫描抢走。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这是本机制最贵的失败形态，也是它一度真的存在的地方：`SendAsync` 曾把
    /// <c>Status = Sending</c> 只写在内存里、直到整个收件人循环跑完才落库，于是一次几十分钟的
    /// 群发期间库里那行一直写着 <c>Scheduled</c> —— 到期扫描会**合法地**认领它，
    /// 第二次 `SendAsync` 看到全部收件人仍是 <c>Pending</c>，把已经发出去的那些再发一遍。
    /// </para>
    /// <para>
    /// ★ <b>宽限期挡不住这个</b>：定时器到点只是<b>入队</b>，队列单读者串行执行，
    /// 真正开发的时刻取决于积压 —— 可以远远晚于 <c>ScheduledTime + 宽限</c>。
    /// 挡住它的是「进循环之前就落库 Sending」+ 条件认领这一对。
    /// </para>
    /// <para>
    /// ★★ <b>探针必须在循环<u>内</u></b>：跑完整个 <c>SendAsync</c> 再认领是问不出问题的 ——
    /// 末尾那次保存已经把行改成 <c>Sent</c>，两种实现都会让认领失败。所以这里让邮件发送器的
    /// 回调（逐个收件人被调用，正处在循环中途）去尝试认领。<c>ExecuteUpdateAsync</c> 绕开
    /// 变更跟踪器直接读写库，看到的就是**已提交**的状态。
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AMessageBeingSentRightNow_CannotBeClaimedByTheDueScan()
    {
        var messageId = await SeedAsync(NotificationStatus.Scheduled, scheduledTime: MinutesAgo(60));
        var repository = ServiceProvider.GetRequiredService<IRepository<Message, Guid>>();

        bool? claimedMidFlight = null;
        _emailSender
            .Setup(s => s.SendToAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<List<EmailAttachment>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                claimedMidFlight ??= NotificationDispatchBackgroundService
                    .TryClaimScheduledAsync(repository, messageId, CancellationToken.None)
                    .GetAwaiter().GetResult();
                return SendResult.CreateSuccess("stub-id");
            });

        await ServiceProvider.GetRequiredService<INotificationService>().SendAsync(messageId);

        claimedMidFlight.ShouldNotBeNull("the probe never ran - the send did not reach a recipient");
        claimedMidFlight!.Value.ShouldBeFalse();
    }

    /// <summary>对照：没人抢的时候认领必须成功（否则上一条是在验一个恒假的条件）。</summary>
    [Fact]
    public async Task AMessageNobodyGrabbed_IsClaimed()
    {
        var messageId = await SeedAsync(NotificationStatus.Scheduled, scheduledTime: MinutesAgo(60));
        var repository = ServiceProvider.GetRequiredService<IRepository<Message, Guid>>();

        var claimed = await NotificationDispatchBackgroundService.TryClaimScheduledAsync(
            repository, messageId, CancellationToken.None);

        claimed.ShouldBeTrue();
    }

    /// <summary>认领把消息推进到 Sending 并重新计时，否则同一轮里它又会被当成"卡了很久"。</summary>
    [Fact]
    public async Task ClaimingMovesTheMessageOutOfScheduled()
    {
        var messageId = await SeedAsync(NotificationStatus.Scheduled, scheduledTime: MinutesAgo(60));
        var repository = ServiceProvider.GetRequiredService<IRepository<Message, Guid>>();

        await NewService().ClaimDueScheduledAsync(repository, MinutesAgo(15), 50, CancellationToken.None);

        var message = await ReloadAsync(messageId);
        message.Status.ShouldBe(NotificationStatus.Sending);
        message.LastModificationTime!.Value.ShouldBeGreaterThan(MinutesAgo(1));
    }

    /// <summary>一轮里最多接手 RecoveryBatchSize 条 —— 否则一次积压能把发送账号打停。</summary>
    [Fact]
    public async Task TheClaimRespectsTheBatchSize()
    {
        for (var i = 0; i < 3; i++)
            await SeedAsync(NotificationStatus.Scheduled, scheduledTime: MinutesAgo(60 + i));
        var repository = ServiceProvider.GetRequiredService<IRepository<Message, Guid>>();

        var claimed = await NewService().ClaimDueScheduledAsync(repository, MinutesAgo(15), batchSize: 2, CancellationToken.None);

        claimed.Count.ShouldBe(2);
    }

    // ── 夹具 ─────────────────────────────────────────────────────────────────

    private static DateTime MinutesAgo(int minutes) => DateTime.UtcNow.AddMinutes(-minutes);

    private NotificationDispatchBackgroundService NewService()
        => new(ServiceProvider,
               ServiceProvider.GetRequiredService<IOptionsMonitor<NotificationOptions>>(),
               new Mock<ILogger<NotificationDispatchBackgroundService>>().Object);

    private Task RecoverOnceAsync() => NewService().RecoverOnceAsync(_dispatch, CancellationToken.None);

    private void VerifySent(Times times) => _emailSender.Verify(s => s.SendToAsync(
        It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(),
        It.IsAny<bool>(), It.IsAny<List<EmailAttachment>?>(), It.IsAny<CancellationToken>()), times);

    private async Task<Message> ReloadAsync(Guid messageId)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Set<Message>().AsNoTracking().FirstAsync(m => m.Id == messageId);
    }

    private async Task<Guid> SeedAsync(
        NotificationStatus status, DateTime? scheduledTime = null, DateTime? lastModified = null)
    {
        var message = new Message
        {
            Id = Guid.NewGuid(),
            Subject = "Quarterly newsletter",
            Content = "Body",
            Type = NotificationType.Email,
            Category = "General",
            IsTransactional = true,
            Status = status,
            ScheduledTime = scheduledTime,
            TotalRecipientCount = 1,
            Recipients = [new Recipient { Address = "reader@example.com", Status = NotificationStatus.Pending }],
        };

        await DbContext.Messages.AddAsync(message);
        await DbContext.SaveChangesAsync();

        if (lastModified != null)
        {
            // ★ 必须绕开变更跟踪器：审计拦截器在每次 SaveChanges 时把 LastModificationTime
            // 覆写成"现在"，所以直接赋值再保存是回不到过去的（这条正是本文件第一版的假失败）。
            await DbContext.Set<Message>().Where(m => m.Id == message.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.LastModificationTime, lastModified));
        }

        DbContext.ChangeTracker.Clear();
        return message.Id;
    }
}
