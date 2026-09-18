using Tnzi.Storage.Helpers;

namespace Tnzi.Storage.Workspace.Tests.Integration;

/// <summary>
/// 工作区集成测试的 DbContext：<b>七个实体一起装</b>。
/// </summary>
/// <remarks>
/// 本模块的四个服务全都要读写父模块的 <c>FileRecord</c>（建版本改的是父记录、分片完成
/// 落的是父记录、移动文件写的是 <c>FileRecord.FolderId</c>），所以父子两侧的实体必须在
/// 同一个上下文里。<b>刻意不复用父测试项目的那份夹具</b>：父项目的夹具只认父模块的两张表，
/// 而让它认识子模块的五张表就等于让「父不依赖子」这条边在测试里不成立 ——
/// 父测试项目**不引用**子包，正是它能真实演出「宿主没加载这个包」的原因。
/// </remarks>
public class WorkspaceTestDbContext : TnziDbContext<WorkspaceTestDbContext>
{
    public WorkspaceTestDbContext(DbContextOptions<WorkspaceTestDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    public DbSet<FileRecord> FileRecords => Set<FileRecord>();
    public DbSet<FileReference> FileReferences => Set<FileReference>();
    public DbSet<FileVersion> FileVersions => Set<FileVersion>();
    public DbSet<FileShare> FileShares => Set<FileShare>();
    public DbSet<FileUploadSession> FileUploadSessions => Set<FileUploadSession>();
    public DbSet<FileChunk> FileChunks => Set<FileChunk>();
    public DbSet<FileFolder> FileFolders => Set<FileFolder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new Storage.Entities.Configs.FileRecordConfiguration());
        modelBuilder.ApplyConfiguration(new Storage.Entities.Configs.FileReferenceConfiguration());
        modelBuilder.ApplyConfiguration(new FileVersionConfiguration());
        modelBuilder.ApplyConfiguration(new FileShareConfiguration());
        modelBuilder.ApplyConfiguration(new FileUploadSessionConfiguration());
        modelBuilder.ApplyConfiguration(new FileChunkConfiguration());
        modelBuilder.ApplyConfiguration(new FileFolderConfiguration());

        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

/// <summary>
/// 工作区四个服务的集成测试基类。构造出的服务与生产装配同形，只是仓储直接指向测试上下文。
/// </summary>
public abstract class WorkspaceIntegrationTestBase : IntegratedTestBase<WorkspaceTestDbContext>
{
    private readonly string _storagePath = Path.Combine(Path.GetTempPath(), "tnzi-storage-workspace-tests", Guid.NewGuid().ToString("N"));

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

        // 发布目录生命周期事件的服务（FileFolderService）在没有 EventBus 模块的宿主上不该 NRE，
        // 所以给一个 no-op。DefaultValue.Empty 让泛型 PublishAsync<TEvent> 返回已完成的 Task。
        var eventBus = new Mock<Tnzi.EventBus.IEventBus> { DefaultValue = DefaultValue.Empty };
        services.AddSingleton(eventBus.Object);

        // 仓储也进 DI：分享链接的口令失败计数会从**子作用域**解析仓储以逃出外层事务，
        // 测试宿主解析不出来的话那条路径会静默变成 no-op —— 而它恰恰最该被覆盖。
        services.AddScoped<IRepository<FileShare, Guid>>(sp =>
            new EFCoreRepository<WorkspaceTestDbContext, FileShare, Guid>(
                sp.GetRequiredService<WorkspaceTestDbContext>(), serviceProvider: sp));
        services.AddScoped<IRepository<FileRecord, Guid>>(sp =>
            new EFCoreRepository<WorkspaceTestDbContext, FileRecord, Guid>(
                sp.GetRequiredService<WorkspaceTestDbContext>(), serviceProvider: sp));
    }

    /// <summary>父模块的存储服务，工作区用例用它来准备/回读文件记录。</summary>
    protected FileStorageService CreateStorageService(
        StorageOptions? options = null,
        IFileAccessAuthorizer? authorizer = null,
        IFileAccessGrantContext? grantContext = null)
    {
        var effective = options ?? StorageOptions;
        return new FileStorageService(
            Repo<FileRecord>(),
            Repo<FileReference>(),
            Storage,
            new StaticOptionsMonitor<StorageOptions>(effective),
            // 分享用例要看的是「授予表放行了吗」，所以它们传一个真实授权器进来：
            // 放行只能来自授予，不能来自默认的全放行。
            authorizer ?? (grantContext is null ? TestFileAccessAuthorizer.AllowAll() : CreateRealAuthorizer(grantContext)),
            TestPublicFileFieldResolver.Empty(),
            new TestFileUrlSigner(),
            ServiceProvider,
            Guard(effective),
            new FileThumbnailGenerator(Storage, new StaticOptionsMonitor<StorageOptions>(effective)));
    }

    /// <summary>
    /// 目录服务。默认全放行，让既有的目录用例保持原语义（它们关心的是目录树逻辑，
    /// 不是「谁有权访问」）；授权行为本身由传入的策略驱动。
    /// </summary>
    protected FileFolderService CreateFolderService(IFileAccessAuthorizer? authorizer = null)
        => new(
            ServiceProvider,
            Repo<FileFolder>(),
            Repo<FileRecord>(),
            authorizer ?? TestFileAccessAuthorizer.AllowAll());

    protected FileVersionService CreateVersionService(
        IEnumerable<IUploadSanitizer>? sanitizers = null,
        StorageOptions? options = null)
        => new(
            Repo<FileVersion>(),
            Repo<FileRecord>(),
            Storage,
            TestFileAccessAuthorizer.AllowAll(),
            ServiceProvider,
            Guard(options ?? StorageOptions, sanitizers));

    protected FileChunkUploadService CreateChunkUploadService(
        StorageOptions? options = null,
        IEnumerable<IUploadSanitizer>? sanitizers = null)
        => new(
            Repo<FileUploadSession>(),
            Repo<FileChunk>(),
            Repo<FileRecord>(),
            Storage,
            new StaticOptionsMonitor<StorageOptions>(options ?? StorageOptions),
            ServiceProvider,
            Guard(options ?? StorageOptions, sanitizers));

    /// <summary>
    /// 传入同一个 <paramref name="grantContext"/> 给分享服务和读取服务，就能在测试里
    /// 复现真实请求里的那条链路：分享校验写进授予表 → 授权器据此放行。
    /// 默认全放行的授权器让既有用例保持原语义；要看「谁能管理这条链接」时传真实的那个。
    /// </summary>
    protected FileShareService CreateShareService(IFileAccessGrantContext? grantContext = null, IFileAccessAuthorizer? authorizer = null)
        => new(
            Repo<FileShare>(),
            Repo<FileRecord>(),
            authorizer ?? TestFileAccessAuthorizer.AllowAll(),
            grantContext ?? new FileAccessGrantContext(),
            new StaticOptionsMonitor<StorageOptions>(StorageOptions),
            ServiceProvider);

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
            Repo<FileReference>(),
            [],
            grantContext);
    }

    /// <summary>
    /// 父模块的清理服务。工作区用例用它证明「分片合并出的记录不会被孤儿回收选中」——
    /// 那条路径此前把 ReferenceCount 写死成 0，于是分片通道传的正式文件在 72 小时后被静默删除。
    /// </summary>
    protected FileCleanupService CreateCleanupService(StorageOptions options)
        => new(
            Repo<FileRecord>(),
            Repo<FileReference>(),
            Storage,
            new Tnzi.MultiTenancy.CurrentTenant(),
            new StaticOptionsMonitor<StorageOptions>(options),
            ServiceProvider);

    protected ExpiredUploadSessionCleanupContributor CreateCleanupContributor(
        Tnzi.MultiTenancy.ICurrentTenant? currentTenant = null,
        bool multiTenancyEnabled = false)
        => new(
            Repo<FileUploadSession>(),
            Repo<FileChunk>(),
            Storage,
            currentTenant ?? new Tnzi.MultiTenancy.CurrentTenant(),
            NullLogger<ExpiredUploadSessionCleanupContributor>.Instance,
            Microsoft.Extensions.Options.Options.Create(new Tnzi.MultiTenancy.MultiTenancyOptions { Enabled = multiTenancyEnabled }));

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

    protected EFCoreRepository<WorkspaceTestDbContext, TEntity, Guid> Repo<TEntity>() where TEntity : class, Tnzi.Domain.Entities.IEntity<Guid>
        => new(DbContext, serviceProvider: ServiceProvider);

    protected static UploadGuard Guard(StorageOptions options, IEnumerable<IUploadSanitizer>? sanitizers = null)
        => new(new StaticOptionsMonitor<StorageOptions>(options), sanitizers);

    private static string ComputeMd5(byte[] content)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        return Convert.ToHexString(md5.ComputeHash(content)).ToLowerInvariant();
    }
}
