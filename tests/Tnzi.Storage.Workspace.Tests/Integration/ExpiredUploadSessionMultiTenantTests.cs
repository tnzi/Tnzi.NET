using Microsoft.Data.Sqlite;
using MsOptions = Microsoft.Extensions.Options.Options;
using Tnzi.MultiTenancy;

namespace Tnzi.Storage.Workspace.Tests.Integration;

/// <summary>
/// 多租户「启用」下的过期分片会话清理隔离（T3 在本模块这一侧）。
/// </summary>
/// <remarks>
/// 与父测试项目的 <c>FileCleanupMultiTenantTests</c> 是同一条逻辑，只是数据属于本程序集：
/// 清理跑在没有 <c>HttpContext</c> 的作用域里，当前租户为空，全局租户过滤器因此会放行**所有**租户 ——
/// 所以贡献者必须先跨租户取 distinct <c>TenantId</c>、再逐个 <c>Change</c> 切上下文。
/// 这条断言拆分前后一字未改；变的只是驱动它的是贡献者而不是父服务的一个方法。
/// </remarks>
public class ExpiredUploadSessionMultiTenantTests : IDisposable
{
    private sealed class MtWorkspaceDbContext : TnziDbContext<MtWorkspaceDbContext>
    {
        public MtWorkspaceDbContext(DbContextOptions<MtWorkspaceDbContext> options, ICurrentUser currentUser, ICurrentTenant currentTenant)
            : base(options, currentUser, currentTenant, multiTenancyOptions: MsOptions.Create(new MultiTenancyOptions { Enabled = true }))
        {
        }

        public DbSet<FileUploadSession> FileUploadSessions => Set<FileUploadSession>();
        public DbSet<FileChunk> FileChunks => Set<FileChunk>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new FileUploadSessionConfiguration());
            modelBuilder.ApplyConfiguration(new FileChunkConfiguration());
            base.OnModelCreating(modelBuilder);
            TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
        }
    }

    private readonly SqliteConnection _connection;
    private readonly MtWorkspaceDbContext _db;
    private readonly CurrentTenant _currentTenant;
    private readonly LocalStorage _storage;
    private readonly string _storagePath = Path.Combine(Path.GetTempPath(), "tnzi-storage-workspace-mt-tests", Guid.NewGuid().ToString("N"));

    public ExpiredUploadSessionMultiTenantTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(u => u.Id).Returns((Guid?)null);
        currentUser.Setup(u => u.TenantId).Returns((Guid?)null);

        _currentTenant = new CurrentTenant();

        var dbOptions = new DbContextOptionsBuilder<MtWorkspaceDbContext>()
            .UseSqlite(_connection)
            .EnableSensitiveDataLogging()
            .Options;
        _db = new MtWorkspaceDbContext(dbOptions, currentUser.Object, _currentTenant);
        _db.Database.EnsureCreated();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:StoragePath"] = _storagePath })
            .Build();
        _storage = new LocalStorage(config, logger: NullLogger<LocalStorage>.Instance);
    }

    private ExpiredUploadSessionCleanupContributor CreateContributor()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ICurrentTenant>(_currentTenant);
        var sp = services.BuildServiceProvider();

        return new ExpiredUploadSessionCleanupContributor(
            new EFCoreRepository<MtWorkspaceDbContext, FileUploadSession, Guid>(_db, serviceProvider: sp),
            new EFCoreRepository<MtWorkspaceDbContext, FileChunk, Guid>(_db, serviceProvider: sp),
            _storage,
            _currentTenant,
            NullLogger<ExpiredUploadSessionCleanupContributor>.Instance,
            MsOptions.Create(new MultiTenancyOptions { Enabled = true }));
    }

    [Fact]
    public async Task CleanupExpiredSessions_IsolatedPerTenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var sessionA = await SeedSessionAsync(tenantA, expired: true);
        var sessionB = await SeedSessionAsync(tenantB, expired: false); // 未过期，应保留

        var deleted = await CreateContributor().CleanupAsync(maxItems: 100);

        Assert.Equal(1, deleted);
        Assert.Equal(0, await _db.FileUploadSessions.IgnoreQueryFilters().CountAsync(s => s.Id == sessionA));
        Assert.Equal(1, await _db.FileUploadSessions.IgnoreQueryFilters().CountAsync(s => s.Id == sessionB));
    }

    [Fact]
    public async Task CleanupExpiredSessions_DeletesEveryTenantsOwnExpiredSession()
    {
        // 只删一个租户的、把别人的漏掉，同样是隔离没做对 —— 只是错在另一边。
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await SeedSessionAsync(tenantA, expired: true);
        await SeedSessionAsync(tenantB, expired: true);

        var deleted = await CreateContributor().CleanupAsync(maxItems: 100);

        Assert.Equal(2, deleted);
        Assert.Equal(0, await _db.FileUploadSessions.IgnoreQueryFilters().CountAsync());
    }

    private async Task<Guid> SeedSessionAsync(Guid tenantId, bool expired)
    {
        var session = new FileUploadSession
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FileName = "s.zip",
            TotalSize = 3,
            ChunkSize = 3,
            TotalChunks = 1,
            CreationTime = DateTime.UtcNow.AddHours(-2),
            ExpiresAt = expired ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow.AddHours(24)
        };
        _db.FileUploadSessions.Add(session);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return session.Id;
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Close();
        _connection.Dispose();
        if (Directory.Exists(_storagePath))
        {
            Directory.Delete(_storagePath, recursive: true);
        }
        GC.SuppressFinalize(this);
    }
}
