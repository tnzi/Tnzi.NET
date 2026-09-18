using Tnzi.Domain.Entities;
using Tnzi.Notification.Services.Internal;

namespace Tnzi.Notification.Tests.Integration;

/// <summary>
/// 一次<b>正在飞</b>的群发不会被恢复扫描当成「卡住」接手过去再发一遍 —— 在真库上验证。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ <b>补的是哪个洞。</b><c>SendAsync</c> 曾把整批收件人的投递结果攒到循环结束后
/// <b>一次</b> <c>SaveChangesAsync</c> 才落库。于是在整个循环期间，库里
/// ① 每个收件人都还写着 <c>Pending</c>，② 消息行的 <c>LastModificationTime</c> 停在进循环之前。
/// 而 <c>NotificationDispatchBackgroundService</c> 判「卡住」的依据正是
/// <c>Status == Sending &amp;&amp; (LastModificationTime ?? CreationTime) &lt; cutoff</c>：
/// 一次一千人的串行群发跑过 <c>StuckAfterMinutes</c>（默认 15 分钟）就会被另一个作用域
/// <b>合法地</b>接手，第二次 <c>SendAsync</c> 看到全部收件人仍是 <c>Pending</c> ——
/// <b>已经发出去的那些全部再发一遍</b>。
/// </para>
/// <para>
/// ★ <b>探针必须在循环<u>内</u></b>：跑完整个 <c>SendAsync</c> 再去看是问不出问题的 ——
/// 末尾那次保存把一切都写对了，两种实现都通过。所以这里让邮件发送器的回调
/// （逐个收件人被调用，正处在循环中途）去读库。这与
/// <see cref="ScheduledDispatchRecoveryTests.AMessageBeingSentRightNow_CannotBeClaimedByTheDueScan"/>
/// 是同一条教训的第三次应用。
/// </para>
/// <para>
/// ★ 每次投递里那个 <c>Task.Delay</c> 不是装饰：断言要区分「循环开始时那次写入」与
/// 「循环中途那次心跳」两个时间戳，而 Windows 上 <c>DateTime.UtcNow</c> 的分辨率约 15ms ——
/// 不让循环真的花掉一点时间，两个时间戳会落在同一个刻度上，那时红绿都不说明问题。
/// </para>
/// </remarks>
public class InFlightBatchTests : IntegrationTestBase
{
    /// <summary>收件人数量跨过一次分片（<c>SendProgress.FlushEveryRecipients</c> = 20）。</summary>
    private const int RecipientCount = 25;

    /// <summary>探针落在第几个收件人上 —— 必须在第一次分片落库之后。</summary>
    private const int ProbeAt = 22;

    private readonly Mock<IEmailSender> _emailSender = new();
    private readonly DispatchOptions _dispatch = new()
    {
        EnableRecovery = true,
        StuckAfterMinutes = 15,
        RecoveryBatchSize = 50,
        RatePerMinute = 0,
    };

    private int _sent;
    private DateTime? _cutoffTakenEarly;
    private bool? _claimableMidFlight;
    private int _recordedAsSentMidFlight = -1;

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
            .Returns(async () =>
            {
                // 让循环真的花掉时间，两次写入的时间戳才分得开（见类注释）。
                await Task.Delay(5);
                await OnRecipientSentAsync();
                return SendResult.CreateSuccess("stub-id");
            });

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

    // ── 正在飞的批次 ─────────────────────────────────────────────────────────

    /// <summary>
    /// ★★★ 循环跑到一半时，已经投递出去的收件人必须<b>已经在库里</b>。
    /// 留在内存里的话，进程此刻退出 = 整份名单重发。
    /// </summary>
    [Fact]
    public async Task AMidFlightBatch_HasAlreadyRecordedTheRecipientsItFinished()
    {
        var messageId = await SeedAsync();

        await SendAsync(messageId);

        _recordedAsSentMidFlight.ShouldBeGreaterThanOrEqualTo(
            SendProgress.FlushEveryRecipients,
            "the send loop reached recipient #" + ProbeAt + " without persisting a single delivery");
    }

    /// <summary>
    /// ★★★ 循环跑到一半时，消息的心跳已经推进过，于是「卡住」扫描认领不到它。
    /// </summary>
    /// <remarks>
    /// cutoff 取自第一个收件人投递完的那一刻。心跳若从不推进，
    /// <c>LastModificationTime</c> 就停在进循环之前那次写入 —— 早于 cutoff，认领成功，
    /// 而那次认领的下一步就是把这批人再发一遍。
    /// </remarks>
    [Fact]
    public async Task AMidFlightBatch_CannotBeClaimedByTheStalledScan()
    {
        var messageId = await SeedAsync();

        await SendAsync(messageId);

        _claimableMidFlight.ShouldNotBeNull("the probe never ran - the send did not reach recipient #" + ProbeAt);
        _claimableMidFlight!.Value.ShouldBeFalse();
    }

    /// <summary>对照：整批发完之后，每个人都记成 Sent，一个不多一个不少。</summary>
    [Fact]
    public async Task TheWholeBatch_IsDeliveredExactlyOnce()
    {
        var messageId = await SeedAsync();

        await SendAsync(messageId);

        _sent.ShouldBe(RecipientCount);
        DbContext.ChangeTracker.Clear();
        (await DbContext.Set<Recipient>().AsNoTracking()
            .CountAsync(r => r.MessageId == messageId && r.Status == NotificationStatus.Sent))
            .ShouldBe(RecipientCount);
    }

    // ── 真的卡住的批次 ───────────────────────────────────────────────────────

    /// <summary>
    /// ★★ 两个实例各自选出了同一条卡住的批次，只有一个能认领 ——
    /// 否则同一批收件人被两个作用域各发一遍。
    /// </summary>
    /// <remarks>
    /// 租约就用 <c>LastModificationTime</c> 本身：认领把它推到现在，条件里带着
    /// 「它现在仍然早于 cutoff」。条件<b>必须写在 UPDATE 里</b> —— 留在上一步的 SELECT 上
    /// 等于两边都认为自己抢到了。
    /// </remarks>
    [Fact]
    public async Task OnlyOneScanner_CanClaimAStalledBatch()
    {
        var messageId = await SeedAsync(status: NotificationStatus.Sending, lastModified: MinutesAgo(60));
        var repository = ServiceProvider.GetRequiredService<IRepository<Message, Guid>>();
        var cutoff = MinutesAgo(15);

        var first = await NotificationDispatchBackgroundService.TryClaimStalledAsync(
            repository, messageId, cutoff, CancellationToken.None);
        DbContext.ChangeTracker.Clear();
        var second = await NotificationDispatchBackgroundService.TryClaimStalledAsync(
            repository, messageId, cutoff, CancellationToken.None);

        first.ShouldBeTrue();
        second.ShouldBeFalse();
    }

    /// <summary>对照：没人抢的时候认领必须成功（否则上一条是在验一个恒假的条件）。</summary>
    [Fact]
    public async Task AStalledBatchNobodyGrabbed_IsClaimed()
    {
        var messageId = await SeedAsync(status: NotificationStatus.Sending, lastModified: MinutesAgo(60));
        var repository = ServiceProvider.GetRequiredService<IRepository<Message, Guid>>();

        var claimed = await NotificationDispatchBackgroundService.TryClaimStalledAsync(
            repository, messageId, MinutesAgo(15), CancellationToken.None);

        claimed.ShouldBeTrue();
    }

    /// <summary>刚刚还在推进的 Sending 批次不是「卡住」，认领不到。</summary>
    [Fact]
    public async Task ARecentlyActiveSendingBatch_IsNotClaimable()
    {
        var messageId = await SeedAsync(status: NotificationStatus.Sending);
        var repository = ServiceProvider.GetRequiredService<IRepository<Message, Guid>>();

        var claimed = await NotificationDispatchBackgroundService.TryClaimStalledAsync(
            repository, messageId, MinutesAgo(15), CancellationToken.None);

        claimed.ShouldBeFalse();
    }

    // ── 夹具 ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// 每个收件人投递完时跑一次：记录序号、在中途取一次探针。
    /// </summary>
    private async Task OnRecipientSentAsync()
    {
        _sent++;

        if (_sent == 1)
        {
            // 进循环之后的第一个时刻。心跳不推进的话，消息行的时间戳永远早于这一刻。
            _cutoffTakenEarly = DateTime.UtcNow;
            return;
        }

        if (_sent != ProbeAt)
            return;

        var repository = ServiceProvider.GetRequiredService<IRepository<Message, Guid>>();
        var messageId = await DbContext.Set<Message>().AsNoTracking().Select(m => m.Id).FirstAsync();

        // ★ AsNoTracking 直接读库，看到的是**已提交**的状态，不受循环里那些被跟踪的实体影响。
        _recordedAsSentMidFlight = await DbContext.Set<Recipient>().AsNoTracking()
            .CountAsync(r => r.MessageId == messageId && r.Status == NotificationStatus.Sent);

        _claimableMidFlight = await NotificationDispatchBackgroundService.TryClaimStalledAsync(
            repository, messageId, _cutoffTakenEarly!.Value, CancellationToken.None);
    }

    private static DateTime MinutesAgo(int minutes) => DateTime.UtcNow.AddMinutes(-minutes);

    private Task<Result> SendAsync(Guid messageId)
        => ServiceProvider.GetRequiredService<INotificationService>().SendAsync(messageId);

    private async Task<Guid> SeedAsync(
        NotificationStatus status = NotificationStatus.Pending, DateTime? lastModified = null)
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
            TotalRecipientCount = RecipientCount,
            Recipients = [.. Enumerable.Range(0, RecipientCount)
                .Select(i => new Recipient { Address = $"reader{i}@example.com", Status = NotificationStatus.Pending })],
        };

        await DbContext.Messages.AddAsync(message);
        await DbContext.SaveChangesAsync();

        if (lastModified != null)
        {
            // ★ 必须绕开变更跟踪器：审计拦截器在每次 SaveChanges 时把 LastModificationTime
            // 覆写成"现在"，直接赋值再保存是回不到过去的。
            await DbContext.Set<Message>().Where(m => m.Id == message.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.LastModificationTime, lastModified));
        }

        DbContext.ChangeTracker.Clear();
        return message.Id;
    }
}
