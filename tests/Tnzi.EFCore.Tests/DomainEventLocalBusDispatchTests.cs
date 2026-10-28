using Tnzi.EventBus;

namespace Tnzi.EFCore.Tests;

/// <summary>
/// 领域事件经<b>真实</b>本地总线派发到具体处理器的端到端回归测试。
/// </summary>
/// <remarks>
/// <para>
/// <c>TnziDbContextHelper</c> 从实体上收下来的领域事件是 <see cref="IEvent"/> 列表，调用
/// <c>IEventBus.PublishAsync</c> 时推断出的类型参数就是 <see cref="IEvent"/>。总线若按类型参数找处理器，
/// 找的是 <c>IEventHandler&lt;IEvent&gt;</c>：具体事件的处理器一个都取不到，只留一条 Debug 日志。
/// 用替身总线断言「发布了」看不出这一点（替身恰好把 <c>TEvent = IEvent</c> 钉成了期望），所以这里用真总线。
/// </para>
/// <para>
/// 事务内保存时领域事件延迟到提交后发布；全局工作单元在 Action 返回后才提交，此时保存方的
/// <c>Change()</c> 早已退出。租户必须在保存那一刻捕获。
/// </para>
/// </remarks>
public class DomainEventLocalBusDispatchTests : IDisposable
{
    public class Observed
    {
        public List<(FinalCommitDocCreatedEvent Event, Guid? AmbientTenant)> Calls { get; } = new();

        /// <summary>非空时第一次处理就取消它：模拟「保存之后、其余事件派发之前」请求被取消。</summary>
        public CancellationTokenSource? CancelOnFirstCall { get; set; }
    }

    public class DocCreatedHandler : IEventHandler<FinalCommitDocCreatedEvent>
    {
        private readonly Observed _observed;
        private readonly ICurrentTenant _currentTenant;

        public DocCreatedHandler(Observed observed, ICurrentTenant currentTenant)
        {
            _observed = observed;
            _currentTenant = currentTenant;
        }

        public Task HandleAsync(FinalCommitDocCreatedEvent @event, CancellationToken cancellationToken = default)
        {
            _observed.Calls.Add((@event, _currentTenant.Id));
            _observed.CancelOnFirstCall?.Cancel();
            return Task.CompletedTask;
        }
    }

    private readonly ServiceProvider _serviceProvider;
    private readonly FinalCommitDbContext _dbContext;
    private readonly IUnitOfWorkManager _manager;
    private readonly SqliteConnection _connection;
    private readonly Observed _observed = new();

    public DomainEventLocalBusDispatchTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddSingleton<ICurrentUser>(new MockCurrentUser());
        services.AddScoped<ICurrentTenant, CurrentTenant>();

        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        services.AddDbContext<FinalCommitDbContext>((_, options) => options.UseSqlite(_connection));

        var entityManagerMock = new Mock<IEntityManager>();
        entityManagerMock.Setup(m => m.GetAllDbContextTypes()).Returns(new[] { typeof(FinalCommitDbContext) });
        services.AddSingleton(_ => entityManagerMock.Object);

        services.AddSingleton(_observed);
        services.AddScoped<IEventHandler<FinalCommitDocCreatedEvent>, DocCreatedHandler>();
        services.AddSingleton<IEventBus>(sp => new LocalEventBus(sp, sp.GetRequiredService<ILogger<LocalEventBus>>()));

        services.AddScoped<IPostCommitActionQueue, PostCommitActionQueue>();
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();

        _serviceProvider = services.BuildServiceProvider();
        _dbContext = _serviceProvider.GetRequiredService<FinalCommitDbContext>();
        _manager = _serviceProvider.GetRequiredService<IUnitOfWorkManager>();

        _dbContext.Database.EnsureCreated();
    }

    [Fact]
    public async Task SaveChanges_WithoutTransaction_DomainEventReachesItsConcreteHandler()
    {
        var doc = new FinalCommitDoc { Title = "no-tx" };
        var domainEvent = new FinalCommitDocCreatedEvent();
        doc.AddDomainEvent(domainEvent);
        _dbContext.Docs.Add(doc);

        await _dbContext.SaveChangesAsync();

        var call = Assert.Single(_observed.Calls);
        Assert.Same(domainEvent, call.Event);
    }

    [Fact]
    public async Task SaveChanges_WithoutTransaction_RequestCancelledAfterTheSave_StillPublishesEveryDomainEvent()
    {
        using var requestAborted = new CancellationTokenSource();
        _observed.CancelOnFirstCall = requestAborted;

        var first = new FinalCommitDoc { Title = "cancel-1" };
        first.AddDomainEvent(new FinalCommitDocCreatedEvent());
        var second = new FinalCommitDoc { Title = "cancel-2" };
        second.AddDomainEvent(new FinalCommitDocCreatedEvent());
        _dbContext.Docs.AddRange(first, second);

        await _dbContext.SaveChangesAsync(requestAborted.Token);

        Assert.Equal(2, _observed.Calls.Count);
    }

    [Fact]
    public async Task SaveChanges_InsideTransaction_DomainEventReachesItsConcreteHandlerAfterCommit()
    {
        _manager.EnableTransaction();

        var doc = new FinalCommitDoc { Title = "deferred" };
        doc.AddDomainEvent(new FinalCommitDocCreatedEvent());
        _dbContext.Docs.Add(doc);

        await _manager.SaveChangesAsync();
        Assert.Empty(_observed.Calls);

        await _manager.CommitTransactionAsync();

        Assert.Single(_observed.Calls);
    }

    [Fact]
    public async Task SaveChanges_InsideTransaction_TenantChangeEndsBeforeCommit_HandlerRunsUnderTheSavingTenant()
    {
        var tenantId = Guid.NewGuid();
        var currentTenant = _serviceProvider.GetRequiredService<ICurrentTenant>();
        var domainEvent = new FinalCommitDocCreatedEvent();

        _manager.EnableTransaction();

        using (currentTenant.Change(tenantId))
        {
            var doc = new FinalCommitDoc { Title = "tenant" };
            doc.AddDomainEvent(domainEvent);
            _dbContext.Docs.Add(doc);

            await _manager.SaveChangesAsync();
        }

        // 提交发生在 Change() 作用域之外
        await _manager.CommitTransactionAsync();

        var call = Assert.Single(_observed.Calls);
        Assert.Equal(tenantId, domainEvent.TenantId);
        Assert.Equal(tenantId, call.AmbientTenant);
        Assert.Null(currentTenant.Id);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }
}
