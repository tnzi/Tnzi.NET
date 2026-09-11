using Tnzi.EventBus;
using Tnzi.Storage;

namespace Tnzi.EFCore.Tests;

/// <summary>
/// 工作单元「最终提交」阶段的事务护栏回归测试。
///
/// 框架的物理事务是延迟开启的（首次 UoW SaveChanges 才 BEGIN）。最常见的写法
/// —— <c>ExecuteInUnitOfWorkAsync</c> 内只做仓储写入、中途不 flush —— 会让整批变更
/// 缓冲到最终提交那一刻才落库。若最终提交在 SaveChanges **之前**就把事务深度递减到 0，
/// 那次 SaveChanges 会判定「未启用事务」而在自动提交模式下执行：主实体已经落库，
/// 随后的文件引用处理失败时回滚无事可回滚，<c>TnziDbContextHelper</c>
/// 「实体与文件引用要么全成功要么全失败」的承诺落空。
/// </summary>
public class FinalCommitTransactionTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly FinalCommitDbContext _dbContext;
    private readonly IUnitOfWorkManager _manager;
    private readonly SqliteConnection _connection;
    private readonly TransactionObservingInterceptor _interceptor = new();
    private readonly List<IEvent> _publishedEvents = new();
    private readonly ObservingFileReferenceProcessor _processor;

    public FinalCommitTransactionTests()
    {
        var services = new ServiceCollection();

        services.AddSingleton<ICurrentUser>(new MockCurrentUser());
        services.AddSingleton<ICurrentTenant>(new MockCurrentTenant());

        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        services.AddDbContext<FinalCommitDbContext>((_, options) =>
        {
            options.UseSqlite(_connection);
            options.EnableSensitiveDataLogging();
            options.AddInterceptors(_interceptor);
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

        _processor = new ObservingFileReferenceProcessor(() => _dbContext!);
        services.AddScoped<IFileReferenceProcessor>(_ => _processor);

        services.AddScoped<IPostCommitActionQueue, PostCommitActionQueue>();
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();

        _serviceProvider = services.BuildServiceProvider();
        _dbContext = _serviceProvider.GetRequiredService<FinalCommitDbContext>();
        _manager = _serviceProvider.GetRequiredService<IUnitOfWorkManager>();

        _dbContext.Database.EnsureCreated();
    }

    private EFCoreRepository<FinalCommitDbContext, FinalCommitDoc, Guid> CreateRepository()
        => new(_dbContext, options: null, serviceProvider: _serviceProvider, logger: null);

    [Fact]
    public async Task FinalCommit_WhenFileReferenceProcessingFails_RollsBackTheEntity()
    {
        _processor.ThrowOnProcess = true;
        var repository = CreateRepository();

        // 事务内只做仓储写入、不 flush —— ExecuteInUnitOfWorkAsync 的常态形状
        _manager.EnableTransaction();
        var doc = new FinalCommitDoc { Title = "rollback-me", CoverImageId = Guid.NewGuid() };
        await repository.InsertAsync(doc);
        var id = doc.Id;

        // 最终提交：SaveChanges 在此发生，随后文件引用处理抛异常
        await Assert.ThrowsAsync<InvalidOperationException>(() => _manager.CommitTransactionAsync());

        Assert.True(_processor.ProcessCalled, "file reference processing should have been reached");
        Assert.True(_processor.HadPhysicalTransaction, "the final commit's save must run inside a physical transaction");

        // 修复前：那次 SaveChanges 跑在自动提交模式，主实体已落库且回滚无从撤销
        _dbContext.ChangeTracker.Clear();
        var reloaded = await _dbContext.Docs.IgnoreQueryFilters().FirstOrDefaultAsync(d => d.Id == id);
        Assert.Null(reloaded);
    }

    [Fact]
    public async Task FinalCommit_SaveChanges_RunsInsidePhysicalTransaction()
    {
        var repository = CreateRepository();

        _manager.EnableTransaction();
        var doc = new FinalCommitDoc { Title = "commit-me" };
        await repository.InsertAsync(doc);
        var id = doc.Id;

        await _manager.CommitTransactionAsync();

        // 最终提交那次 SaveChanges 必须发生在物理事务内（而不是自动提交）
        Assert.NotEmpty(_interceptor.SaveHadPhysicalTransaction);
        Assert.All(_interceptor.SaveHadPhysicalTransaction, hadTransaction => Assert.True(
            hadTransaction, "every save inside an enabled transaction must run inside a physical transaction"));

        _dbContext.ChangeTracker.Clear();
        var reloaded = await _dbContext.Docs.FirstOrDefaultAsync(d => d.Id == id);
        Assert.NotNull(reloaded);
    }

    [Fact]
    public async Task FinalCommit_WhenCommitFails_DomainEventsAreNotPublished()
    {
        _processor.ThrowOnProcess = true;
        var repository = CreateRepository();

        _manager.EnableTransaction();
        var doc = new FinalCommitDoc { Title = "ghost-event", CoverImageId = Guid.NewGuid() };
        doc.AddDomainEvent(new FinalCommitDocCreatedEvent());
        await repository.InsertAsync(doc);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _manager.CommitTransactionAsync());

        // 领域事件在最终提交的 SaveChanges 里被收集。提交尚未完成，事件必须进入
        // post-commit 队列而不是就地发布；提交失败后队列被清空，事件不应外泄。
        Assert.Empty(_publishedEvents);
    }

    public void Dispose()
    {
        _dbContext?.Dispose();
        _connection?.Dispose();
        _serviceProvider?.Dispose();
        GC.SuppressFinalize(this);
    }
}
