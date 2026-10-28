using Tnzi.Domain.Entities;

namespace Tnzi.Notification.Tests.Integration;

/// <summary>
/// 排队发送与延迟重试的工作项随进程一起丢掉之后，消息由派发恢复接着发出去：在真库上验证。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ <b>补的是哪个洞。</b><c>CreateAndSendAsync</c> 默认（<c>SendImmediately = false</c>）只把工作项写进
/// <b>进程内</b>的有界队列，行停在 <c>Pending</c>；<c>RetryAsync</c> 把消息改回 <c>Pending</c> 后用进程内的
/// <c>Task.Delay</c> 等退避。一次部署 / 回收发生在队列排空或退避到期之前，这些行就永远停在 <c>Pending</c>：
/// 恢复扫描只看 <c>Sending</c> / <c>Scheduled</c>，「重试失败项」只挑 <c>Failed</c>，管理端只对失败的行给「重发」。
/// 关机日志还说「消息没丢，恢复扫描会发」：对它们不成立。
/// </para>
/// <para>
/// ★ <b>草稿不能被捎带发出去。</b><c>Pending</c> 也是 <c>CreateAsync</c>（只创建不发送）的状态。判据是
/// <c>ScheduledTime</c>：交给发送的那一刻写下「最早几点发」（排队 = 当时，重试 = 退避到期的时刻），
/// 草稿不写，恢复永远不碰它。不加列，不需要迁移。
/// </para>
/// </remarks>
public class QueuedRecoveryTests : IntegrationTestBase
{
    private readonly Mock<IEmailSender> _emailSender = new();
    private readonly DiscardingQueue _queue = new();
    private readonly DispatchOptions _dispatch = new()
    {
        EnableRecovery = true,
        StuckAfterMinutes = 15,
        RecoveryBatchSize = 50,
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

        // ★ 进程重启的替身：工作项交进来就丢，一个都不执行。
        services.AddSingleton<INotificationQueueService>(_queue);

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

    // ── 排队发送 ─────────────────────────────────────────────────────────────

    /// <summary>★★★ 排队的工作项丢了（重启），超过宽限之后恢复扫描把它发出去。</summary>
    [Fact]
    public async Task AQueuedMessageWhoseWorkItemWasLost_IsSentOnceOverdue()
    {
        var messageId = await CreateQueuedAsync();
        _queue.Discarded.ShouldBe(1);
        await AgeDueTimeAsync(messageId, minutes: 60);

        await RecoverOnceAsync();

        VerifySent(Times.Once());
        (await ReloadAsync(messageId)).Status.ShouldBe(NotificationStatus.Sent);
    }

    /// <summary>★ 刚入队的还在队列里排着：宽限期内留给队列，不与它抢。</summary>
    [Fact]
    public async Task AQueuedMessageStillWithinTheGracePeriod_IsLeftToTheQueue()
    {
        var messageId = await CreateQueuedAsync();

        await RecoverOnceAsync();

        VerifySent(Times.Never());
        (await ReloadAsync(messageId)).Status.ShouldBe(NotificationStatus.Pending);
    }

    /// <summary>★★ 只创建、没要求发送的草稿永远不会被恢复扫描捎带发出去。</summary>
    [Fact]
    public async Task ADraft_IsNeverSentByTheRecoveryScan()
    {
        var created = await ServiceProvider.GetRequiredService<INotificationService>().CreateAsync(NewRequest());
        created.Succeeded.ShouldBeTrue(created.Message);
        var messageId = created.Data!.Id;
        await DbContext.Set<Message>().Where(m => m.Id == messageId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.LastModificationTime, DateTime.UtcNow.AddDays(-1)));
        DbContext.ChangeTracker.Clear();

        await RecoverOnceAsync();

        VerifySent(Times.Never());
        var draft = await ReloadAsync(messageId);
        draft.Status.ShouldBe(NotificationStatus.Pending);
        draft.ScheduledTime.ShouldBeNull();
    }

    /// <summary>★ 两轮扫描各自选出同一条时只有一轮认领得到（条件写在 UPDATE 里）。</summary>
    [Fact]
    public async Task AnOverdueQueuedMessage_IsClaimedOnlyOnce()
    {
        var messageId = await CreateQueuedAsync();
        await AgeDueTimeAsync(messageId, minutes: 60);
        var repository = ServiceProvider.GetRequiredService<IRepository<Message, Guid>>();

        var first = await NotificationDispatchBackgroundService.TryClaimQueuedAsync(repository, messageId, CancellationToken.None);
        var second = await NotificationDispatchBackgroundService.TryClaimQueuedAsync(repository, messageId, CancellationToken.None);

        first.ShouldBeTrue();
        second.ShouldBeFalse();
    }

    // ── 延迟重试 ─────────────────────────────────────────────────────────────

    /// <summary>★★ 重试把「什么时候再发」写进行里：退避到期的那一刻。</summary>
    [Fact]
    public async Task Retry_PersistsWhenTheRetryIsDue()
    {
        var messageId = await SeedFailedAsync();

        var result = await ServiceProvider.GetRequiredService<INotificationRetryService>().RetryAsync(messageId);
        result.Succeeded.ShouldBeTrue(result.Message);

        var message = await ReloadAsync(messageId);
        message.Status.ShouldBe(NotificationStatus.Pending);
        // 默认 60 秒 × 2^RetryCount(1) = 120 秒之后。
        message.ScheduledTime!.Value.ShouldBe(DateTime.UtcNow.AddSeconds(120), TimeSpan.FromSeconds(30));
    }

    /// <summary>★★★ 重试的延迟项丢了（重启），退避到期再过一个宽限之后，恢复扫描把它发出去。</summary>
    [Fact]
    public async Task ARetryWhoseDelayedItemWasLost_IsSentOnceOverdue()
    {
        var messageId = await SeedFailedAsync();
        await ServiceProvider.GetRequiredService<INotificationRetryService>().RetryAsync(messageId);

        await RecoverOnceAsync();
        VerifySent(Times.Never());

        await AgeDueTimeAsync(messageId, minutes: 60);
        await RecoverOnceAsync();

        VerifySent(Times.Once());
        (await ReloadAsync(messageId)).Status.ShouldBe(NotificationStatus.Sent);
    }

    // ── 夹具 ─────────────────────────────────────────────────────────────────

    private static CreateNotificationRequest NewRequest() => new()
    {
        Type = NotificationType.Email,
        Subject = "Monthly statement",
        Content = "Body",
        IsTransactional = true,
        Recipients = [new RecipientInput { Address = "reader@example.com" }],
    };

    private async Task<Guid> CreateQueuedAsync()
    {
        var result = await ServiceProvider.GetRequiredService<INotificationService>().CreateAndSendAsync(NewRequest());
        result.Succeeded.ShouldBeTrue(result.Message);
        DbContext.ChangeTracker.Clear();
        return result.Data!.Id;
    }

    /// <summary>
    /// 把行上已有的「最早几点发」往前挪：等价于让时间过去这么久。没有就不补：
    /// 那一列是不是被写下了，正是这里要验的东西。
    /// </summary>
    private async Task AgeDueTimeAsync(Guid messageId, int minutes)
    {
        var dueAt = (await ReloadAsync(messageId)).ScheduledTime;
        dueAt.ShouldNotBeNull("the message was handed to sending without recording when it is due");
        await DbContext.Set<Message>().Where(m => m.Id == messageId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ScheduledTime, dueAt.Value.AddMinutes(-minutes)));
        DbContext.ChangeTracker.Clear();
    }

    private async Task<Guid> SeedFailedAsync()
    {
        var message = new Message
        {
            Id = Guid.NewGuid(),
            Subject = "Invoice",
            Content = "Body",
            Type = NotificationType.Email,
            Category = "General",
            IsTransactional = true,
            Status = NotificationStatus.Failed,
            FailureReason = "SMTP 451",
            RetryCount = 1,
            MaxRetryCount = 3,
            TotalRecipientCount = 1,
            Recipients = [new Recipient { Address = "reader@example.com", Status = NotificationStatus.Failed, FailureReason = "SMTP 451" }],
        };

        await DbContext.Messages.AddAsync(message);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return message.Id;
    }

    private Task RecoverOnceAsync()
        => new NotificationDispatchBackgroundService(
                ServiceProvider,
                ServiceProvider.GetRequiredService<IOptionsMonitor<NotificationOptions>>(),
                new Mock<ILogger<NotificationDispatchBackgroundService>>().Object)
            .RecoverOnceAsync(_dispatch, CancellationToken.None);

    private void VerifySent(Times times) => _emailSender.Verify(s => s.SendToAsync(
        It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(),
        It.IsAny<bool>(), It.IsAny<List<EmailAttachment>?>(), It.IsAny<CancellationToken>()), times);

    private async Task<Message> ReloadAsync(Guid messageId)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Set<Message>().AsNoTracking().FirstAsync(m => m.Id == messageId);
    }

    /// <summary>工作项交进来就丢：进程在它执行之前退出了。</summary>
    private sealed class DiscardingQueue : INotificationQueueService
    {
        public int Discarded { get; private set; }

        public Task EnqueueAsync(NotificationWorkItem workItem)
        {
            Discarded++;
            return Task.CompletedTask;
        }

        public Task EnqueueWithDelayAsync(NotificationWorkItem workItem, TimeSpan delay) => EnqueueAsync(workItem);
    }
}
