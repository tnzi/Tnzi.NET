using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Tnzi.EFCore;
using Tnzi.Security.Claims;
using Tnzi.Storage.Entities.Configs;
using Tnzi.Storage.Helpers;
using Tnzi.Storage.Options;
using Tnzi.Storage.Providers;
using Tnzi.Storage.Services;
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
    }

    protected FileStorageService CreateStorageService(StorageOptions? options = null)
    {
        return CreateStorageService(Storage, options);
    }

    /// <summary>
    /// Build a FileStorageService backed by a caller-supplied IFileStorage (e.g. a fake that
    /// simulates physical-delete failure) so storage-level edge cases can be exercised.
    /// </summary>
    protected FileStorageService CreateStorageService(IFileStorage storage, StorageOptions? options = null)
    {
        var effective = options ?? StorageOptions;
        var optionsMonitor = new Mock<IOptionsMonitor<StorageOptions>>();
        optionsMonitor.Setup(x => x.CurrentValue).Returns(effective);
        return new FileStorageService(
            new EFCoreRepository<StorageTestDbContext, FileRecord, Guid>(DbContext, serviceProvider: ServiceProvider),
            new EFCoreRepository<StorageTestDbContext, FileReference, Guid>(DbContext, serviceProvider: ServiceProvider),
            storage,
            optionsMonitor.Object,
            ServiceProvider);
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

    protected FileShareService CreateShareService()
    {
        return new FileShareService(
            new EFCoreRepository<StorageTestDbContext, Tnzi.Storage.Entities.FileShare, Guid>(DbContext, serviceProvider: ServiceProvider),
            new EFCoreRepository<StorageTestDbContext, FileRecord, Guid>(DbContext, serviceProvider: ServiceProvider),
            ServiceProvider);
    }

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
