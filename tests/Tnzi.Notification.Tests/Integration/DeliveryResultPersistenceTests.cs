using Tnzi.Domain.Entities;
using Tnzi.Notification.Metadata;
using Tnzi.Notification.Services.Internal;

namespace Tnzi.Notification.Tests.Integration;

/// <summary>
/// 投递结果落库前必须收敛到列宽 —— 在<b>真实发送路径</b>上验证，不是只验纯函数。
/// </summary>
/// <remarks>
/// <para>
/// ★★ <b>这条缺陷的后果不是「少了一行原因」，是重复投递。</b>一次群发的全部收件人状态
/// 由<b>一次</b> <c>SaveChangesAsync</c> 落库：任何一条 <c>FailureReason</c> 越界都让整次保存
/// 抛异常并回滚，于是<b>已经真的发出去</b>的那些人留在 <c>Pending</c>；
/// <c>NotificationDispatchBackgroundService</c> 随后重扫这条消息并再发一遍。
/// 它的注释「SendAsync 只发 Pending/Failed 的收件人，所以续发不会重复投递」正建立在
/// 这些状态已经落库的前提上，而失败的那次保存把前提丢掉了。
/// </para>
/// <para>
/// ★ <b>断言不能落在「插入有没有报错」上</b>：测试夹具跑 SQLite，而 SQLite 根本不强制
/// <c>VARCHAR</c> 长度 —— 去掉收敛这里照样绿。所以断言一律落在<b>存进去的字符串本身</b>
/// 与 <b>EF 模型声明的列宽</b>上，这两样与数据库提供程序无关。
/// </para>
/// </remarks>
public class DeliveryResultPersistenceTests : IntegrationTestBase
{
    private const string Failing = "broken@example.com";

    /// <summary>网关 502 回来的一整页 HTML —— 这就是真实世界里那个越界值。</summary>
    private static readonly string GatewayErrorPage =
        "Twilio API error: BadGateway - <html><head><title>502 Bad Gateway</title></head><body>"
        + new string('x', 6000) + "</body></html>";

    private readonly Mock<IEmailSender> _emailSender = new();
    private string? _externalMessageId = "stub-id";

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

        // 一个地址失败并带回超长错误体，其余成功 —— 群发里最常见的形态。
        _emailSender
            .Setup(s => s.SendToAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<List<EmailAttachment>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string address, string? _, string _, string _, bool _, List<EmailAttachment>? _, CancellationToken _)
                => address == Failing
                    ? SendResult.CreateFailure(GatewayErrorPage)
                    : SendResult.CreateSuccess(_externalMessageId));

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

    // ── 列宽与常量不许漂开 ───────────────────────────────────────────────────

    /// <summary>
    /// 代码收敛到的长度就是列声明的长度。两处各写一个数字，漂开的那天不会有任何东西变红 ——
    /// 除了这条。
    /// </summary>
    [Fact]
    public void TheColumnWidths_AreTheOnesTheCodeTruncatesTo()
    {
        MaxLengthOf<Recipient>(nameof(Recipient.FailureReason))
            .ShouldBe(NotificationFieldLimits.FailureReasonMaxLength);
        MaxLengthOf<Message>(nameof(Message.FailureReason))
            .ShouldBe(NotificationFieldLimits.FailureReasonMaxLength);
        MaxLengthOf<Recipient>(nameof(Recipient.ExternalMessageId))
            .ShouldBe(NotificationFieldLimits.ExternalMessageIdMaxLength);
    }

    // ── 越界的失败原因 ───────────────────────────────────────────────────────

    [Fact]
    public async Task AnOversizeGatewayError_IsBoundedBeforeItReachesTheRecipientRow()
    {
        var messageId = await SeedMessageAsync([Failing]);

        await SendAsync(messageId);

        var recipient = await ReloadRecipientAsync(messageId, Failing);
        recipient.Status.ShouldBe(NotificationStatus.Failed);
        recipient.FailureReason!.Length.ShouldBe(NotificationFieldLimits.FailureReasonMaxLength);
    }

    /// <summary>消息级的原因抄自同一个字符串，同样要收敛。</summary>
    [Fact]
    public async Task AnOversizeGatewayError_IsBoundedOnTheMessageRowToo()
    {
        var messageId = await SeedMessageAsync([Failing]);

        await SendAsync(messageId);

        var message = await ReloadMessageAsync(messageId);
        message.FailureReason!.Length.ShouldBe(NotificationFieldLimits.FailureReasonMaxLength);
    }

    /// <summary>
    /// ★★ 本轮的要害：同一批里已经送达的那些人，状态必须留在库里。
    /// 留不住就会被续发扫描当成没发过，再发一遍。
    /// </summary>
    [Fact]
    public async Task TheDeliveredRecipientsInTheSameBatch_KeepTheirSentState()
    {
        var messageId = await SeedMessageAsync([Failing, "a@example.com", "b@example.com"]);

        await SendAsync(messageId);

        (await ReloadRecipientAsync(messageId, "a@example.com")).Status.ShouldBe(NotificationStatus.Sent);
        (await ReloadRecipientAsync(messageId, "b@example.com")).Status.ShouldBe(NotificationStatus.Sent);
        (await ReloadMessageAsync(messageId)).Status.ShouldBe(NotificationStatus.PartiallySent);
    }

    /// <summary>
    /// 落库的每一个字符串都不得超过它那一列 —— 不点名字段，这样将来新加的字段也在网里。
    /// </summary>
    [Fact]
    public async Task NoPersistedString_ExceedsItsOwnColumn()
    {
        var messageId = await SeedMessageAsync([Failing, "a@example.com"]);

        await SendAsync(messageId);

        DbContext.ChangeTracker.Clear();
        var recipients = await DbContext.Set<Recipient>().AsNoTracking()
            .Where(r => r.MessageId == messageId).ToListAsync();
        var message = await ReloadMessageAsync(messageId);

        foreach (var recipient in recipients)
        {
            AssertWithinColumn<Recipient>(nameof(Recipient.FailureReason), recipient.FailureReason);
            AssertWithinColumn<Recipient>(nameof(Recipient.ExternalMessageId), recipient.ExternalMessageId);
        }

        AssertWithinColumn<Message>(nameof(Message.FailureReason), message.FailureReason);
    }

    /// <summary>重发路径抄的是同一段赋值，同样要收敛（否则那是一条绕过收敛的后门）。</summary>
    [Fact]
    public async Task TheResendPath_BoundsTheReasonToo()
    {
        var messageId = await SeedMessageAsync([Failing], recipientStatus: NotificationStatus.Failed);

        var resent = await ServiceProvider.GetRequiredService<INotificationService>()
            .ResendToFailedRecipientsAsync(messageId);
        resent.Succeeded.ShouldBeTrue(resent.Message);

        var recipient = await ReloadRecipientAsync(messageId, Failing);
        recipient.FailureReason!.Length.ShouldBe(NotificationFieldLimits.FailureReasonMaxLength);
    }

    // ── 越界的外部消息号 ─────────────────────────────────────────────────────

    /// <summary>
    /// ★ 超长的外部消息号丢掉，但这次投递<b>仍然是成功的</b> —— 消息确实发出去了，
    /// 只是回执再也对不上号。把它记成失败会让一封已送达的信被重发。
    /// </summary>
    [Fact]
    public async Task AnOversizeExternalMessageId_IsDroppedWhileTheSendStaysSent()
    {
        _externalMessageId = new string('m', NotificationFieldLimits.ExternalMessageIdMaxLength + 20);
        var messageId = await SeedMessageAsync(["a@example.com"]);

        await SendAsync(messageId);

        var recipient = await ReloadRecipientAsync(messageId, "a@example.com");
        recipient.Status.ShouldBe(NotificationStatus.Sent);
        recipient.ExternalMessageId.ShouldBeNull();
    }

    [Fact]
    public async Task AnOrdinaryExternalMessageId_IsStillRecorded()
    {
        var messageId = await SeedMessageAsync(["a@example.com"]);

        await SendAsync(messageId);

        (await ReloadRecipientAsync(messageId, "a@example.com")).ExternalMessageId.ShouldBe("stub-id");
    }

    // ── 夹具 ─────────────────────────────────────────────────────────────────

    private int MaxLengthOf<TEntity>(string property) where TEntity : class
        => DbContext.Model.FindEntityType(typeof(TEntity))!.FindProperty(property)!.GetMaxLength()
           ?? throw new InvalidOperationException($"{typeof(TEntity).Name}.{property} declares no max length.");

    private void AssertWithinColumn<TEntity>(string property, string? value) where TEntity : class
    {
        if (value == null) return;
        value.Length.ShouldBeLessThanOrEqualTo(MaxLengthOf<TEntity>(property),
            $"{typeof(TEntity).Name}.{property} was persisted past its declared column width.");
    }

    private async Task<Recipient> ReloadRecipientAsync(Guid messageId, string address)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Set<Recipient>().AsNoTracking()
            .FirstAsync(r => r.MessageId == messageId && r.Address == address);
    }

    private async Task<Message> ReloadMessageAsync(Guid messageId)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Set<Message>().AsNoTracking().FirstAsync(m => m.Id == messageId);
    }

    private async Task<Guid> SeedMessageAsync(
        string[] addresses, NotificationStatus recipientStatus = NotificationStatus.Pending)
    {
        var message = new Message
        {
            Id = Guid.NewGuid(),
            Subject = "Batch",
            Content = "Body",
            Type = NotificationType.Email,
            Category = "General",
            IsTransactional = true,
            Status = NotificationStatus.Pending,
            TotalRecipientCount = addresses.Length,
            Recipients = [.. addresses.Select(a => new Recipient { Address = a, Status = recipientStatus })]
        };

        await DbContext.Messages.AddAsync(message);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return message.Id;
    }

    private Task<Result> SendAsync(Guid messageId)
        => ServiceProvider.GetRequiredService<INotificationService>().SendAsync(messageId);
}
