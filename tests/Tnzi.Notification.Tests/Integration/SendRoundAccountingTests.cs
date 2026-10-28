using Tnzi.Domain.Entities;

namespace Tnzi.Notification.Tests.Integration;

/// <summary>
/// 一条消息会被发送路径走好几遍（首发、续发、静默时段延后到点），每一遍只该为它自己记账。真库、真 <see cref="NotificationService"/>。
/// </summary>
/// <remarks>
/// <para>
/// <b>被保护的缺陷一</b>：<c>RetryCount</c> 在每次进入发送路径时加一。首次发送、续发、延后到点那一遍都各算一次，
/// 一条延后过两次的消息还没真正重试过就到了 <c>MaxRetryCount</c>，之后真失败了「重试」答「已到上限」、「重试全部失败」直接跳过它。
/// </para>
/// <para>
/// <b>被保护的缺陷二</b>：「这一轮没人可发」时的收尾只看这一轮。昨晚已经送达了一部分、剩下被延后的人到点时被退订或上限拦下，
/// 整条消息就被写成 <c>Cancelled</c>，与「从来没人收到」长得一样。
/// </para>
/// </remarks>
public class SendRoundAccountingTests : IntegrationTestBase
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
        services.AddScoped<INotificationRetryService, NotificationRetryService>();
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

    [Fact]
    public async Task AFirstSend_IsNotARetry()
    {
        var messageId = await SeedAsync(NotificationStatus.Pending, maxRetryCount: 3,
            new Recipient { Address = "alice@example.com", Status = NotificationStatus.Pending });

        var result = await ServiceProvider.GetRequiredService<INotificationService>().SendAsync(messageId);

        result.Succeeded.ShouldBeTrue(result.Message);
        var message = await ReloadAsync(messageId);
        message.Status.ShouldBe(NotificationStatus.Sent);
        message.RetryCount.ShouldBe(0);
    }

    [Fact]
    public async Task ARetry_CountsOnce_AndTheLastAllowedRetryStillRuns()
    {
        var messageId = await SeedAsync(NotificationStatus.Failed, maxRetryCount: 1,
            new Recipient { Address = "alice@example.com", Status = NotificationStatus.Failed, FailureReason = "SMTP 421" });

        var result = await ServiceProvider.GetRequiredService<INotificationRetryService>().RetryAsync(messageId);

        result.Succeeded.ShouldBeTrue(result.Message);
        var message = await ReloadAsync(messageId);
        message.RetryCount.ShouldBe(1);
        message.Status.ShouldBe(NotificationStatus.Sent);
    }

    [Fact]
    public async Task ADeferredPassWhoseRecipientsAreAllBlocked_AfterEarlierDeliveries_EndsAsSent()
    {
        // Bob 昨晚已经送达；Alice 被延后，到点时她已经退订。
        var messageId = await SeedAsync(NotificationStatus.Scheduled, maxRetryCount: 3,
            new Recipient { Address = "bob@example.com", Status = NotificationStatus.Sent, SentTime = DateTime.UtcNow.AddHours(-9) },
            new Recipient { Address = "alice@example.com", Status = NotificationStatus.Scheduled, DeferredUntil = DateTime.UtcNow.AddMinutes(-5) });
        (await ServiceProvider.GetRequiredService<INotificationOptOutService>()
            .OptOutAsync("alice@example.com", NotificationType.Email)).Succeeded.ShouldBeTrue();
        DbContext.ChangeTracker.Clear();

        var result = await ServiceProvider.GetRequiredService<INotificationService>().SendAsync(messageId);

        result.Succeeded.ShouldBeTrue(result.Message);
        var message = await ReloadAsync(messageId);
        message.Status.ShouldBe(NotificationStatus.Sent, "Bob 收到了：这条消息不是「从来没人收到」");
        message.RetryCount.ShouldBe(0, "延后到点的那一遍不是重试");
        var alice = await DbContext.Set<Recipient>().AsNoTracking().SingleAsync(r => r.MessageId == messageId && r.Address == "alice@example.com");
        alice.Status.ShouldBe(NotificationStatus.Cancelled);
    }

    [Fact]
    public async Task ARoundWhereEveryoneIsBlocked_AndNobodyEverReceivedIt_StillEndsAsCancelled()
    {
        var messageId = await SeedAsync(NotificationStatus.Pending, maxRetryCount: 3,
            new Recipient { Address = "alice@example.com", Status = NotificationStatus.Pending });
        (await ServiceProvider.GetRequiredService<INotificationOptOutService>()
            .OptOutAsync("alice@example.com", NotificationType.Email)).Succeeded.ShouldBeTrue();
        DbContext.ChangeTracker.Clear();

        await ServiceProvider.GetRequiredService<INotificationService>().SendAsync(messageId);

        (await ReloadAsync(messageId)).Status.ShouldBe(NotificationStatus.Cancelled);
    }

    private async Task<Guid> SeedAsync(NotificationStatus status, int maxRetryCount, params Recipient[] recipients)
    {
        var message = new Message
        {
            Id = Guid.NewGuid(),
            Subject = "Quarterly newsletter",
            Content = "Body",
            Type = NotificationType.Email,
            Category = "General",
            Status = status,
            RetryCount = 0,
            MaxRetryCount = maxRetryCount,
            TotalRecipientCount = recipients.Length,
            SuccessCount = recipients.Count(r => r.Status == NotificationStatus.Sent),
            FailureCount = recipients.Count(r => r.Status == NotificationStatus.Failed),
            Recipients = [.. recipients],
        };

        await DbContext.Messages.AddAsync(message);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return message.Id;
    }

    private async Task<Message> ReloadAsync(Guid messageId)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Set<Message>().AsNoTracking().FirstAsync(m => m.Id == messageId);
    }
}
