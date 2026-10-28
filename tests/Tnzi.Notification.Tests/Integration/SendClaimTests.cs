using Tnzi.Domain.Entities;
using Tnzi.Notification.Services.Internal;

namespace Tnzi.Notification.Tests.Integration;

/// <summary>
/// 一条消息同一时刻只能有一个发送方：在真库上验证 <c>SendAsync</c> 自己的认领。
/// </summary>
/// <remarks>
/// <para>
/// ★★ <b>补的是哪个洞。</b>派发恢复的几遍扫描在交给发送路径之前会先做一次条件认领，
/// 但 <c>SendAsync</c> 本身只拒绝 <c>Sent</c> / <c>Cancelled</c> / 未到点的定时消息，然后无条件写 <c>Sending</c>。
/// 于是顺序一反就挡不住：恢复扫描先认领了一条到期的定时消息并开始发送，本进程那个定时器的工作项
/// 在单读者队列里排了一阵子之后才轮到，<c>SendAsync</c> 看到 <c>Sending</c> 照样往下走，
/// 把还没落库的那些收件人再发一遍。管理端对一条正在飞的消息点「发送」是同一回事。
/// </para>
/// <para>
/// ★ 恢复扫描自己认领过的消息走 <c>SendClaimedAsync</c>（不再认领一次）；其余入口都必须先抢到。
/// </para>
/// </remarks>
public class SendClaimTests : IntegrationTestBase
{
    private readonly Mock<IEmailSender> _emailSender = new();

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
        options.Setup(o => o.CurrentValue).Returns(new NotificationOptions { MaxConcurrency = 4 });
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

    // ── 已经有人在发 ─────────────────────────────────────────────────────────

    /// <summary>
    /// ★★★ 恢复扫描已经认领了一条到期的定时消息；那条消息的定时器工作项随后才轮到，它不能再发一遍。
    /// </summary>
    [Fact]
    public async Task TheTimerItem_ForAMessageTheRecoveryScanAlreadyClaimed_SendsNothing()
    {
        var messageId = await SeedAsync(NotificationStatus.Scheduled, scheduledTime: DateTime.UtcNow.AddMinutes(-60));
        var repository = ServiceProvider.GetRequiredService<IRepository<Message, Guid>>();
        (await NotificationDispatchBackgroundService.TryClaimScheduledAsync(repository, messageId, CancellationToken.None))
            .ShouldBeTrue();
        DbContext.ChangeTracker.Clear();

        var result = await SendAsync(messageId);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(409);
        VerifySent(Times.Never());
    }

    /// <summary>★★ 管理端对一条正在发送的消息再点一次「发送」：拒绝，而不是把没落库的那些人再发一遍。</summary>
    [Fact]
    public async Task SendingAMessageThatIsAlreadyBeingSent_IsRefused()
    {
        var messageId = await SeedAsync(NotificationStatus.Sending);

        var result = await SendAsync(messageId);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(409);
        VerifySent(Times.Never());
        (await ReloadAsync(messageId)).RetryCount.ShouldBe(0);
    }

    /// <summary>
    /// ★★ 真正的并发形态：第二个发送方在第一个的收件人循环<b>中途</b>到达：
    /// 此刻库里那些还没分片落库的收件人仍写着 <c>Pending</c>。
    /// </summary>
    [Fact]
    public async Task ASecondSender_ArrivingMidFlight_SendsNothing()
    {
        var messageId = await SeedAsync(NotificationStatus.Pending);
        Result? midFlight = null;
        var probed = false;
        _emailSender
            .Setup(s => s.SendToAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<List<EmailAttachment>?>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                // 先置位再进去：第二个发送方若没被挡住，它自己的投递也会回到这里：不置位就是无限递归。
                if (!probed)
                {
                    probed = true;
                    using var other = ServiceProvider.CreateScope();
                    midFlight = await other.ServiceProvider.GetRequiredService<INotificationService>().SendAsync(messageId);
                }
                return SendResult.CreateSuccess("stub-id");
            });

        await SendAsync(messageId);

        midFlight.ShouldNotBeNull("the probe never ran - the first send did not reach a recipient");
        midFlight!.Succeeded.ShouldBeFalse();
        VerifySent(Times.Once());
    }

    /// <summary>
    /// ★★★ 最窄的那个窗口：第二个发送方<b>在第一个认领之前</b>就读到了这一行（读到的是 <c>Pending</c>），
    /// 认领发生在两次读写之间。只有把条件写进 UPDATE，它才会抢不到。
    /// </summary>
    /// <remarks>
    /// ★ 上一条验不出这件事：第二个发送方读到的已经是 <c>Sending</c>，单凭「Sending 答 409」就挡住了。
    /// 这里让第二个作用域先把行跟踪成 <c>Pending</c>：带跟踪的查询对已跟踪的实体保留内存里的值，
    /// 于是它进 <c>SendAsync</c> 时看到的仍是认领之前的状态，只剩条件更新能拦它。
    /// </remarks>
    [Fact]
    public async Task ASecondSender_ThatReadTheRowBeforeTheFirstClaimedIt_SendsNothing()
    {
        var messageId = await SeedAsync(NotificationStatus.Pending);
        using var other = ServiceProvider.CreateScope();
        var otherContext = other.ServiceProvider.GetRequiredService<NotificationTestDbContext>();
        (await otherContext.Set<Message>().Include(m => m.Recipients).FirstAsync(m => m.Id == messageId))
            .Status.ShouldBe(NotificationStatus.Pending);

        Result? late = null;
        var probed = false;
        _emailSender
            .Setup(s => s.SendToAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<List<EmailAttachment>?>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                if (!probed)
                {
                    probed = true;
                    late = await other.ServiceProvider.GetRequiredService<INotificationService>().SendAsync(messageId);
                }
                return SendResult.CreateSuccess("stub-id");
            });

        await SendAsync(messageId);

        late.ShouldNotBeNull("the probe never ran - the first send did not reach a recipient");
        late!.Succeeded.ShouldBeFalse();
        late.Code.ShouldBe(409);
        VerifySent(Times.Once());
    }

    /// <summary>
    /// ★★ 同形的另一个入口：「重发失败项」绕开 <c>SendAsync</c> 自己投递，管理端连点两下就是两个并发的重发：
    /// 第二个必须抢不到，否则同一批失败的收件人各收两封。
    /// </summary>
    [Fact]
    public async Task ASecondResend_ArrivingMidFlight_SendsNothing()
    {
        var messageId = await SeedAsync(NotificationStatus.Failed, recipientStatus: NotificationStatus.Failed);
        Result<int>? midFlight = null;
        var probed = false;
        _emailSender
            .Setup(s => s.SendToAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<List<EmailAttachment>?>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                if (!probed)
                {
                    probed = true;
                    using var other = ServiceProvider.CreateScope();
                    midFlight = await other.ServiceProvider.GetRequiredService<INotificationService>().ResendToFailedRecipientsAsync(messageId);
                }
                return SendResult.CreateSuccess("stub-id");
            });

        var first = await ServiceProvider.GetRequiredService<INotificationService>().ResendToFailedRecipientsAsync(messageId);

        first.Succeeded.ShouldBeTrue(first.Message);
        midFlight.ShouldNotBeNull("the probe never ran - the first resend did not reach a recipient");
        midFlight!.Succeeded.ShouldBeFalse();
        midFlight.Code.ShouldBe(409);
        VerifySent(Times.Once());
        (await ReloadAsync(messageId)).Status.ShouldBe(NotificationStatus.Sent);
    }

    /// <summary>对照：重发全部再次失败时消息落回 Failed，而不是停在认领时写下的 Sending。</summary>
    [Fact]
    public async Task AResendThatFailsAgain_LeavesTheMessageFailed()
    {
        var messageId = await SeedAsync(NotificationStatus.Failed, recipientStatus: NotificationStatus.Failed);
        _emailSender
            .Setup(s => s.SendToAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<List<EmailAttachment>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SendResult.CreateFailure("SMTP 451"));

        await ServiceProvider.GetRequiredService<INotificationService>().ResendToFailedRecipientsAsync(messageId);

        (await ReloadAsync(messageId)).Status.ShouldBe(NotificationStatus.Failed);
    }

    // ── 抢到的那一个照常发 ───────────────────────────────────────────────────

    /// <summary>对照：没人抢的待发消息照常发出去（否则上面几条是在验一个恒假的条件）。</summary>
    [Fact]
    public async Task APendingMessageNobodyIsSending_IsSent()
    {
        var messageId = await SeedAsync(NotificationStatus.Pending);

        var result = await SendAsync(messageId);

        result.Succeeded.ShouldBeTrue(result.Message);
        VerifySent(Times.Once());
        (await ReloadAsync(messageId)).Status.ShouldBe(NotificationStatus.Sent);
    }

    /// <summary>对照：到点的定时消息由它自己的定时器发出去。</summary>
    [Fact]
    public async Task ADueScheduledMessage_IsSentByItsTimer()
    {
        var messageId = await SeedAsync(NotificationStatus.Scheduled, scheduledTime: DateTime.UtcNow.AddMinutes(-1));

        var result = await SendAsync(messageId);

        result.Succeeded.ShouldBeTrue(result.Message);
        VerifySent(Times.Once());
    }

    /// <summary>
    /// ★ 恢复扫描认领之后走 <c>SendClaimedAsync</c>：它手上就是那份认领，不能被自己的认领挡在门外。
    /// </summary>
    [Fact]
    public async Task TheScanThatHoldsTheClaim_SendsIt()
    {
        var messageId = await SeedAsync(NotificationStatus.Scheduled, scheduledTime: DateTime.UtcNow.AddMinutes(-60));
        var repository = ServiceProvider.GetRequiredService<IRepository<Message, Guid>>();
        (await NotificationDispatchBackgroundService.TryClaimScheduledAsync(repository, messageId, CancellationToken.None))
            .ShouldBeTrue();
        DbContext.ChangeTracker.Clear();

        var service = (NotificationService)ServiceProvider.GetRequiredService<INotificationService>();
        var result = await service.SendClaimedAsync(messageId);

        result.Succeeded.ShouldBeTrue(result.Message);
        VerifySent(Times.Once());
    }

    /// <summary>
    /// ★ 认领本身：同一个起始状态只能被抢到一次（条件写在 UPDATE 里）。
    /// </summary>
    [Fact]
    public async Task TheClaim_SucceedsOnlyOnce()
    {
        var messageId = await SeedAsync(NotificationStatus.Pending);
        var repository = ServiceProvider.GetRequiredService<IRepository<Message, Guid>>();

        var first = await MessageClaim.TryClaimAsync(repository, messageId, NotificationStatus.Pending, CancellationToken.None);
        var second = await MessageClaim.TryClaimAsync(repository, messageId, NotificationStatus.Pending, CancellationToken.None);

        first.ShouldBeTrue();
        second.ShouldBeFalse();
    }

    // ── 夹具 ─────────────────────────────────────────────────────────────────

    private Task<Result> SendAsync(Guid messageId)
        => ServiceProvider.GetRequiredService<INotificationService>().SendAsync(messageId);

    private void VerifySent(Times times) => _emailSender.Verify(s => s.SendToAsync(
        It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(),
        It.IsAny<bool>(), It.IsAny<List<EmailAttachment>?>(), It.IsAny<CancellationToken>()), times);

    private async Task<Message> ReloadAsync(Guid messageId)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Set<Message>().AsNoTracking().FirstAsync(m => m.Id == messageId);
    }

    private async Task<Guid> SeedAsync(
        NotificationStatus status, DateTime? scheduledTime = null, NotificationStatus recipientStatus = NotificationStatus.Pending)
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
            Recipients = [new Recipient { Address = "reader@example.com", Status = recipientStatus }],
        };

        await DbContext.Messages.AddAsync(message);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return message.Id;
    }
}
