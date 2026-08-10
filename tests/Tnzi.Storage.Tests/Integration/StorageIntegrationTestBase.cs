using Tnzi.EFCore;
using Tnzi.Storage.Entities.Configs;
using Tnzi.Storage.Helpers;
using Tnzi.TestBase;

namespace Tnzi.Storage.Tests.Integration;

public class StorageTestDbContext : TnziDbContext<StorageTestDbContext>
{
    public StorageTestDbContext(DbContextOptions<StorageTestDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    public DbSet<FileRecord> FileRecords => Set<FileRecord>();
    public DbSet<FileReference> FileReferences => Set<FileReference>();
    public DbSet<FileVersion> FileVersions => Set<FileVersion>();
    public DbSet<Tnzi.Storage.Entities.FileShare> FileShares => Set<Tnzi.Storage.Entities.FileShare>();
    public DbSet<FileUploadSession> FileUploadSessions => Set<FileUploadSession>();
    public DbSet<FileChunk> FileChunks => Set<FileChunk>();
    public DbSet<FileFolder> FileFolders => Set<FileFolder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new FileRecordConfiguration());
        modelBuilder.ApplyConfiguration(new FileReferenceConfiguration());
        modelBuilder.ApplyConfiguration(new FileVersionConfiguration());
        modelBuilder.ApplyConfiguration(new FileShareConfiguration());
        modelBuilder.ApplyConfiguration(new FileUploadSessionConfiguration());
        modelBuilder.ApplyConfiguration(new FileChunkConfiguration());
        modelBuilder.ApplyConfiguration(new FileFolderConfiguration());

        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

public abstract class StorageIntegrationTestBase : IntegratedTestBase<StorageTestDbContext>
{
    private readonly string _storagePath = Path.Combine(Path.GetTempPath(), "tnzi-storage-tests", Guid.NewGuid().ToString("N"));
    protected LocalStorage Storage { get; private set; } = null!;
    protected StorageOptions StorageOptions { get; } = new()
    {
        MaxFileSize = 50 * 1024 * 1024,
        AllowedExtensions = [".txt", ".jpg", ".png", ".zip"],
        AutoGenerateThumbnail = false
    };

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:StoragePath"] = _storagePath
            })
            .Build();

        Storage = new LocalStorage(configuration, logger: NullLogger<LocalStorage>.Instance);

        services.AddSingleton<IFileStorage>(Storage);

        // No-op event bus so services that publish lifecycle events (e.g. FileFolderService)
        // do not NRE when no real EventBus module is loaded in the test host.
        // DefaultValue.Empty makes the generic PublishAsync<TEvent> return a completed Task
        // (not null) without per-generic-arg setup.
        var eventBus = new Mock<Tnzi.EventBus.IEventBus> { DefaultValue = DefaultValue.Empty };
        services.AddSingleton(eventBus.Object);

        // 仓储也进 DI（此前只在工厂方法里手工 new）：服务要从**子作用域**解析仓储来
        // 逃出外层事务时（分享链接的口令失败计数就是），测试宿主必须解析得出来，
        // 否则那条路径在测试里静默变成 no-op —— 而它恰恰是最需要被覆盖的一条。
        services.AddScoped<IRepository<Tnzi.Storage.Entities.FileShare, Guid>>(sp =>
            new EFCoreRepository<StorageTestDbContext, Tnzi.Storage.Entities.FileShare, Guid>(
                sp.GetRequiredService<StorageTestDbContext>(), serviceProvider: sp));
        services.AddScoped<IRepository<FileRecord, Guid>>(sp =>
            new EFCoreRepository<StorageTestDbContext, FileRecord, Guid>(
                sp.GetRequiredService<StorageTestDbContext>(), serviceProvider: sp));
    }

    protected FileStorageService CreateStorageService(
        StorageOptions? options = null,
        IPublicFileFieldResolver? publicFieldResolver = null,
        IFileAccessAuthorizer? authorizer = null,
        IFileAccessGrantContext? grantContext = null,
        IEnumerable<IUploadSanitizer>? sanitizers = null)
    {
        return CreateStorageService(Storage, options, publicFieldResolver, authorizer, grantContext, sanitizers);
    }

    /// <summary>
    /// Build a FileStorageService backed by a caller-supplied IFileStorage (e.g. a fake that
    /// simulates physical-delete failure) so storage-level edge cases can be exercised.
    /// </summary>
    protected FileStorageService CreateStorageService(
        IFileStorage storage,
        StorageOptions? options = null,
        IPublicFileFieldResolver? publicFieldResolver = null,
        IFileAccessAuthorizer? authorizer = null,
        IFileAccessGrantContext? grantContext = null,
        IEnumerable<IUploadSanitizer>? sanitizers = null)
    {
        var effective = options ?? StorageOptions;
        var optionsMonitor = new Mock<IOptionsMonitor<StorageOptions>>();
        optionsMonitor.Setup(x => x.CurrentValue).Returns(effective);
        return new FileStorageService(
            new EFCoreRepository<StorageTestDbContext, FileRecord, Guid>(DbContext, serviceProvider: ServiceProvider),
            new EFCoreRepository<StorageTestDbContext, FileReference, Guid>(DbContext, serviceProvider: ServiceProvider),
            storage,
            optionsMonitor.Object,
            // 分享用例要看的是"授予表放行了吗",所以它们传一个 DenyAll 授权器进来:
            // 放行只能来自授予,不能来自默认的全放行。
            authorizer ?? BuildAuthorizer(grantContext),
            publicFieldResolver ?? TestPublicFileFieldResolver.Empty(),
            new TestFileUrlSigner(),
            ServiceProvider,
            sanitizers);
    }

    protected FileFolderService CreateFolderService()
    {
        return new FileFolderService(
            ServiceProvider,
            new EFCoreRepository<StorageTestDbContext, FileFolder, Guid>(DbContext, serviceProvider: ServiceProvider),
            new EFCoreRepository<StorageTestDbContext, FileRecord, Guid>(DbContext, serviceProvider: ServiceProvider));
    }

    protected FileVersionService CreateVersionService()
    {
        return new FileVersionService(
            new EFCoreRepository<StorageTestDbContext, FileVersion, Guid>(DbContext, serviceProvider: ServiceProvider),
            new EFCoreRepository<StorageTestDbContext, FileRecord, Guid>(DbContext, serviceProvider: ServiceProvider),
            Storage,
            TestFileAccessAuthorizer.AllowAll(),
            ServiceProvider);
    }

    protected FileChunkUploadService CreateChunkUploadService(StorageOptions? options = null)
    {
        return new FileChunkUploadService(
            new EFCoreRepository<StorageTestDbContext, FileUploadSession, Guid>(DbContext, serviceProvider: ServiceProvider),
            new EFCoreRepository<StorageTestDbContext, FileChunk, Guid>(DbContext, serviceProvider: ServiceProvider),
            new EFCoreRepository<StorageTestDbContext, FileRecord, Guid>(DbContext, serviceProvider: ServiceProvider),
            Storage,
            new StaticOptionsMonitor<StorageOptions>(options ?? StorageOptions),
            ServiceProvider);
    }

    protected FileReferenceProcessor CreateReferenceProcessor(StorageOptions? options = null)
    {
        return new FileReferenceProcessor(
            new EFCoreRepository<StorageTestDbContext, FileReference, Guid>(DbContext, serviceProvider: ServiceProvider),
            new EFCoreRepository<StorageTestDbContext, FileRecord, Guid>(DbContext, serviceProvider: ServiceProvider),
            NullLogger<FileReferenceProcessor>.Instance,
            new StaticOptionsMonitor<StorageOptions>(options ?? StorageOptions));
    }

    /// <summary>
    /// 传入同一个 <paramref name="grantContext"/> 给分享服务和读取服务，就能在测试里
    /// 复现真实请求里的那条链路：分享校验写进授予表 → 授权器据此放行。
    /// </summary>
    protected FileShareService CreateShareService(IFileAccessGrantContext? grantContext = null)
    {
        return new FileShareService(
            new EFCoreRepository<StorageTestDbContext, Tnzi.Storage.Entities.FileShare, Guid>(DbContext, serviceProvider: ServiceProvider),
            new EFCoreRepository<StorageTestDbContext, FileRecord, Guid>(DbContext, serviceProvider: ServiceProvider),
            TestFileAccessAuthorizer.AllowAll(),
            grantContext ?? new FileAccessGrantContext(),
            new StaticOptionsMonitor<StorageOptions>(StorageOptions),
            ServiceProvider);
    }

    /// <summary>
    /// 真实的 <see cref="FileAccessAuthorizer"/>（匿名调用者、无权限体系），用来验证
    /// 授予表在完整判定链里的位置：读放行、写和签发不放行。
    /// </summary>
    protected FileAccessAuthorizer CreateRealAuthorizer(IFileAccessGrantContext grantContext)
    {
        var anonymous = new Mock<ICurrentUser>();
        anonymous.SetupGet(u => u.IsAuthenticated).Returns(false);
        anonymous.SetupGet(u => u.Id).Returns((Guid?)null);

        return new FileAccessAuthorizer(
            anonymous.Object,
            new StaticOptionsMonitor<StorageOptions>(StorageOptions),
            new EFCoreRepository<StorageTestDbContext, FileReference, Guid>(DbContext, serviceProvider: ServiceProvider),
            [],
            grantContext);
    }

    /// <summary>
    /// 默认给存储服务的授权器：给了授予表就用真实实现（分享用例要的），
    /// 否则沿用全放行（其余用例只关心存储逻辑本身）。
    /// </summary>
    private IFileAccessAuthorizer BuildAuthorizer(IFileAccessGrantContext? grantContext)
        => grantContext is null ? TestFileAccessAuthorizer.AllowAll() : CreateRealAuthorizer(grantContext);

    protected FileCleanupService CreateCleanupService(
        Tnzi.MultiTenancy.ICurrentTenant? currentTenant = null,
        IOrphanReferenceValidator? orphanReferenceValidator = null,
        bool multiTenancyEnabled = false)
    {
        return new FileCleanupService(
            new EFCoreRepository<StorageTestDbContext, FileRecord, Guid>(DbContext, serviceProvider: ServiceProvider),
            new EFCoreRepository<StorageTestDbContext, FileReference, Guid>(DbContext, serviceProvider: ServiceProvider),
            new EFCoreRepository<StorageTestDbContext, FileUploadSession, Guid>(DbContext, serviceProvider: ServiceProvider),
            new EFCoreRepository<StorageTestDbContext, FileChunk, Guid>(DbContext, serviceProvider: ServiceProvider),
            Storage,
            currentTenant ?? new Tnzi.MultiTenancy.CurrentTenant(),
            new StaticOptionsMonitor<StorageOptions>(StorageOptions),
            ServiceProvider,
            Microsoft.Extensions.Options.Options.Create(new Tnzi.MultiTenancy.MultiTenancyOptions { Enabled = multiTenancyEnabled }),
            orphanReferenceValidator);
    }

    protected async Task<FileRecord> CreateStoredFileAsync(string originalName, byte[] content, string? tags = null)
    {
        using var stream = new MemoryStream(content);
        var savedPath = await Storage.UploadAsync(originalName, stream, FileTypeHelper.GetContentType(Path.GetExtension(originalName)));
        var record = new FileRecord
        {
            Id = Guid.NewGuid(),
            FileName = originalName,
            OriginalName = originalName,
            Extension = Path.GetExtension(originalName),
            Size = content.LongLength,
            Path = savedPath,
            Md5Hash = ComputeMd5(content),
            Provider = Storage.ProviderName,
            ContentType = FileTypeHelper.GetContentType(Path.GetExtension(originalName)),
            ReferenceCount = 1,
            Tags = tags
        };

        DbContext.FileRecords.Add(record);
        await DbContext.SaveChangesAsync();
        return record;
    }

    public override void Dispose()
    {
        base.Dispose();
        if (Directory.Exists(_storagePath))
        {
            Directory.Delete(_storagePath, recursive: true);
        }
    }

    private static string ComputeMd5(byte[] content)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        return Convert.ToHexString(md5.ComputeHash(content)).ToLowerInvariant();
    }
}
