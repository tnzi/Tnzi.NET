using Tnzi.Domain.Entities;
using Tnzi.Notification.Metadata;

namespace Tnzi.Notification.Tests.Integration;

/// <summary>
/// 每小时上限在<b>真实发送路径</b>上确实生效：真库、真
/// <see cref="NotificationPreferenceService"/>、真 <see cref="NotificationService.SendAsync"/>。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>为什么必须是这一层</b>：判定规则由 <c>FrequencyCapFilterTests</c>（纯函数）覆盖，
/// 但这个缺陷的形态<b>从来不是判定写错了</b> —— <see cref="Preference.MaxFrequencyPerHour"/>
/// 全仓零处读取。用户设了「每小时最多一封」，界面显示得好好的，然后一小时收五十封。
/// 只测纯函数就等于把退订（2026-08-08）与渠道开关（2026-08-09）那两个坑再挖一遍。
/// </para>
/// <para>
/// ★ 每条「不该发」都配一条「应该发」的对照。
/// </para>
/// </remarks>
public class FrequencyCapSendPathTests : IntegrationTestBase
{
    private static readonly Guid Alice = Guid.Parse("22222222-2222-2222-2222-222222222222");
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
    }

    private static void AddRepo<TEntity>(IServiceCollection services) where TEntity : class, IEntity<Guid>
    {
        services.AddScoped<IRepository<TEntity, Guid>>(sp =>
            new EFCoreRepository<NotificationTestDbContext, TEntity, Guid>(
                sp.GetRequiredService<NotificationTestDbContext>(), serviceProvider: sp));
        services.AddScoped<IReadOnlyRepository<TEntity, Guid>>(sp =>
            sp.GetRequiredService<IRepository<TEntity, Guid>>());
    }

    // ── 不该发 ───────────────────────────────────────────────────────────────

    /// <summary>★★ 上限 1、这一小时已经收过一封，第二封就不该发出去。</summary>
    [Fact]
    public async Task AUserAtTheirHourlyLimit_IsNotSentToAgain()
    {
        await SetCapAsync(1);
        await SeedAlreadyDeliveredAsync(NotificationType.Email, MinutesAgo(10));
        var messageId = await SeedMessageAsync(Alice, isTransactional: false);

        await SendAsync(messageId);

        VerifySent(Times.Never());
    }

    /// <summary>被拦下的收件人标 <c>Cancelled</c> 并写明原因，而且真的落库。</summary>
    [Fact]
    public async Task TheBlockedMarking_IsPersisted()
    {
        await SetCapAsync(1);
        await SeedAlreadyDeliveredAsync(NotificationType.Email, MinutesAgo(10));
        var messageId = await SeedMessageAsync(Alice, isTransactional: false);

        await SendAsync(messageId);

        DbContext.ChangeTracker.Clear();
        var recipient = await DbContext.Set<Recipient>().AsNoTracking()
            .FirstAsync(r => r.MessageId == messageId);
        recipient.Status.ShouldBe(NotificationStatus.Cancelled);
        recipient.FailureReason.ShouldBe(FrequencyCapFilter.OverFrequencyCapReason);
    }

    /// <summary>
    /// ★ 同一批里同一个人出现两次，上限也要算得住 —— 只看库里的条数会让一次群发
    /// 里给同一个人发的每一条都放行，而那正是上限要拦的事。
    /// </summary>
    [Fact]
    public async Task TheLimitIsCountedWithinASingleBatchToo()
    {
        await SetCapAsync(1);
        var messageId = await SeedMessageAsync(Alice, isTransactional: false, recipientCount: 2);

        await SendAsync(messageId);

        VerifySent(Times.Once());
        DbContext.ChangeTracker.Clear();
        var recipients = await DbContext.Set<Recipient>().AsNoTracking()
            .Where(r => r.MessageId == messageId).ToListAsync();
        recipients.Count(r => r.Status == NotificationStatus.Sent).ShouldBe(1);
        recipients.Count(r => r.Status == NotificationStatus.Cancelled).ShouldBe(1);
    }

    /// <summary>
    /// 被上限拦下的人不能被重发路径捞回来 —— 标 <c>Failed</c> 就等于开一条绕过上限的后门。
    /// </summary>
    [Fact]
    public async Task ARecipientBlockedByTheLimit_IsNotResendable()
    {
        await SetCapAsync(1);
        await SeedAlreadyDeliveredAsync(NotificationType.Email, MinutesAgo(10));
        var messageId = await SeedMessageAsync(Alice, isTransactional: false);
        await SendAsync(messageId);

        var resent = await ServiceProvider.GetRequiredService<INotificationService>()
            .ResendToFailedRecipientsAsync(messageId);

        resent.Succeeded.ShouldBeTrue(resent.Message);
        resent.Data.ShouldBe(0);
        VerifySent(Times.Never());
    }

    // ── 应该发（对照）────────────────────────────────────────────────────────

    /// <summary>上限 3、这一小时才收过一封，照发。</summary>
    [Fact]
    public async Task AUserBelowTheirLimit_IsStillSentTo()
    {
        await SetCapAsync(3);
        await SeedAlreadyDeliveredAsync(NotificationType.Email, MinutesAgo(10));
        var messageId = await SeedMessageAsync(Alice, isTransactional: false);

        await SendAsync(messageId);

        VerifySent(Times.Once());
    }

    /// <summary>★★ 事务性消息不受上限约束 —— 把营销邮件限到每小时一封的人仍要收到验证码。</summary>
    [Fact]
    public async Task ATransactionalMessage_IgnoresTheLimit()
    {
        await SetCapAsync(1);
        await SeedAlreadyDeliveredAsync(NotificationType.Email, MinutesAgo(10));
        var messageId = await SeedMessageAsync(Alice, isTransactional: true);

        await SendAsync(messageId);

        VerifySent(Times.Once());
    }

    /// <summary>窗口是一小时：更早的投递不该继续占名额。</summary>
    [Fact]
    public async Task ADeliveryOlderThanTheWindow_DoesNotCount()
    {
        await SetCapAsync(1);
        await SeedAlreadyDeliveredAsync(NotificationType.Email, MinutesAgo(90));
        var messageId = await SeedMessageAsync(Alice, isTransactional: false);

        await SendAsync(messageId);

        VerifySent(Times.Once());
    }

    /// <summary>上限按渠道算：短信收得多不该让邮件发不出去。</summary>
    [Fact]
    public async Task ADeliveryOnAnotherChannel_DoesNotCount()
    {
        await SetCapAsync(1);
        await SeedAlreadyDeliveredAsync(NotificationType.Sms, MinutesAgo(10));
        var messageId = await SeedMessageAsync(Alice, isTransactional: false);

        await SendAsync(messageId);

        VerifySent(Times.Once());
    }

    /// <summary>没设上限的人完全不受影响（默认绝不能是「有偏好行就限」）。</summary>
    [Fact]
    public async Task AUserWithNoLimit_IsUnaffected()
    {
        await SetPreferenceAsync(cap: null);
        await SeedAlreadyDeliveredAsync(NotificationType.Email, MinutesAgo(10));
        var messageId = await SeedMessageAsync(Alice, isTransactional: false);

        await SendAsync(messageId);

        VerifySent(Times.Once());
    }

    /// <summary>★ 上限按人算，纯地址收件人没有偏好行，必须原样放行。</summary>
    [Fact]
    public async Task AnAddressOnlyRecipient_IsUnaffected()
    {
        await SetCapAsync(1);
        await SeedAlreadyDeliveredAsync(NotificationType.Email, MinutesAgo(10));
        var messageId = await SeedMessageAsync(userId: null, isTransactional: false);

        await SendAsync(messageId);

        VerifySent(Times.Once());
    }

    /// <summary>
    /// 一条被拦光的消息不能显示成「已发送」 —— 与退订那条规则同源。
    /// </summary>
    [Fact]
    public async Task AMessageWhoseEveryRecipientIsCapped_IsNotMarkedSent()
    {
        await SetCapAsync(1);
        await SeedAlreadyDeliveredAsync(NotificationType.Email, MinutesAgo(10));
        var messageId = await SeedMessageAsync(Alice, isTransactional: false);

        await SendAsync(messageId);

        DbContext.ChangeTracker.Clear();
        var message = await DbContext.Set<Message>().AsNoTracking().FirstAsync(m => m.Id == messageId);
        message.Status.ShouldBe(NotificationStatus.Cancelled);
    }

    // ── 夹具 ─────────────────────────────────────────────────────────────────

    private static DateTime MinutesAgo(int minutes) => DateTime.UtcNow.AddMinutes(-minutes);

    private void VerifySent(Times times) => _emailSender.Verify(s => s.SendToAsync(
        It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(),
        It.IsAny<bool>(), It.IsAny<List<EmailAttachment>?>(), It.IsAny<CancellationToken>()), times);

    private Task SetCapAsync(int cap) => SetPreferenceAsync(cap);

    private async Task SetPreferenceAsync(int? cap)
    {
        var result = await ServiceProvider.GetRequiredService<INotificationPreferenceService>()
            .SetPreferenceAsync(Alice, new SetNotificationPreferenceDto
            {
                Channel = "Email",
                IsEnabled = true,
                MaxFrequencyPerHour = cap,
            });
        result.Succeeded.ShouldBeTrue(result.Message);
        DbContext.ChangeTracker.Clear();
    }

    /// <summary>造一条「这一小时已经送达过」的历史记录。</summary>
    private async Task SeedAlreadyDeliveredAsync(NotificationType channel, DateTime sentTime)
    {
        var message = new Message
        {
            Id = Guid.NewGuid(),
            Subject = "Earlier message",
            Content = "Body",
            Type = channel,
            Category = "Marketing",
            Status = NotificationStatus.Sent,
            TotalRecipientCount = 1,
            SuccessCount = 1,
            Recipients =
            [
                new Recipient
                {
                    Address = channel == NotificationType.Sms ? "+16135550134" : "alice@example.com",
                    UserId = Alice,
                    Status = NotificationStatus.Sent,
                    SentTime = sentTime,
                }
            ],
        };

        await DbContext.Messages.AddAsync(message);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private async Task<Guid> SeedMessageAsync(Guid? userId, bool isTransactional, int recipientCount = 1)
    {
        var message = new Message
        {
            Id = Guid.NewGuid(),
            Subject = "Spring sale",
            Content = "Body",
            Type = NotificationType.Email,
            Category = "Marketing",
            IsTransactional = isTransactional,
            Status = NotificationStatus.Pending,
            TotalRecipientCount = recipientCount,
            Recipients =
            [
                .. Enumerable.Range(0, recipientCount).Select(i => new Recipient
                {
                    Address = $"alice+{i}@example.com",
                    UserId = userId,
                    Status = NotificationStatus.Pending,
                })
            ],
        };

        await DbContext.Messages.AddAsync(message);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return message.Id;
    }

    private Task<Result> SendAsync(Guid messageId)
        => ServiceProvider.GetRequiredService<INotificationService>().SendAsync(messageId);
}
