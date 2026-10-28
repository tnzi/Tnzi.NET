using Microsoft.Extensions.Options;
using Tnzi.EventBus;
using Tnzi.Storage;

namespace Tnzi.EFCore.Tests;

/// <summary>带文件字段、软删与并发戳的测试实体。</summary>
public class WriteHostDoc : FullAuditedEntity<Guid>, IConcurrencyStamp
{
    public string Title { get; set; } = string.Empty;

    [FileField]
    public Guid? AttachmentId { get; set; }

    public string ConcurrencyStamp { get; set; } = string.Empty;
}

/// <summary>带唯一索引的测试实体：提交时撞唯一约束用。</summary>
public class WriteHostProbe : EntityBase<Guid>
{
    public string Code { get; set; } = string.Empty;
}

/// <summary>
/// 形状与文件引用记录相同的测试实体（只有创建时间与租户两类审计列），
/// 由测试用引用处理器在 <c>SaveChanges</c> 的引用处理阶段经仓储新增。
/// </summary>
public class WriteHostReference : EntityBase<Guid>, IHasCreationTime, IMultiTenant
{
    public Guid? TenantId { get; set; }

    public Guid FileId { get; set; }

    public DateTime CreationTime { get; set; }
}

public class WriteHostDbContext : TnziDbContext<WriteHostDbContext>
{
    public WriteHostDbContext(DbContextOptions<WriteHostDbContext> options, ICurrentUser currentUser, ICurrentTenant? currentTenant = null)
        : base(options, currentUser, currentTenant)
    {
    }

    public DbSet<WriteHostDoc> Docs => Set<WriteHostDoc>();

    public DbSet<WriteHostProbe> Probes => Set<WriteHostProbe>();

    public DbSet<WriteHostReference> References => Set<WriteHostReference>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WriteHostDoc>(b =>
        {
            b.ToTable("WriteHostDocs");
            b.HasKey(x => x.Id);
            b.Property(x => x.Title).HasMaxLength(200);
        });

        modelBuilder.Entity<WriteHostProbe>(b =>
        {
            b.ToTable("WriteHostProbes");
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(50);
            b.HasIndex(x => x.Code).IsUnique();
        });

        modelBuilder.Entity<WriteHostReference>(b =>
        {
            b.ToTable("WriteHostReferences");
            b.HasKey(x => x.Id);
        });

        // 基类在实体配置之后挂软删 / 多租户过滤器
        base.OnModelCreating(modelBuilder);
    }
}

/// <summary>
/// 记录 <c>SaveChanges</c> 交给引用处理器的每一条文件引用变更；<see cref="OnProcess"/> 可在处理阶段追加写入。
/// </summary>
public sealed class RecordingFileReferenceProcessor : IFileReferenceProcessor
{
    public List<FileReferenceChange> Changes { get; } = new();

    public Func<IReadOnlyList<FileReferenceChange>, CancellationToken, Task>? OnProcess { get; set; }

    public async Task ProcessChangesAsync(IReadOnlyList<FileReferenceChange> changes, CancellationToken cancellationToken = default)
    {
        Changes.AddRange(changes);
        if (OnProcess != null)
        {
            await OnProcess(changes, cancellationToken);
        }
    }

    public Task PublishDeleteEventsAsync(IReadOnlyList<FileReferenceChange> changes, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

/// <summary>
/// 仓储与工作单元的写路径测试宿主：真实 SQLite + <c>AddTnziDbContext</c>（与运行期同一条装配路径）
/// + 工作单元管理器，每个「请求」一个 DI 作用域。
/// </summary>
public sealed class RepositoryWriteHost : IDisposable
{
    private readonly SqliteConnection _connection;

    public RepositoryWriteHost(bool multiTenancy = false)
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentUser>(User);
        services.AddSingleton<ICurrentTenant>(Tenant);
        services.AddSingleton<IOptions<MultiTenancyOptions>>(Microsoft.Extensions.Options.Options.Create(new MultiTenancyOptions { Enabled = multiTenancy }));
        services.AddSingleton<IFileReferenceProcessor>(Processor);

        var eventBus = new Mock<IEventBus>();
        eventBus
            .Setup(b => b.PublishAsync(It.IsAny<IEvent>(), It.IsAny<CancellationToken>()))
            .Callback<IEvent, CancellationToken>((e, _) => PublishedEvents.Add(e))
            .Returns(Task.CompletedTask);
        services.AddSingleton(eventBus.Object);
        services.AddScoped<IPostCommitActionQueue, PostCommitActionQueue>();
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();
        services.AddTnziDbContext<WriteHostDbContext>(o => o.UseSqlite(_connection), isPrimary: true);

        Provider = services.BuildServiceProvider();

        using var scope = CreateScope();
        scope.ServiceProvider.GetRequiredService<WriteHostDbContext>().Database.EnsureCreated();
    }

    public ServiceProvider Provider { get; }

    public MockCurrentUser User { get; } = new();

    public MockCurrentTenant Tenant { get; } = new();

    public RecordingFileReferenceProcessor Processor { get; } = new();

    /// <summary>经事件总线真正发布出去的领域事件（事务内的会排到提交之后）。</summary>
    public List<IEvent> PublishedEvents { get; } = new();

    public IServiceScope CreateScope() => Provider.CreateScope();

    public static WriteHostDbContext Context(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<WriteHostDbContext>();

    public static IUnitOfWorkManager Manager(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

    public static EFCoreRepository<WriteHostDbContext, TEntity, Guid> Repository<TEntity>(IServiceScope scope)
        where TEntity : class, IEntity<Guid>
        => new(Context(scope), options: null, serviceProvider: scope.ServiceProvider);

    /// <summary>新作用域、不跟踪、忽略过滤器读回库里的那一行（不受被测作用域的变更跟踪器影响）。</summary>
    public async Task<TEntity?> ReadAsync<TEntity>(Guid id) where TEntity : class, IEntity<Guid>
    {
        using var scope = CreateScope();
        return await Context(scope).Set<TEntity>().IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(e => e.Id == id);
    }

    /// <summary>在独立作用域里直接经 <c>DbContext</c> 落库若干行（种子数据），返回时引用处理器的记录已清空。</summary>
    public async Task SeedAsync(params object[] entities)
    {
        using (var scope = CreateScope())
        {
            var context = Context(scope);
            context.AddRange(entities);
            await context.SaveChangesAsync();
        }

        Processor.Changes.Clear();
    }

    public void Dispose()
    {
        Provider.Dispose();
        _connection.Dispose();
    }
}
