using Microsoft.Extensions.DependencyInjection.Extensions;
using Tnzi.Domain.Entities;
using Tnzi.MultiTenancy;
using Tnzi.Security.Claims;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.Notification.Tests.Integration;

/// <summary>
/// 多租户<b>开启</b>时，排队 / 定时 / 重试的工作项与派发恢复的三遍扫描都要在消息所属的租户里跑。
/// </summary>
/// <remarks>
/// <para>
/// ★★ <b>补的是哪个洞</b>：<c>Message</c> 是多租户实体，全局过滤器是严格等值
/// <c>TenantId == 当前租户</c>，没有 host 旁路；而后台队列与恢复扫描都从根容器开一个新作用域，
/// 里面没有任何租户。于是 <c>SendAsync</c> 首查就查不到那条消息、返回 404，结果被整个丢掉 ——
/// 接口答复「已排队」，行停在 <c>Pending</c>，一封信都没发出去，日志干净。
/// </para>
/// <para>
/// ★ 夹具单独搭一个 <c>MultiTenancy.Enabled=true</c> 的 DbContext（同 Storage 的
/// <c>FileCleanupMultiTenantTests</c>），<c>ICurrentTenant</c> 用真实的 <see cref="CurrentTenant"/>
/// 且与生产一样按作用域注册 —— 租户过滤靠的是「DbContext 与切换租户的那个对象是同一个实例」，
/// 用 mock 代替就测不到这一层。SQLite 不会掩盖这里的任何断言：过滤器是 LINQ 表达式，
/// 在哪个库上都一样求值。
/// </para>
/// <para>
/// ★ 队列用一个<b>只记录</b>的替身，然后由测试在一个<b>全新的作用域</b>里执行工作项 ——
/// 这正是 <c>ChannelQueueService.ExecuteAsync</c> 做的事，也正是租户丢失的那一步。
/// 真实的 <c>ChannelQueueService</c> 是否照样调用 <c>RunAsync</c>，由
/// <c>ChannelQueueServiceTests</c> 单独守着。
/// </para>
/// </remarks>
public class MultiTenantDispatchTests : IntegratedTestBase<MultiTenantNotificationDbContext>
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private readonly List<(string Address, Guid? TenantId)> _sent = [];
    private readonly RecordingQueue _queue = new();
    private readonly DispatchOptions _dispatch = new()
    {
        EnableRecovery = true,
        StuckAfterMinutes = 15,
        RecoveryBatchSize = 50,
        RatePerMinute = 0,
    };

    public MultiTenantDispatchTests()
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        // 基类注册的是一个恒为 null 的 mock；这里要的是真实实现，而且必须与生产一样是 Scoped，
        // 让 DbContext 与 Change() 的调用方拿到同一个实例。
        services.RemoveAll<ICurrentTenant>();
        services.AddScoped<ICurrentTenant, CurrentTenant>();
        services.AddSingleton(MsOptions.Create(new MultiTenancyOptions { Enabled = true }));

        AddRepo<Message>(services);
        AddRepo<Recipient>(services);
        AddRepo<OptOut>(services);
        AddRepo<Preference>(services);

        var entityManagerMock = new Mock<IEntityManager>();
        entityManagerMock.Setup(m => m.GetAllDbContextTypes()).Returns(new[] { typeof(MultiTenantNotificationDbContext) });
        entityManagerMock.Setup(m => m.Initialize());
        services.AddSingleton(_ => entityManagerMock.Object);
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();
        services.AddScoped<IUnitOfWork, EFCoreUnitOfWork<MultiTenantNotificationDbContext>>();

        var options = new Mock<IOptionsMonitor<NotificationOptions>>();
        options.Setup(o => o.CurrentValue).Returns(new NotificationOptions { MaxConcurrency = 4, Dispatch = _dispatch });
        services.AddSingleton(_ => options.Object);

        // 发送器按作用域注册并记下发信那一刻的租户：这是唯一能观察到「切进了哪个租户」的地方。
        services.AddScoped<IEmailSender>(sp => new TenantRecordingEmailSender(sp.GetRequiredService<ICurrentTenant>(), _sent));
        services.AddSingleton(_ => new Mock<ISmsSender>().Object);
        services.AddSingleton(_ => new Mock<IPushSender>().Object);
        services.AddSingleton(_ => new Mock<IFaxSender>().Object);

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
            new EFCoreRepository<MultiTenantNotificationDbContext, TEntity, Guid>(
                sp.GetRequiredService<MultiTenantNotificationDbContext>(), serviceProvider: sp));
        services.AddScoped<IReadOnlyRepository<TEntity, Guid>>(sp =>
            sp.GetRequiredService<IRepository<TEntity, Guid>>());
    }

    // ── 队列工作项：入队时带走租户，执行时切回去 ──────────────────────────────

    /// <summary>★★ 默认路径（不带 SendImmediately）：租户 A 创建的消息在无租户的新作用域里照样发得出去。</summary>
    [Fact]
    public async Task QueuedSend_RestoresTenantContext_SendsTenantMessage()
    {
        Guid messageId;
        using (var creating = ServiceProvider.CreateScope())
        using (creating.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(TenantA))
        {
            var result = await creating.ServiceProvider.GetRequiredService<INotificationService>()
                .CreateAndSendAsync(NewRequest());
            result.Succeeded.ShouldBeTrue(result.Message);
            messageId = result.Data!.Id;
        }

        _queue.Items.Count.ShouldBe(1);
        _queue.Items[0].TenantId.ShouldBe(TenantA);

        await RunQueuedItemsInFreshScopeAsync();

        _sent.ShouldBe([("reader@example.com", (Guid?)TenantA)]);
        (await ReloadAsync(messageId)).Status.ShouldBe(NotificationStatus.Sent);
    }

    /// <summary>定时分支走的是延迟入队，租户同样要随工作项带走。</summary>
    [Fact]
    public async Task ScheduledSend_RestoresTenantContext()
    {
        Guid messageId;
        using (var creating = ServiceProvider.CreateScope())
        using (creating.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(TenantA))
        {
            var request = NewRequest();
            request.ScheduledTime = DateTime.UtcNow.AddMinutes(1);
            var result = await creating.ServiceProvider.GetRequiredService<INotificationService>()
                .CreateAndSendAsync(request);
            result.Succeeded.ShouldBeTrue(result.Message);
            messageId = result.Data!.Id;
        }

        _queue.Items.Count.ShouldBe(1);
        _queue.Items[0].TenantId.ShouldBe(TenantA);

        // 让它到期（绕开过滤器与审计：这是在模拟时间流逝，不是一次业务写入）。
        await DbContext.Set<Message>().IgnoreQueryFilters().Where(m => m.Id == messageId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ScheduledTime, DateTime.UtcNow.AddMinutes(-1)));

        await RunQueuedItemsInFreshScopeAsync();

        _sent.ShouldBe([("reader@example.com", (Guid?)TenantA)]);
        (await ReloadAsync(messageId)).Status.ShouldBe(NotificationStatus.Sent);
    }

    /// <summary>延迟重试与排队发送走同一条队列，同样要带租户。</summary>
    [Fact]
    public async Task Retry_RestoresTenantContext()
    {
        var messageId = await SeedAsync(TenantA, NotificationStatus.Failed, recipientStatus: NotificationStatus.Failed);

        using (var retrying = ServiceProvider.CreateScope())
        using (retrying.ServiceProvider.GetRequiredService<ICurrentTenant>().Change(TenantA))
        {
            var result = await retrying.ServiceProvider.GetRequiredService<INotificationRetryService>().RetryAsync(messageId);
            result.Succeeded.ShouldBeTrue(result.Message);
        }

        _queue.Items.Count.ShouldBe(1);
        _queue.Items[0].TenantId.ShouldBe(TenantA);

        await RunQueuedItemsInFreshScopeAsync();

        _sent.ShouldBe([("reader@example.com", (Guid?)TenantA)]);
        (await ReloadAsync(messageId)).Status.ShouldBe(NotificationStatus.Sent);
    }

    // ── 派发恢复：三遍扫描逐租户跑 ──────────────────────────────────────────

    /// <summary>两个租户各有一条卡住的批次，一轮恢复要把两条都续发完。</summary>
    [Fact]
    public async Task RecoverOnce_ClaimsStalledMessagesOfEveryTenant()
    {
        var a = await SeedAsync(TenantA, NotificationStatus.Sending, lastModified: MinutesAgo(60), address: "a@example.com");
        var b = await SeedAsync(TenantB, NotificationStatus.Sending, lastModified: MinutesAgo(60), address: "b@example.com");

        await RecoverOnceAsync();

        AssertSentInOwnTenant();
        (await ReloadAsync(a)).Status.ShouldBe(NotificationStatus.Sent);
        (await ReloadAsync(b)).Status.ShouldBe(NotificationStatus.Sent);
    }

    [Fact]
    public async Task RecoverOnce_ClaimsDueScheduledOfEveryTenant()
    {
        var a = await SeedAsync(TenantA, NotificationStatus.Scheduled, scheduledTime: MinutesAgo(60), address: "a@example.com");
        var b = await SeedAsync(TenantB, NotificationStatus.Scheduled, scheduledTime: MinutesAgo(60), address: "b@example.com");

        await RecoverOnceAsync();

        AssertSentInOwnTenant();
        (await ReloadAsync(a)).Status.ShouldBe(NotificationStatus.Sent);
        (await ReloadAsync(b)).Status.ShouldBe(NotificationStatus.Sent);
    }

    [Fact]
    public async Task RecoverOnce_ClaimsDueDeferredRecipientsOfEveryTenant()
    {
        var a = await SeedAsync(TenantA, NotificationStatus.Scheduled,
            recipientStatus: NotificationStatus.Scheduled, deferredUntil: MinutesAgo(5), address: "a@example.com");
        var b = await SeedAsync(TenantB, NotificationStatus.Scheduled,
            recipientStatus: NotificationStatus.Scheduled, deferredUntil: MinutesAgo(5), address: "b@example.com");

        await RecoverOnceAsync();

        AssertSentInOwnTenant();
        (await ReloadAsync(a)).Status.ShouldBe(NotificationStatus.Sent);
        (await ReloadAsync(b)).Status.ShouldBe(NotificationStatus.Sent);
    }

    /// <summary>对照：没有任何租户的（host 级）消息照旧被接手 —— 逐租户迭代不能把 null 那一格漏掉。</summary>
    [Fact]
    public async Task RecoverOnce_StillClaimsMessagesWithoutATenant()
    {
        var host = await SeedAsync(tenantId: null, NotificationStatus.Sending, lastModified: MinutesAgo(60));

        await RecoverOnceAsync();

        _sent.ShouldBe([("reader@example.com", (Guid?)null)]);
        (await ReloadAsync(host)).Status.ShouldBe(NotificationStatus.Sent);
    }

    /// <summary>一轮跑完第二轮什么都不该再发：逐租户迭代没有让同一条被两个租户的上下文各发一遍。</summary>
    [Fact]
    public async Task RecoverOnce_SendsEachMessageExactlyOnce()
    {
        await SeedAsync(TenantA, NotificationStatus.Sending, lastModified: MinutesAgo(60), address: "a@example.com");
        await SeedAsync(TenantB, NotificationStatus.Sending, lastModified: MinutesAgo(60), address: "b@example.com");

        await RecoverOnceAsync();
        await RecoverOnceAsync();

        _sent.Count.ShouldBe(2);
    }

    // ── 夹具 ─────────────────────────────────────────────────────────────────

    private static CreateNotificationRequest NewRequest() => new()
    {
        Type = NotificationType.Email,
        Subject = "Tenant newsletter",
        Content = "Body",
        IsTransactional = true,
        Recipients = [new RecipientInput { Address = "reader@example.com" }],
    };

    private static DateTime MinutesAgo(int minutes) => DateTime.UtcNow.AddMinutes(-minutes);

    /// <summary>与 <c>ChannelQueueService.ExecuteAsync</c> 同形：根容器开新作用域，把工作项交给它。</summary>
    private async Task RunQueuedItemsInFreshScopeAsync()
    {
        foreach (var item in _queue.Items.ToList())
        {
            using var scope = ServiceProvider.CreateScope();
            await item.RunAsync(scope.ServiceProvider, CancellationToken.None);
        }
    }

    private Task RecoverOnceAsync()
        => new NotificationDispatchBackgroundService(
                ServiceProvider,
                ServiceProvider.GetRequiredService<IOptionsMonitor<NotificationOptions>>(),
                new Mock<ILogger<NotificationDispatchBackgroundService>>().Object,
                MsOptions.Create(new MultiTenancyOptions { Enabled = true }))
            .RecoverOnceAsync(_dispatch, CancellationToken.None);

    /// <summary>★ 每一封都是在<b>它自己的</b>租户里发出去的 —— 逐租户不是「关掉过滤器然后全发」。</summary>
    private void AssertSentInOwnTenant()
    {
        _sent.Count.ShouldBe(2);
        _sent.ShouldContain(("a@example.com", (Guid?)TenantA));
        _sent.ShouldContain(("b@example.com", (Guid?)TenantB));
    }

    private async Task<Message> ReloadAsync(Guid messageId)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.Set<Message>().IgnoreQueryFilters().AsNoTracking().FirstAsync(m => m.Id == messageId);
    }

    private async Task<Guid> SeedAsync(
        Guid? tenantId,
        NotificationStatus status,
        NotificationStatus recipientStatus = NotificationStatus.Pending,
        DateTime? scheduledTime = null,
        DateTime? lastModified = null,
        DateTime? deferredUntil = null,
        string address = "reader@example.com")
    {
        var message = new Message
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Subject = "Quarterly newsletter",
            Content = "Body",
            Type = NotificationType.Email,
            Category = "General",
            IsTransactional = true,
            Status = status,
            ScheduledTime = scheduledTime,
            TotalRecipientCount = 1,
            MaxRetryCount = 3,
            Recipients =
            [
                new Recipient
                {
                    TenantId = tenantId,
                    Address = address,
                    Status = recipientStatus,
                    DeferredUntil = deferredUntil,
                }
            ],
        };

        await DbContext.Set<Message>().AddAsync(message);
        await DbContext.SaveChangesAsync();

        if (lastModified != null)
        {
            // 审计拦截器每次 SaveChanges 都把 LastModificationTime 覆写成"现在"，要回到过去只能绕开跟踪器。
            await DbContext.Set<Message>().IgnoreQueryFilters().Where(m => m.Id == message.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.LastModificationTime, lastModified));
        }

        DbContext.ChangeTracker.Clear();
        return message.Id;
    }

    /// <summary>记下发信那一刻的租户。</summary>
    private sealed class TenantRecordingEmailSender(ICurrentTenant currentTenant, List<(string Address, Guid? TenantId)> sent) : IEmailSender
    {
        public Task<SendResult> SendToAsync(string to, string? name, string subject, string body, bool isHtml = true,
            List<EmailAttachment>? attachments = null, CancellationToken cancellationToken = default)
        {
            sent.Add((to, currentTenant.Id));
            return Task.FromResult(SendResult.CreateSuccess("stub-id"));
        }
    }

    /// <summary>只记录、不执行的队列替身：执行由测试在新作用域里做。</summary>
    private sealed class RecordingQueue : INotificationQueueService
    {
        public List<NotificationWorkItem> Items { get; } = [];

        public Task EnqueueAsync(NotificationWorkItem workItem)
        {
            Items.Add(workItem);
            return Task.CompletedTask;
        }

        public Task EnqueueWithDelayAsync(NotificationWorkItem workItem, TimeSpan delay) => EnqueueAsync(workItem);
    }
}

/// <summary>多租户开启的测试 DbContext。</summary>
public class MultiTenantNotificationDbContext : TnziDbContext<MultiTenantNotificationDbContext>
{
    public MultiTenantNotificationDbContext(
        DbContextOptions<MultiTenantNotificationDbContext> options,
        ICurrentUser currentUser,
        ICurrentTenant currentTenant)
        : base(options, currentUser, currentTenant,
            multiTenancyOptions: MsOptions.Create(new MultiTenancyOptions { Enabled = true }))
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new Entities.Configs.MessageConfiguration());
        modelBuilder.ApplyConfiguration(new Entities.Configs.RecipientConfiguration());
        modelBuilder.ApplyConfiguration(new Entities.Configs.AttachmentConfiguration());
        modelBuilder.ApplyConfiguration(new Entities.Configs.OptOutConfiguration());
        modelBuilder.ApplyConfiguration(new Entities.Configs.PreferenceConfiguration());

        base.OnModelCreating(modelBuilder);

        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}
