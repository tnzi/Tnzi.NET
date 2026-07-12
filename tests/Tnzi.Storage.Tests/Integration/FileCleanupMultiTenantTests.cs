using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;
using Tnzi.EFCore;
using Tnzi.MultiTenancy;
using Tnzi.Security.Claims;
using Tnzi.Storage.Entities.Configs;
using Tnzi.Storage.Helpers;
using Tnzi.Storage.Providers;
using Tnzi.TestBase;

namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// 多租户「启用」下的清理隔离测试（T3）。
///
/// 单独搭建一个 MultiTenancy.Enabled=true 的 DbContext（共享同一个 CurrentTenant 实例），
/// 验证清理任务遍历每个租户、在该租户上下文内借助框架全局租户过滤器只删自己租户的数据，
/// 不会跨租户误删别人的文件。
/// </summary>
public class FileCleanupMultiTenantTests : IDisposable
{
    private sealed class MtStorageDbContext : TnziDbContext<MtStorageDbContext>
    {
        public MtStorageDbContext(DbContextOptions<MtStorageDbContext> options, ICurrentUser currentUser, ICurrentTenant currentTenant)
            : base(options, currentUser, currentTenant, multiTenancyOptions: MsOptions.Create(new MultiTenancyOptions { Enabled = true }))
        {
        }

        public DbSet<FileRecord> FileRecords => Set<FileRecord>();
        public DbSet<FileReference> FileReferences => Set<FileReference>();
        public DbSet<FileVersion> FileVersions => Set<FileVersion>();
        public DbSet<Tnzi.Storage.Entities.FileShare> FileShares => Set<Tnzi.Storage.Entities.FileShare>();
        public DbSet<FileUploadSession> FileUploadSessions => Set<FileUploadSession>();
        public DbSet<FileChunk> FileChunks => Set<FileChunk>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new FileRecordConfiguration());
            modelBuilder.ApplyConfiguration(new FileReferenceConfiguration());
            modelBuilder.ApplyConfiguration(new FileVersionConfiguration());
            modelBuilder.ApplyConfiguration(new FileShareConfiguration());
            modelBuilder.ApplyConfiguration(new FileUploadSessionConfiguration());
            modelBuilder.ApplyConfiguration(new FileChunkConfiguration());
            base.OnModelCreating(modelBuilder);
            TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
        }
    }

    private readonly SqliteConnection _connection;
    private readonly MtStorageDbContext _db;
    private readonly CurrentTenant _currentTenant;
    private readonly LocalStorage _storage;
    private readonly StorageOptions _options = new();
    private readonly string _storagePath = Path.Combine(Path.GetTempPath(), "tnzi-storage-mt-tests", Guid.NewGuid().ToString("N"));

    public FileCleanupMultiTenantTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(u => u.Id).Returns((Guid?)null);
        currentUser.Setup(u => u.TenantId).Returns((Guid?)null);

        _currentTenant = new CurrentTenant();

        var dbOptions = new DbContextOptionsBuilder<MtStorageDbContext>()
            .UseSqlite(_connection)
            .EnableSensitiveDataLogging()
            .Options;
        _db = new MtStorageDbContext(dbOptions, currentUser.Object, _currentTenant);
        _db.Database.EnsureCreated();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:StoragePath"] = _storagePath })
            .Build();
        _storage = new LocalStorage(config, logger: NullLogger<LocalStorage>.Instance);
    }

    private FileCleanupService CreateService()
    {
        // ServiceProvider 用于 ApplicationService 的 logger 等懒属性；这里只需 LoggerFactory。
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ICurrentTenant>(_currentTenant);
        var sp = services.BuildServiceProvider();

        return new FileCleanupService(
            new EFCoreRepository<MtStorageDbContext, FileRecord, Guid>(_db, serviceProvider: sp),
            new EFCoreRepository<MtStorageDbContext, FileReference, Guid>(_db, serviceProvider: sp),
            new EFCoreRepository<MtStorageDbContext, FileUploadSession, Guid>(_db, serviceProvider: sp),
            new EFCoreRepository<MtStorageDbContext, FileChunk, Guid>(_db, serviceProvider: sp),
            _storage,
            _currentTenant,
            new StaticOptionsMonitor<StorageOptions>(_options),
            sp,
            MsOptions.Create(new MultiTenancyOptions { Enabled = true }));
    }

    [Fact]
    public async Task CleanupOrphanFiles_IsolatedPerTenant_DeletesBothTenantsOwnOrphans()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await SeedOrphanAsync(tenantA, "a.txt", agedHours: 200);
        await SeedOrphanAsync(tenantB, "b.txt", agedHours: 200);

        var service = CreateService();

        var deleted = await service.CleanupOrphanFilesAsync();

        // 两个租户各自的孤儿都被各自上下文清理
        Assert.Equal(2, deleted);
        Assert.Equal(0, await _db.FileRecords.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task CleanupOrphanFiles_DoesNotDeleteCrossTenant_WhenOnlyOneTenantHasOrphans()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        // tenantA 有过期孤儿（应删）
        await SeedOrphanAsync(tenantA, "a-orphan.txt", agedHours: 200, referenceCount: 0);
        // tenantB 的文件仍被引用（不应删）
        var keepB = await SeedOrphanAsync(tenantB, "b-keep.txt", agedHours: 200, referenceCount: 1);

        var service = CreateService();

        var deleted = await service.CleanupOrphanFilesAsync();

        Assert.Equal(1, deleted);
        var survivors = await _db.FileRecords.IgnoreQueryFilters().ToListAsync();
        Assert.Single(survivors);
        Assert.Equal(keepB.Id, survivors[0].Id);
        Assert.Equal(tenantB, survivors[0].TenantId);
    }

    [Fact]
    public async Task CleanupExpiredSessions_IsolatedPerTenant()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var sessionA = await SeedSessionAsync(tenantA, expired: true);
        var sessionB = await SeedSessionAsync(tenantB, expired: false); // 未过期，应保留

        var service = CreateService();

        var deleted = await service.CleanupExpiredUploadSessionsAsync();

        Assert.Equal(1, deleted);
        Assert.Equal(0, await _db.FileUploadSessions.IgnoreQueryFilters().CountAsync(s => s.Id == sessionA));
        Assert.Equal(1, await _db.FileUploadSessions.IgnoreQueryFilters().CountAsync(s => s.Id == sessionB));
    }

    // ---- seed helpers (绕过 SaveChanges 审计填租户，直接写 TenantId) ----

    private async Task<FileRecord> SeedOrphanAsync(Guid tenantId, string name, int agedHours, int referenceCount = 0)
    {
        using var stream = new MemoryStream("orphan"u8.ToArray());
        var path = await _storage.UploadAsync(name, stream, "text/plain");
        var record = new FileRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FileName = name,
            OriginalName = name,
            Extension = Path.GetExtension(name),
            Size = 6,
            Path = path,
            Provider = _storage.ProviderName,
            ContentType = "text/plain",
            ReferenceCount = 1, // 先插入为 1，避免 EF HasDefaultValue(1) 把 CLR 默认 0 当成"未设置"
            CreationTime = DateTime.UtcNow.AddHours(-agedHours)
        };
        _db.FileRecords.Add(record);
        await _db.SaveChangesAsync();

        // 通过一次 UPDATE 显式写入目标 ReferenceCount（0 才能精确落库）
        if (referenceCount != 1)
        {
            record.ReferenceCount = referenceCount;
            _db.FileRecords.Update(record);
            await _db.SaveChangesAsync();
        }

        _db.ChangeTracker.Clear();
        return record;
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
