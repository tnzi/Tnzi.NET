namespace Tnzi.EFCore.Tests;

/// <summary>
/// 带 <c>[FileField]</c> 的硬删实体按条件删除：在工作单元里、或关掉自动保存时，必须能结束并删干净。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ 这类实体不能走 <c>ExecuteDelete</c>（文件引用要经变更跟踪才清理得到），所以 <c>DeleteAsync(predicate)</c>
/// 分批「查一批、RemoveRange、保存」。此前只在「立即保存」时保存：事务开着或 <c>AutoSaveChanges=false</c> 时
/// 行从未离开数据库，下一轮同一条查询按主键身份解析回到已跟踪（Deleted）的同一批实例，循环永不结束，
/// 请求占着事务与连接一直空转。<c>DeleteAsync(id)</c> 也走这条路。
/// </para>
/// <para>
/// 用例带超时令牌：缺陷在时查询被取消、用例失败，而不是把测试进程挂住。
/// </para>
/// </remarks>
public class FileFieldPredicateDeleteTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;

    public FileFieldPredicateDeleteTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUser>(new MockCurrentUser());
        services.AddSingleton<ICurrentTenant>(new MockCurrentTenant());
        services.AddDbContext<FileFieldDeleteDbContext>(options => options.UseSqlite(_connection));

        var entityManager = new Mock<IEntityManager>();
        entityManager.Setup(m => m.GetAllDbContextTypes()).Returns([typeof(FileFieldDeleteDbContext)]);
        services.AddSingleton(entityManager.Object);
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();

        _serviceProvider = services.BuildServiceProvider();

        using var scope = _serviceProvider.CreateScope();
        scope.ServiceProvider.GetRequiredService<FileFieldDeleteDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task DeleteById_InsideTransaction_FinishesAndCommitDeletesTheRow()
    {
        var ids = await SeedAsync("a", "b");
        using var scope = _serviceProvider.CreateScope();
        var (repository, manager, _) = Resolve(scope);
        using var cts = new CancellationTokenSource(Timeout);

        manager.EnableTransaction();
        await repository.DeleteAsync(ids[0], cts.Token);
        await manager.CommitTransactionAsync(cts.Token);

        Assert.Equal(["b"], await RemainingNamesAsync());
    }

    [Fact]
    public async Task DeleteByPredicate_InsideTransaction_DeletesMoreRowsThanOneBatch()
    {
        await SeedAsync("x1", "x2", "x3", "x4", "x5", "keep");
        using var scope = _serviceProvider.CreateScope();
        var (repository, manager, _) = Resolve(scope, batchSize: 2);
        using var cts = new CancellationTokenSource(Timeout);

        manager.EnableTransaction();
        await repository.DeleteAsync(d => d.Name.StartsWith("x"), cts.Token);
        await manager.CommitTransactionAsync(cts.Token);

        Assert.Equal(["keep"], await RemainingNamesAsync());
    }

    [Fact]
    public async Task DeleteByPredicate_InsideTransaction_RollbackKeepsTheRows()
    {
        await SeedAsync("x1", "x2", "x3");
        using var scope = _serviceProvider.CreateScope();
        var (repository, manager, _) = Resolve(scope, batchSize: 2);
        using var cts = new CancellationTokenSource(Timeout);

        manager.EnableTransaction();
        await repository.DeleteAsync(d => d.Name.StartsWith("x"), cts.Token);
        await manager.RollbackTransactionAsync(cts.Token);

        Assert.Equal(["x1", "x2", "x3"], await RemainingNamesAsync());
    }

    /// <summary>
    /// 关掉自动保存 = 仓储不替调用方保存：返回时一行都还没删，调用方保存之后全部删除。
    /// </summary>
    [Fact]
    public async Task DeleteByPredicate_WithAutoSaveOff_DefersToTheCallersSave()
    {
        await SeedAsync("x1", "x2", "x3", "keep");
        using var scope = _serviceProvider.CreateScope();
        var (repository, _, context) = Resolve(scope, batchSize: 2, autoSave: false);
        using var cts = new CancellationTokenSource(Timeout);

        await repository.DeleteAsync(d => d.Name.StartsWith("x"), cts.Token);

        Assert.Equal(["keep", "x1", "x2", "x3"], await RemainingNamesAsync());

        await context.SaveChangesAsync(cts.Token);

        Assert.Equal(["keep"], await RemainingNamesAsync());
    }

    /// <summary>对照组：无事务、自动保存开着（原本就能工作的那条路）。</summary>
    [Fact]
    public async Task DeleteByPredicate_WithoutTransaction_DeletesImmediately()
    {
        await SeedAsync("x1", "x2", "x3", "keep");
        using var scope = _serviceProvider.CreateScope();
        var (repository, _, _) = Resolve(scope, batchSize: 2);
        using var cts = new CancellationTokenSource(Timeout);

        await repository.DeleteAsync(d => d.Name.StartsWith("x"), cts.Token);

        Assert.Equal(["keep"], await RemainingNamesAsync());
    }

    private (EFCoreRepository<FileFieldDeleteDbContext, FileFieldDoc, Guid> Repository, IUnitOfWorkManager Manager, FileFieldDeleteDbContext Context)
        Resolve(IServiceScope scope, int batchSize = 1000, bool autoSave = true)
    {
        var context = scope.ServiceProvider.GetRequiredService<FileFieldDeleteDbContext>();
        var options = Microsoft.Extensions.Options.Options.Create(new EFCoreOptions { AutoSaveChanges = autoSave, BatchSize = batchSize });
        var repository = new EFCoreRepository<FileFieldDeleteDbContext, FileFieldDoc, Guid>(context, options, scope.ServiceProvider);
        return (repository, scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>(), context);
    }

    private async Task<List<Guid>> SeedAsync(params string[] names)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FileFieldDeleteDbContext>();
        var docs = names.Select(n => new FileFieldDoc { Name = n, AttachmentId = Guid.NewGuid() }).ToList();
        context.Docs.AddRange(docs);
        await context.SaveChangesAsync();
        return docs.Select(d => d.Id).ToList();
    }

    private async Task<List<string>> RemainingNamesAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FileFieldDeleteDbContext>();
        return await context.Docs.AsNoTracking().Select(d => d.Name).OrderBy(n => n).ToListAsync();
    }
}

/// <summary>带文件字段、<b>不</b>软删的测试实体：只能经变更跟踪硬删。</summary>
public class FileFieldDoc : EntityBase<Guid>
{
    public string Name { get; set; } = string.Empty;

    [FileField]
    public Guid? AttachmentId { get; set; }
}

public class FileFieldDeleteDbContext : TnziDbContext<FileFieldDeleteDbContext>
{
    public FileFieldDeleteDbContext(DbContextOptions<FileFieldDeleteDbContext> options, ICurrentUser currentUser, ICurrentTenant? currentTenant = null)
        : base(options, currentUser, currentTenant)
    {
    }

    public DbSet<FileFieldDoc> Docs => Set<FileFieldDoc>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FileFieldDoc>(b =>
        {
            b.ToTable("FileFieldDocs");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(50);
        });

        base.OnModelCreating(modelBuilder);
    }
}
