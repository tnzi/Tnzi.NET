using Tnzi.Domain.Entities;
using Tnzi.Notification.Services.Internal;

namespace Tnzi.Notification.Tests.Integration;

/// <summary>
/// 取消与删除一条<b>正在发送</b>的消息：在真库上验证发送循环看得见、也不会盖掉它们。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ <b>补的是哪个洞。</b>管理端对 <c>Sending</c> 的消息给出「取消」，后端也答复成功，
/// 可发送循环在另一个作用域里拿着开跑时读出的名单，从不回头看库；每次分片落库又把整个消息行置为已修改，
/// 内存里的 <c>Status = Sending</c> 与 <c>IsDeleted = false</c> 于是盖掉了取消与删除。
/// 结果是「已取消」答复之后剩下的人照样收到，行先回到 <c>Sending</c> 再变成 <c>Sent</c>；删掉的消息又回到列表里。
/// </para>
/// <para>
/// 探针挂在发送器上：第一个收件人投递时，另一个作用域去取消或删除这条消息，就是管理员在群发进行中点下按钮的样子。
/// </para>
/// </remarks>
public class CancelWhileSendingTests : IntegrationTestBase
{
    private const int RecipientCount = 3;

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
        services.AddScoped<INotificationQueryService, NotificationQueryService>();
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

    // ── 取消 ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// ★★★ 群发进行中点「取消」：剩下的人一个都不发，消息停在 <c>Cancelled</c>，
    /// 正在投递的那一个照实记成 <c>Sent</c>。
    /// </summary>
    [Fact]
    public async Task CancellingMidFlight_StopsTheRemainingDeliveries_AndStaysCancelled()
    {
        var messageId = await SeedAsync(NotificationStatus.Pending);
        Result? cancel = null;
        ProbeOnFirstDelivery(async sp => cancel = await sp.GetRequiredService<INotificationService>().CancelAsync(messageId));

        var send = await SendAsync(messageId);

        cancel.ShouldNotBeNull("the probe never ran - the send did not reach a recipient");
        cancel!.Succeeded.ShouldBeTrue(cancel.Message);
        send.Succeeded.ShouldBeFalse("a send that was cancelled half way did not finish");
        send.Code.ShouldBe(409);
        VerifySent(Times.Once());

        var message = await ReloadAsync(messageId);
        message.Status.ShouldBe(NotificationStatus.Cancelled);
        message.SentTime.ShouldBeNull();
        var statuses = await RecipientStatusesAsync(messageId);
        statuses.Count(s => s == NotificationStatus.Sent).ShouldBe(1);
        statuses.Count(s => s == NotificationStatus.Cancelled).ShouldBe(RecipientCount - 1);
    }

    /// <summary>
    /// ★★ 同形的另一个入口：「重发失败项」进行中点「取消」，剩下的失败项不再重发，状态不被写回。
    /// </summary>
    [Fact]
    public async Task CancellingAResendMidFlight_StopsTheRemainingResends()
    {
        var messageId = await SeedAsync(NotificationStatus.Failed, NotificationStatus.Failed);
        Result? cancel = null;
        ProbeOnFirstDelivery(async sp => cancel = await sp.GetRequiredService<INotificationService>().CancelAsync(messageId));

        var resend = await ServiceProvider.GetRequiredService<INotificationService>().ResendToFailedRecipientsAsync(messageId);

        cancel.ShouldNotBeNull("the probe never ran - the resend did not reach a recipient");
        cancel!.Succeeded.ShouldBeTrue(cancel.Message);
        resend.Succeeded.ShouldBeFalse();
        VerifySent(Times.Once());
        (await ReloadAsync(messageId)).Status.ShouldBe(NotificationStatus.Cancelled);
    }

    /// <summary>
    /// ★ 取消是一次条件更新：读到之后、写下之前状态变了（这里是一次认领把它抢成了 Sending），就不写，答 409 而不是盖掉。
    /// </summary>
    [Fact]
    public async Task ACancelOnAStaleStatus_IsNotWritten()
    {
        var messageId = await SeedAsync(NotificationStatus.Pending);
        var repository = ServiceProvider.GetRequiredService<IRepository<Message, Guid>>();
        (await MessageClaim.TryClaimAsync(repository, messageId, NotificationStatus.Pending, CancellationToken.None)).ShouldBeTrue();

        (await MessageClaim.TryTransitionAsync(repository, messageId, NotificationStatus.Pending, NotificationStatus.Cancelled, CancellationToken.None))
            .ShouldBeFalse();
        (await ReloadAsync(messageId)).Status.ShouldBe(NotificationStatus.Sending);
    }

    // ── 删除 ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// ★★★ 群发进行中把消息删掉：它不会因为发送循环的分片落库而「复活」，剩下的人也不再发。
    /// </summary>
    [Fact]
    public async Task DeletingMidFlight_KeepsTheMessageDeleted_AndStopsTheRemainingDeliveries()
    {
        var messageId = await SeedAsync(NotificationStatus.Pending);
        Result<int>? delete = null;
        ProbeOnFirstDelivery(async sp => delete = await sp.GetRequiredService<INotificationQueryService>().BatchDeleteAsync([messageId]));

        var send = await SendAsync(messageId);

        delete.ShouldNotBeNull("the probe never ran - the send did not reach a recipient");
        delete!.Data.ShouldBe(1);
        send.Succeeded.ShouldBeFalse();
        VerifySent(Times.Once());

        DbContext.ChangeTracker.Clear();
        var message = await DbContext.Set<Message>().IgnoreQueryFilters().AsNoTracking().FirstAsync(m => m.Id == messageId);
        message.IsDeleted.ShouldBeTrue();
    }

    // ── 对照 ─────────────────────────────────────────────────────────────────

    /// <summary>对照：没人取消的群发照常发完（否则上面几条验的可能是一个恒假的条件）。</summary>
    [Fact]
    public async Task AnUninterruptedSend_DeliversToEveryone()
    {
        var messageId = await SeedAsync(NotificationStatus.Pending);

        var send = await SendAsync(messageId);

        send.Succeeded.ShouldBeTrue(send.Message);
        VerifySent(Times.Exactly(RecipientCount));
        (await ReloadAsync(messageId)).Status.ShouldBe(NotificationStatus.Sent);
    }

    // ── 夹具 ─────────────────────────────────────────────────────────────────

    /// <summary>第一个收件人投递时，在另一个作用域里做 <paramref name="action"/>。</summary>
    private void ProbeOnFirstDelivery(Func<IServiceProvider, Task> action)
    {
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
                    await action(other.ServiceProvider);
                }
                return SendResult.CreateSuccess("stub-id");
            });
    }

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

    private async Task<List<NotificationStatus>> RecipientStatusesAsync(Guid messageId)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Set<Recipient>().AsNoTracking()
            .Where(r => r.MessageId == messageId)
            .Select(r => r.Status)
            .ToListAsync();
    }

    private async Task<Guid> SeedAsync(NotificationStatus status, NotificationStatus recipientStatus = NotificationStatus.Pending)
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
                .Select(i => new Recipient { Address = $"reader{i}@example.com", Status = recipientStatus })],
        };

        await DbContext.Messages.AddAsync(message);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return message.Id;
    }
}
