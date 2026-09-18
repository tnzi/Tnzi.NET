using Tnzi.Domain.Entities;
using Tnzi.EventBus;
using Tnzi.Notification.Events;

namespace Tnzi.Notification.Tests.Integration;

/// <summary>
/// <see cref="FaxConfirmationService"/>：网关回执落到收件人身上时的取舍（真库）。
/// </summary>
/// <remarks>
/// ★ 这里守的核心是 <b>只降级不升级</b>：只有"没送到"会改动数据。判读器认错的最坏后果因此
/// 被钉死在"多一条误报的失败"上 —— 有人会去重发，对方收到两份，讨厌但看得见。
/// 反过来（把没拨通的传真盖上"已送达"的章）没有任何症状，几周后才由对方说出来。
/// </remarks>
public class FaxConfirmationTests : IntegrationTestBase
{
    private static readonly DateTime SentAt = new(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset ConfirmedAt = new(2026, 8, 20, 9, 5, 0, TimeSpan.Zero);

    protected override void ConfigureServices(IServiceCollection services)
    {
        AddRepo<Message>(services);
        AddRepo<Recipient>(services);

        var entityManagerMock = new Mock<IEntityManager>();
        entityManagerMock.Setup(m => m.GetAllDbContextTypes()).Returns(new[] { typeof(NotificationTestDbContext) });
        entityManagerMock.Setup(m => m.Initialize());
        services.AddSingleton(_ => entityManagerMock.Object);
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();
        services.AddScoped<IUnitOfWork, EFCoreUnitOfWork<NotificationTestDbContext>>();

        var options = new Mock<IOptionsMonitor<NotificationOptions>>();
        options.Setup(o => o.CurrentValue).Returns(new NotificationOptions
        {
            FaxSender = new FaxSenderOptions
            {
                GatewayDomain = "fax.example.com",
                Confirmation = new FaxConfirmationOptions { LookbackHours = 72 }
            }
        });
        services.AddSingleton(_ => options.Object);

        services.AddSingleton(_ => EventBus.Object);
        services.AddScoped<IFaxConfirmationService, FaxConfirmationService>();
    }

    /// <summary>截下发出去的事件。</summary>
    protected Mock<IEventBus> EventBus { get; } = new();

    private static void AddRepo<TEntity>(IServiceCollection services) where TEntity : class, IEntity<Guid>
    {
        services.AddScoped<IRepository<TEntity, Guid>>(sp =>
            new EFCoreRepository<NotificationTestDbContext, TEntity, Guid>(
                sp.GetRequiredService<NotificationTestDbContext>(), serviceProvider: sp));
        services.AddScoped<IReadOnlyRepository<TEntity, Guid>>(sp =>
            sp.GetRequiredService<IRepository<TEntity, Guid>>());
    }

    private async Task<Guid> SeedSentFaxAsync(
        string address = "+1 (905) 555-1234",
        string? carrierMessageId = "carrier-1@mail.example.com",
        NotificationType type = NotificationType.Fax,
        DateTime? sentAt = null)
    {
        var message = new Message
        {
            Id = Guid.NewGuid(),
            Subject = "File 2026-118",
            Content = "Archive only",
            Type = type,
            Status = NotificationStatus.Sent,
            SentTime = sentAt ?? SentAt,
            TotalRecipientCount = 1,
            SuccessCount = 1,
            FailureCount = 0,
            Recipients =
            [
                new Recipient
                {
                    Address = address,
                    Status = NotificationStatus.Sent,
                    SentTime = sentAt ?? SentAt,
                    ExternalMessageId = carrierMessageId
                }
            ]
        };

        await DbContext.Messages.AddAsync(message);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return message.Id;
    }

    private Task<Result<bool>> ApplyAsync(FaxConfirmation confirmation)
        => ServiceProvider.GetRequiredService<IFaxConfirmationService>().ApplyAsync(confirmation);

    private static FaxConfirmation Failure(
        string? carrierMessageId = "carrier-1@mail.example.com",
        string? faxNumber = null,
        DateTimeOffset? receivedAt = null)
        => new(FaxDeliveryOutcome.Failed, carrierMessageId, faxNumber, "no answer", receivedAt ?? ConfirmedAt);

    private async Task<Recipient> ReadRecipientAsync(Guid messageId)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Recipients.AsNoTracking().SingleAsync(r => r.MessageId == messageId);
    }

    /// <summary>★ 核心：网关说没送到，那条收件人就不再是"已发送"。</summary>
    [Fact]
    public async Task AFailureConfirmation_TurnsTheRecipientIntoAFailure()
    {
        var messageId = await SeedSentFaxAsync();

        var result = await ApplyAsync(Failure());

        result.Data.ShouldBeTrue(result.Message);
        var recipient = await ReadRecipientAsync(messageId);
        recipient.Status.ShouldBe(NotificationStatus.Failed);
        recipient.FailureReason!.ShouldContain("no answer");
    }

    /// <summary>
    /// ★ 不重算的话，投递报告会自相矛盾：收件人是 Failed，消息还写着「1/1 成功」。
    /// </summary>
    [Fact]
    public async Task AFailureConfirmation_RecountsTheMessage()
    {
        var messageId = await SeedSentFaxAsync();

        await ApplyAsync(Failure());

        DbContext.ChangeTracker.Clear();
        var message = await DbContext.Messages.AsNoTracking().SingleAsync(m => m.Id == messageId);
        message.SuccessCount.ShouldBe(0);
        message.FailureCount.ShouldBe(1);
        message.Status.ShouldBe(NotificationStatus.Failed);
    }

    /// <summary>
    /// ★★ 回执推翻的是"送到了"，不是"发出过"。<c>SentTime</c> 记的是它确实被交给网关的那一刻。
    /// </summary>
    [Fact]
    public async Task AFailureConfirmation_LeavesTheSentTimeAlone()
    {
        var messageId = await SeedSentFaxAsync();

        await ApplyAsync(Failure());

        DbContext.ChangeTracker.Clear();
        var message = await DbContext.Messages.AsNoTracking().SingleAsync(m => m.Id == messageId);
        message.SentTime.ShouldBe(SentAt);
    }

    /// <summary>★★ 只降级不升级：报"已送达"或读不懂的回执都什么也不做。</summary>
    [Theory]
    [InlineData(FaxDeliveryOutcome.Delivered)]
    [InlineData(FaxDeliveryOutcome.Unknown)]
    public async Task ANonFailureConfirmation_ChangesNothing(FaxDeliveryOutcome outcome)
    {
        var messageId = await SeedSentFaxAsync();

        var result = await ApplyAsync(
            new FaxConfirmation(outcome, "carrier-1@mail.example.com", null, null, ConfirmedAt));

        result.Data.ShouldBeFalse();
        (await ReadRecipientAsync(messageId)).Status.ShouldBe(NotificationStatus.Sent);
    }

    /// <summary>对不上号不是错误：回执收件箱里本来就有别人的信。</summary>
    [Fact]
    public async Task AConfirmationThatMatchesNothing_IsNotAnError()
    {
        var messageId = await SeedSentFaxAsync();

        var result = await ApplyAsync(Failure(carrierMessageId: "someone-else@mail.example.com"));

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data.ShouldBeFalse();
        (await ReadRecipientAsync(messageId)).Status.ShouldBe(NotificationStatus.Sent);
    }

    /// <summary>幂等：重启、标已读失败、人工补录撞上轮询，同一封回执都可能来第二次。</summary>
    [Fact]
    public async Task ApplyingTheSameConfirmationTwice_HasEffectOnlyOnce()
    {
        await SeedSentFaxAsync();

        (await ApplyAsync(Failure())).Data.ShouldBeTrue();
        (await ApplyAsync(Failure())).Data.ShouldBeFalse();
    }

    /// <summary>
    /// ★ 按号码对号：库里存的是人写的号码，回执里读出来的是归一化后的数字串。
    /// 直接做字符串等值比较永远对不上。
    /// </summary>
    [Fact]
    public async Task WithoutACarrierMessageId_ItMatchesOnTheNormalisedNumber()
    {
        var messageId = await SeedSentFaxAsync(address: "+1 (905) 555-1234", carrierMessageId: null);

        var result = await ApplyAsync(Failure(carrierMessageId: null, faxNumber: "9055551234"));

        result.Data.ShouldBeTrue(result.Message);
        (await ReadRecipientAsync(messageId)).Status.ShouldBe(NotificationStatus.Failed);
    }

    /// <summary>
    /// ★★ 号码是**不精确**的对号方式，所以只在窗口内认。同一个号码这个月可能发过好几份，
    /// 少了这道限制，今天的回执会去改掉上个月那一份。
    /// </summary>
    [Fact]
    public async Task ANumberMatchOutsideTheLookbackWindow_IsIgnored()
    {
        var messageId = await SeedSentFaxAsync(
            carrierMessageId: null, sentAt: SentAt.AddDays(-30));

        var result = await ApplyAsync(Failure(carrierMessageId: null, faxNumber: "9055551234"));

        result.Data.ShouldBeFalse();
        (await ReadRecipientAsync(messageId)).Status.ShouldBe(NotificationStatus.Sent);
    }

    /// <summary>
    /// ★ 回执把结论推翻了，就得补一条失败事件。
    /// </summary>
    /// <remarks>
    /// 发送那一刻这份传真是成功的，下游收到的是 <c>NotificationSentEvent</c>。回执几分钟后说没送到 ——
    /// 不发事件的话下游永远停在"成功"那一版：告警不响、统计对不上、业务侧也不会知道
    /// 该改用别的方式联系对方。这类缺口不会让任何测试变红，只会让下游一直用着过时的结论。
    /// </remarks>
    [Fact]
    public async Task AFailureConfirmation_PublishesAFailureEvent()
    {
        await SeedSentFaxAsync();

        await ApplyAsync(Failure());

        EventBus.Verify(
            b => b.PublishAsync(
                It.Is<NotificationFailedEvent>(e => e.Type == NotificationType.Fax),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>什么都没改动时不该发事件 —— 一封读不懂的回执不是一次失败。</summary>
    [Fact]
    public async Task AConfirmationThatChangesNothing_PublishesNoEvent()
    {
        await SeedSentFaxAsync();

        await ApplyAsync(
            new FaxConfirmation(FaxDeliveryOutcome.Delivered, "carrier-1@mail.example.com", null, null, ConfirmedAt));

        EventBus.Verify(
            b => b.PublishAsync(It.IsAny<NotificationFailedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// ★ 回执没有可信的时间戳时不能炸。
    /// </summary>
    /// <remarks>
    /// 一封缺 <c>Date</c> 信头的邮件，MimeKit 给出的是 <c>DateTimeOffset.MinValue</c>；
    /// webhook 来源也可能干脆不填。拿它去减 72 小时是 <c>ArgumentOutOfRangeException</c> ——
    /// 而它会把**整轮轮询**掀掉，那一批已经被标成已读的回执就此永久丢失。
    /// </remarks>
    [Fact]
    public async Task AConfirmationWithNoTimestamp_DoesNotThrow()
    {
        await SeedSentFaxAsync(carrierMessageId: null);

        var result = await ApplyAsync(
            new FaxConfirmation(FaxDeliveryOutcome.Failed, null, "9055551234", "no answer", default));

        result.Succeeded.ShouldBeTrue(result.Message);
    }

    /// <summary>
    /// ★ 因退订而取消的收件人不能被回执改成"失败"。
    /// </summary>
    /// <remarks>
    /// <c>Cancelled</c> 与 <c>Failed</c> 在本模块是刻意分开的两件事：前者是"我们没发"，
    /// 后者会被 <c>ResendToFailedRecipientsAsync</c> 捞回来重发 —— 把退订过的地址改成 Failed
    /// 等于给退订开一条后门。所以降级只从 <c>Sent</c> 出发。
    /// </remarks>
    [Fact]
    public async Task AConfirmation_NeverResurrectsACancelledRecipient()
    {
        var messageId = await SeedSentFaxAsync();

        DbContext.ChangeTracker.Clear();
        var seeded = await DbContext.Recipients.SingleAsync(r => r.MessageId == messageId);
        seeded.Status = NotificationStatus.Cancelled;
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var result = await ApplyAsync(Failure());

        result.Data.ShouldBeFalse();
        (await ReadRecipientAsync(messageId)).Status.ShouldBe(NotificationStatus.Cancelled);
    }

    /// <summary>
    /// ★ 邮件也用 <c>ExternalMessageId</c>。少了 Type 这一道，一封传真回执可以去改掉一封同 ID 的邮件。
    /// </summary>
    [Fact]
    public async Task AFaxConfirmation_NeverTouchesANonFaxMessage()
    {
        var messageId = await SeedSentFaxAsync(type: NotificationType.Email);

        var result = await ApplyAsync(Failure());

        result.Data.ShouldBeFalse();
        (await ReadRecipientAsync(messageId)).Status.ShouldBe(NotificationStatus.Sent);
    }
}
