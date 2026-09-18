using Tnzi.Domain.Entities;

namespace Tnzi.Notification.Tests.Integration;

/// <summary>
/// 重试与续发这条路径此前<b>一条测试都没有</b>。三件事在这里立住：
/// 成功要抹掉上一次的失败说明、重试要真的落库、批量重试要有闸门。
/// </summary>
public class RetryPathTests : IntegrationTestBase
{
    private const string StaleReason = "SMTP 550 - mailbox temporarily unavailable";

    private readonly RecordingQueue _queue = new();
    private readonly DispatchOptions _dispatch = new() { RecoveryBatchSize = 2 };

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

        var emailSender = new Mock<IEmailSender>();
        emailSender
            .Setup(s => s.SendToAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<List<EmailAttachment>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SendResult.CreateSuccess("stub-id"));
        services.AddSingleton(_ => emailSender.Object);
        services.AddSingleton(_ => new Mock<ISmsSender>().Object);
        services.AddSingleton(_ => new Mock<IPushSender>().Object);
        services.AddSingleton(_ => new Mock<IFaxSender>().Object);

        // ★ 真队列的替身：只记数，**不执行**工作项。没有它，RetryAsync 会当场把消息发出去，
        // 于是「重试有没有把收件人写回库里」这个问题被发送路径的那次保存盖掉，问不出来。
        services.AddSingleton<INotificationQueueService>(_ => _queue);

        services.AddScoped<INotificationOptOutService, NotificationOptOutService>();
        services.AddScoped<INotificationPreferenceService, NotificationPreferenceService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<INotificationProviderResolver, NotificationProviderResolver>();
        services.AddScoped<INotificationProviderSelector, DefaultNotificationProviderSelector>();
        services.AddScoped<INotificationRetryService, NotificationRetryService>();
    }

    private static void AddRepo<TEntity>(IServiceCollection services) where TEntity : class, IEntity<Guid>
    {
        services.AddScoped<IRepository<TEntity, Guid>>(sp =>
            new EFCoreRepository<NotificationTestDbContext, TEntity, Guid>(
                sp.GetRequiredService<NotificationTestDbContext>(), serviceProvider: sp));
        services.AddScoped<IReadOnlyRepository<TEntity, Guid>>(sp =>
            sp.GetRequiredService<IRepository<TEntity, Guid>>());
    }

    // ── 成功要抹掉上一次的失败说明 ───────────────────────────────────────────

    /// <summary>
    /// ★★ 续发扫描直接调 <c>SendAsync</c>，它挑的收件人里包含 <c>Failed</c> 的那些 ——
    /// 而那些行带着上一次的失败说明。这一次成功了，说明必须消失。
    /// </summary>
    /// <remarks>
    /// 留着的话，投递报告里会出现一行 <c>Status = Sent</c> 却写着「SMTP 550 拒收」的记录，
    /// 读的人无从判断这封到底送到没有。本模块反复在消灭的正是这种
    /// 「记下来的结果与实际发生的事不符」。
    /// </remarks>
    [Fact]
    public async Task ARecipientThatSucceedsOnResume_NoLongerCarriesTheOldFailureReason()
    {
        var messageId = await SeedAsync(
            NotificationStatus.Sending, recipientStatus: NotificationStatus.Failed, failureReason: StaleReason);

        var result = await ServiceProvider.GetRequiredService<INotificationService>().SendAsync(messageId);
        result.Succeeded.ShouldBeTrue(result.Message);

        var recipient = await ReloadRecipientAsync(messageId);
        recipient.Status.ShouldBe(NotificationStatus.Sent);
        recipient.FailureReason.ShouldBeNull();
    }

    // ── 重试要真的落库 ───────────────────────────────────────────────────────

    /// <summary>
    /// ★★ <c>RetryAsync</c> 改的是<b>子实体</b>（每个收件人的状态与失败说明），
    /// 而 <c>UpdateAsync</c> 只把根实体置为 Modified。不带跟踪加载的话整个图是
    /// Unchanged，那些改动一个都不落库 —— 而接口照样返回成功。
    /// </summary>
    [Fact]
    public async Task Retry_PersistsTheRecipientResetToTheDatabase()
    {
        var messageId = await SeedAsync(
            NotificationStatus.Failed, recipientStatus: NotificationStatus.Failed, failureReason: StaleReason);

        var result = await ServiceProvider.GetRequiredService<INotificationRetryService>().RetryAsync(messageId);
        result.Succeeded.ShouldBeTrue(result.Message);

        var recipient = await ReloadRecipientAsync(messageId);
        recipient.Status.ShouldBe(NotificationStatus.Pending);
        recipient.FailureReason.ShouldBeNull();
    }

    /// <summary>对照：消息本身也回到 Pending（这一半原本就工作，用来分辨上一条红在哪）。</summary>
    [Fact]
    public async Task Retry_PersistsTheMessageResetToo()
    {
        var messageId = await SeedAsync(
            NotificationStatus.Failed, recipientStatus: NotificationStatus.Failed, failureReason: StaleReason);

        await ServiceProvider.GetRequiredService<INotificationRetryService>().RetryAsync(messageId);

        var message = await ReloadMessageAsync(messageId);
        message.Status.ShouldBe(NotificationStatus.Pending);
        message.FailureReason.ShouldBeNull();
    }

    // ── 批量重试要有闸门 ─────────────────────────────────────────────────────

    /// <summary>
    /// ★★ 「重试全部失败项」此前没有任何上限：一次点击就能把整张历史失败表灌进发送管线。
    /// 恢复扫描早就有这道闸门，这条一直没有。
    /// </summary>
    [Fact]
    public async Task RetryFailed_TakesAtMostOneBatch()
    {
        for (var i = 0; i < 5; i++)
            await SeedAsync(NotificationStatus.Failed, recipientStatus: NotificationStatus.Failed);

        await ServiceProvider.GetRequiredService<INotificationRetryService>().RetryFailedAsync();

        _queue.Count.ShouldBe(_dispatch.RecoveryBatchSize);
    }

    /// <summary>
    /// ★ 截断必须说出来。不说的话，「处理完了」与「处理了一批还剩 3 条」在界面上
    /// 长得一模一样，而只有再点一次才能推进。
    /// </summary>
    [Fact]
    public async Task RetryFailed_SaysHowManyItLeftBehind()
    {
        for (var i = 0; i < 5; i++)
            await SeedAsync(NotificationStatus.Failed, recipientStatus: NotificationStatus.Failed);

        var result = await ServiceProvider.GetRequiredService<INotificationRetryService>().RetryFailedAsync();

        result.Message!.ShouldContain("3 more");
    }

    /// <summary>对照：一批装得下时不该冒出「还剩几条」这句话。</summary>
    [Fact]
    public async Task RetryFailed_SaysNothingAboutLeftoversWhenThereAreNone()
    {
        await SeedAsync(NotificationStatus.Failed, recipientStatus: NotificationStatus.Failed);

        var result = await ServiceProvider.GetRequiredService<INotificationRetryService>().RetryFailedAsync();

        result.Message!.ShouldNotContain("more failed notification");
        _queue.Count.ShouldBe(1);
    }

    // ── 夹具 ─────────────────────────────────────────────────────────────────

    /// <summary>只记数、不执行工作项的队列替身。</summary>
    private sealed class RecordingQueue : INotificationQueueService
    {
        public int Count { get; private set; }

        public Task EnqueueAsync(NotificationWorkItem workItem)
        {
            Count++;
            return Task.CompletedTask;
        }

        // 默认接口方法会转调 EnqueueAsync，但显式写出来，免得读的人以为延迟入队走了别的路。
        public Task EnqueueWithDelayAsync(NotificationWorkItem workItem, TimeSpan delay)
            => EnqueueAsync(workItem);
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

    private async Task<Guid> SeedAsync(
        NotificationStatus status,
        NotificationStatus recipientStatus,
        string? failureReason = null)
    {
        var message = new Message
        {
            Id = Guid.NewGuid(),
            Subject = "Invoice",
            Content = "Body",
            Type = NotificationType.Email,
            Category = "General",
            IsTransactional = true,
            Status = status,
            FailureReason = failureReason,
            RetryCount = 1,
            MaxRetryCount = 3,
            TotalRecipientCount = 1,
            Recipients =
            [
                new Recipient
                {
                    Address = $"reader{Guid.NewGuid():N}@example.com",
                    Status = recipientStatus,
                    FailureReason = failureReason,
                }
            ],
        };

        await DbContext.Messages.AddAsync(message);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return message.Id;
    }
}
