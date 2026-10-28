using Tnzi.Domain.Entities;

namespace Tnzi.Notification.Tests.Integration;

/// <summary>
/// <c>Dispatch:RatePerMinute</c> 真的给每一次投递限速：在真库、真发送路径上验证。
/// </summary>
/// <remarks>
/// ★★★ <b>补的是哪个洞。</b>这一项是管理端设置中心里可改的「每分钟发送上限」，文档与注释都承诺它防的是
/// 「群发把整个发送账号打到被服务商封停，连密码重置一起停摆」。而它只在派发恢复里每条<b>消息</b>前等一下：
/// 正常的创建并发送、排队、管理端发送、重发失败项一次都不问它，恢复续发的一条五千人的消息也是等一次之后背靠背发完。
/// 现在限速落在<b>每个收件人</b>的投递上。
/// </remarks>
public class SendPacingTests : IntegrationTestBase
{
    /// <summary>每分钟 600 次 = 每 100 毫秒一次。</summary>
    private const int RatePerMinute = 600;

    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(60_000d / RatePerMinute);

    /// <summary>
    /// 计时容差：Windows 上计时器分辨率约 15 毫秒，<c>Task.Delay</c> 偶尔会比请求的早到一个刻度。
    /// </summary>
    private static readonly TimeSpan Tolerance = TimeSpan.FromMilliseconds(25);

    private readonly List<DateTime> _sentAt = [];
    private readonly DispatchOptions _dispatch = new() { RatePerMinute = RatePerMinute };

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
            .ReturnsAsync(() =>
            {
                _sentAt.Add(DateTime.UtcNow);
                return SendResult.CreateSuccess("stub-id");
            });
        services.AddSingleton(_ => emailSender.Object);
        services.AddSingleton(_ => new Mock<ISmsSender>().Object);
        services.AddSingleton(_ => new Mock<IPushSender>().Object);
        services.AddSingleton(_ => new Mock<IFaxSender>().Object);

        // ★ 节奏器的时钟冻结在一刻：第 k 次预订于是恰好要等 k 个间隔，相邻两次投递之间至少隔着那一段等待，
        // 与发送路径上其余开销（冷启动、落库、并发名额）无关。用真时钟时，前一次投递被别的东西拖慢，
        // 下一次照样在自己的槽上出发，两次实际投递可以挨得很近：那仍守住了配额，却会让按间隔量的断言时红时绿。
        services.AddSingleton(sp => new NotificationSendPacer(
            sp.GetRequiredService<IOptionsMonitor<NotificationOptions>>(), new FrozenClock()));

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

    /// <summary>★★★ 一条多人消息的每个收件人之间至少隔一个限速间隔。</summary>
    [Fact]
    public async Task EveryRecipientOfASend_IsPacedByRatePerMinute()
    {
        var messageId = await SeedAsync(recipientStatus: NotificationStatus.Pending, count: 3);

        var result = await ServiceProvider.GetRequiredService<INotificationService>().SendAsync(messageId);

        result.Succeeded.ShouldBeTrue(result.Message);
        AssertPaced(expectedSends: 3);
    }

    /// <summary>★★ 「重发失败项」绕开 SendAsync，限速不能在这条路径上缺席。</summary>
    [Fact]
    public async Task ResendingToFailedRecipients_IsPacedToo()
    {
        var messageId = await SeedAsync(recipientStatus: NotificationStatus.Failed, count: 3, messageStatus: NotificationStatus.Failed);

        var result = await ServiceProvider.GetRequiredService<INotificationService>().ResendToFailedRecipientsAsync(messageId);

        result.Succeeded.ShouldBeTrue(result.Message);
        AssertPaced(expectedSends: 3);
    }

    // ── 夹具 ─────────────────────────────────────────────────────────────────

    private void AssertPaced(int expectedSends)
    {
        _sentAt.Count.ShouldBe(expectedSends);
        for (var i = 1; i < _sentAt.Count; i++)
        {
            (_sentAt[i] - _sentAt[i - 1]).ShouldBeGreaterThanOrEqualTo(
                Interval - Tolerance, $"delivery #{i + 1} followed #{i} without waiting for the rate limit");
        }
    }

    /// <summary>停在一刻不动的时钟（见 ConfigureServices 里的说明）。</summary>
    private sealed class FrozenClock : TimeProvider
    {
        private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private async Task<Guid> SeedAsync(
        NotificationStatus recipientStatus, int count, NotificationStatus messageStatus = NotificationStatus.Pending)
    {
        var message = new Message
        {
            Id = Guid.NewGuid(),
            Subject = "Quarterly newsletter",
            Content = "Body",
            Type = NotificationType.Email,
            Category = "General",
            IsTransactional = true,
            Status = messageStatus,
            TotalRecipientCount = count,
            Recipients = [.. Enumerable.Range(0, count)
                .Select(i => new Recipient { Address = $"reader{i}@example.com", Status = recipientStatus })],
        };

        await DbContext.Messages.AddAsync(message);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return message.Id;
    }
}
