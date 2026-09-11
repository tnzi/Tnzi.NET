using Tnzi.EventBus;
using Tnzi.Storage;

namespace Tnzi.EFCore.Tests;

/// <summary>
/// 领域事件派发链路回归测试。
///
/// <c>TnziDbContextHelper</c> 在 SaveChanges 成功后收集实体上的领域事件并交给
/// <c>IEventBus</c>。它经 <c>DbContextServiceResolver.GetServiceProvider</c> 取容器，
/// 而 <c>IInfrastructure&lt;IServiceProvider&gt;</c> 给出的是 EF **内部**容器 ——
/// 应用注册的服务在那里一个都看不到。拿不到总线时派发方法直接 return，
/// 领域事件被静默丢弃：调用方没有异常、没有日志、没有事件。
/// </summary>
public class DomainEventDispatchTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly FinalCommitDbContext _dbContext;
    private readonly IUnitOfWorkManager _manager;
    private readonly SqliteConnection _connection;
    private readonly List<IEvent> _publishedEvents = new();

    public DomainEventDispatchTests()
    {
        var services = new ServiceCollection();

        services.AddSingleton<ICurrentUser>(new MockCurrentUser());
        services.AddSingleton<ICurrentTenant>(new MockCurrentTenant());

        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        // 与框架的 AddTnziDbContext 同款重载（(serviceProvider, options)），
        // 使 CoreOptionsExtension.ApplicationServiceProvider 是作用域容器而非根容器
        services.AddDbContext<FinalCommitDbContext>((_, options) =>
        {
            options.UseSqlite(_connection);
            options.EnableSensitiveDataLogging();
        });

        var entityManagerMock = new Mock<IEntityManager>();
        entityManagerMock.Setup(m => m.GetAllDbContextTypes()).Returns(new[] { typeof(FinalCommitDbContext) });
        services.AddSingleton(_ => entityManagerMock.Object);

        var eventBusMock = new Mock<IEventBus>();
        eventBusMock
            .Setup(b => b.PublishAsync(It.IsAny<IEvent>(), It.IsAny<CancellationToken>()))
            .Callback<IEvent, CancellationToken>((e, _) => _publishedEvents.Add(e))
            .Returns(Task.CompletedTask);
        services.AddSingleton(_ => eventBusMock.Object);

        services.AddScoped<IPostCommitActionQueue, PostCommitActionQueue>();
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();

        _serviceProvider = services.BuildServiceProvider();
        _dbContext = _serviceProvider.GetRequiredService<FinalCommitDbContext>();
        _manager = _serviceProvider.GetRequiredService<IUnitOfWorkManager>();

        _dbContext.Database.EnsureCreated();
    }

    [Fact]
    public async Task SaveChanges_WithoutTransaction_PublishesDomainEvents()
    {
        var doc = new FinalCommitDoc { Title = "no-tx" };
        doc.AddDomainEvent(new FinalCommitDocCreatedEvent());
        _dbContext.Docs.Add(doc);

        await _dbContext.SaveChangesAsync();

        var published = Assert.Single(_publishedEvents);
        Assert.IsType<FinalCommitDocCreatedEvent>(published);
    }

    [Fact]
    public async Task SaveChanges_InsideTransaction_DefersDomainEventsUntilCommit()
    {
        _manager.EnableTransaction();

        var doc = new FinalCommitDoc { Title = "deferred" };
        doc.AddDomainEvent(new FinalCommitDocCreatedEvent());
        _dbContext.Docs.Add(doc);

        await _manager.SaveChangesAsync();

        // 事务仍活跃：事件必须排进 post-commit 队列，而不是就地发布
        Assert.Empty(_publishedEvents);
        Assert.Equal(1, _serviceProvider.GetRequiredService<IPostCommitActionQueue>().Count);

        await _manager.CommitTransactionAsync();

        Assert.Single(_publishedEvents);
    }

    [Fact]
    public async Task SaveChanges_WithFileReferenceProcessorMissing_StillPublishesDomainEvents()
    {
        // 文件引用处理器缺席（未加载 Storage 模块）不应影响领域事件派发
        Assert.Null(_serviceProvider.GetService<IFileReferenceProcessor>());

        var doc = new FinalCommitDoc { Title = "no-storage", CoverImageId = Guid.NewGuid() };
        doc.AddDomainEvent(new FinalCommitDocCreatedEvent());
        _dbContext.Docs.Add(doc);

        await _dbContext.SaveChangesAsync();

        Assert.Single(_publishedEvents);
    }

    public void Dispose()
    {
        _dbContext?.Dispose();
        _connection?.Dispose();
        _serviceProvider?.Dispose();
        GC.SuppressFinalize(this);
    }
}
