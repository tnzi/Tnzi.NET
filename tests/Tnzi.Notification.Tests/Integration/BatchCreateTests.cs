using Tnzi.Domain.Entities;
using Tnzi.EventBus;
using Tnzi.Notification.Events;

namespace Tnzi.Notification.Tests.Integration;

/// <summary>
/// 批量创建（<c>CreateManyAndSendAsync</c>）与单条创建对同一份请求做同一件事：在真库上验证。
/// </summary>
/// <remarks>
/// ★★ <b>补的是哪个洞。</b>批量那条路径自己拼 <c>Message</c>：不抄 <c>ScheduledTime</c>、状态恒为 <c>Pending</c>、
/// 也不发 <c>NotificationCreatedEvent</c>。于是批量排给明早九点的提醒当场就发出去了，
/// 订阅了「通知已创建」的消费方一条也收不到。DTO 上写着「设了就等到那个 UTC 时刻」，两条入口却不是一回事。
/// </remarks>
public class BatchCreateTests : IntegrationTestBase
{
    private readonly Mock<IEmailSender> _emailSender = new();
    private readonly Mock<IEventBus> _eventBus = new();
    private readonly RecordingQueue _queue = new();

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
        services.AddSingleton(_ => _eventBus.Object);
        services.AddSingleton<INotificationQueueService>(_queue);

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

    // ── 定时 ─────────────────────────────────────────────────────────────────

    /// <summary>★★★ 批量里带了将来的 ScheduledTime：落库为定时消息，交给延迟入队，现在一封不发。</summary>
    [Fact]
    public async Task ABatchItemWithAFutureScheduledTime_IsHeldUntilThen()
    {
        var at = DateTime.UtcNow.AddHours(12);

        var result = await CreateManyAsync(NewRequest("reminder@example.com", scheduledTime: at));

        result.Succeeded.ShouldBeTrue(result.Message);
        var message = await ReloadSingleAsync();
        message.Status.ShouldBe(NotificationStatus.Scheduled);
        message.ScheduledTime!.Value.ShouldBe(at, TimeSpan.FromSeconds(1));
        _queue.Delays.ShouldHaveSingleItem().ShouldBeGreaterThan(TimeSpan.FromHours(11));
        VerifySent(Times.Never());
    }

    /// <summary>★ 定时优先于「立即发送」：单条创建就是这个顺序，批量不能反过来。</summary>
    [Fact]
    public async Task ABatchItemWithAFutureScheduledTime_IsHeldEvenWhenAskedToSendImmediately()
    {
        await CreateManyAsync(NewRequest("reminder@example.com", scheduledTime: DateTime.UtcNow.AddHours(12), sendImmediately: true));

        VerifySent(Times.Never());
        (await ReloadSingleAsync()).Status.ShouldBe(NotificationStatus.Scheduled);
    }

    /// <summary>对照：没设 ScheduledTime 的照旧立即入队（不带延迟）。</summary>
    [Fact]
    public async Task ABatchItemWithoutASchedule_IsQueuedRightAway()
    {
        await CreateManyAsync(NewRequest("reader@example.com"));

        _queue.Delays.ShouldHaveSingleItem().ShouldBe(TimeSpan.Zero);
        (await ReloadSingleAsync()).Status.ShouldBe(NotificationStatus.Pending);
    }

    // ── 事件 ─────────────────────────────────────────────────────────────────

    /// <summary>★★ 每条批量创建的消息都发「通知已创建」事件，与单条创建一样。</summary>
    [Fact]
    public async Task EveryBatchItem_PublishesNotificationCreatedEvent()
    {
        await CreateManyAsync(NewRequest("a@example.com"), NewRequest("b@example.com"));

        _eventBus.Verify(b => b.PublishAsync(It.IsAny<NotificationCreatedEvent>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    // ── 夹具 ─────────────────────────────────────────────────────────────────

    private static CreateNotificationRequest NewRequest(string address, DateTime? scheduledTime = null, bool sendImmediately = false) => new()
    {
        Type = NotificationType.Email,
        Subject = "Appointment reminder",
        Content = "Body",
        IsTransactional = true,
        ScheduledTime = scheduledTime,
        SendImmediately = sendImmediately,
        Recipients = [new RecipientInput { Address = address }],
    };

    private Task<Result<IEnumerable<NotificationInfo>>> CreateManyAsync(params CreateNotificationRequest[] requests)
        => ServiceProvider.GetRequiredService<INotificationService>().CreateManyAndSendAsync(requests);

    private void VerifySent(Times times) => _emailSender.Verify(s => s.SendToAsync(
        It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(),
        It.IsAny<bool>(), It.IsAny<List<EmailAttachment>?>(), It.IsAny<CancellationToken>()), times);

    private async Task<Message> ReloadSingleAsync()
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Set<Message>().AsNoTracking().SingleAsync();
    }

    /// <summary>只记录延迟、不执行工作项的队列替身。</summary>
    private sealed class RecordingQueue : INotificationQueueService
    {
        public List<TimeSpan> Delays { get; } = [];

        public Task EnqueueAsync(NotificationWorkItem workItem)
        {
            Delays.Add(TimeSpan.Zero);
            return Task.CompletedTask;
        }

        public Task EnqueueWithDelayAsync(NotificationWorkItem workItem, TimeSpan delay)
        {
            Delays.Add(delay);
            return Task.CompletedTask;
        }
    }
}
