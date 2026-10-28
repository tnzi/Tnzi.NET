using Tnzi.Domain.Entities;
using Tnzi.EFCore;
using Tnzi.Storage.Entities.Configs;

namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// 一条带软删过滤器的业务记录：软删除之后它的文件引用按设计保留，好让记录能被恢复。
/// </summary>
public class SoftDeletableDocument : FullAuditedEntity<Guid>
{
    [FileField]
    public Guid? AttachmentId { get; set; }
}

/// <summary>
/// 文件两张表加上一类会被软删的业务记录，模型里有真实的软删全局过滤器（由 <c>TnziDbContext</c> 施加）。
/// </summary>
public class SoftDeleteProbeDbContext : TnziDbContext<SoftDeleteProbeDbContext>
{
    public SoftDeleteProbeDbContext(DbContextOptions<SoftDeleteProbeDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    public DbSet<FileRecord> FileRecords => Set<FileRecord>();
    public DbSet<FileReference> FileReferences => Set<FileReference>();
    public DbSet<SoftDeletableDocument> Documents => Set<SoftDeletableDocument>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new FileRecordConfiguration());
        modelBuilder.ApplyConfiguration(new FileReferenceConfiguration());
        modelBuilder.Entity<SoftDeletableDocument>().ToTable("SoftDeletableDocument");

        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

/// <summary>
/// 孤立引用校验把<b>软删除</b>的实体当作仍然存在：软删除是可恢复的，恢复时它的头像与附件必须还在。
/// </summary>
/// <remarks>
/// <b>被保护的缺陷</b>：校验器用 <c>DbContext.FindAsync</c> 判存在，旁边的注释说它「忽略软删除过滤器」，
/// 而 EF Core 的 Find 在跟踪器未命中时发的是一条普通查询，全局查询过滤器照常生效。
/// 于是开启孤立引用清理后，每一条被软删除的业务记录的文件引用都被判为孤儿删掉、计数归零，
/// 下一轮孤儿回收物理删除那些文件；管理员恢复记录时附件已经没了。
/// <para>
/// 校验器解析不到实体所属的上下文时一律保守判「存在」，那时「软删仍算存在」恒真、什么都证明不了。
/// 这里的上下文真的解析得到，正控制 <see cref="AnEntityThatIsGone_StillCountsAsMissing"/> 证明查询确实跑了。
/// </para>
/// </remarks>
public class OrphanReferenceSoftDeleteTests : IntegratedTestBase<SoftDeleteProbeDbContext>
{
    private readonly string _storagePath = Path.Combine(Path.GetTempPath(), "tnzi-storage-tests", Guid.NewGuid().ToString("N"));
    private LocalStorage _storage = null!;

    private readonly StorageOptions _options = new()
    {
        Cleanup = { EnableOrphanReferenceCleanup = true }
    };

    protected override void ConfigureServices(IServiceCollection services)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:StoragePath"] = _storagePath })
            .Build();
        _storage = new LocalStorage(configuration, logger: NullLogger<LocalStorage>.Instance);
        services.AddSingleton<IFileStorage>(_storage);
    }

    [Fact]
    public async Task ASoftDeletedEntity_StillCountsAsExisting()
    {
        var document = await SeedDocumentAsync();
        await SoftDeleteAsync(document);

        Assert.True(await CreateValidator().IsEntityExistsAsync(nameof(SoftDeletableDocument), document.Id));
    }

    /// <summary>正控制：真的不存在的实体仍然答「不存在」，否则上一条用例什么都没证明。</summary>
    [Fact]
    public async Task AnEntityThatIsGone_StillCountsAsMissing()
    {
        await SeedDocumentAsync();

        Assert.False(await CreateValidator().IsEntityExistsAsync(nameof(SoftDeletableDocument), Guid.NewGuid()));
    }

    [Fact]
    public async Task ALiveEntity_CountsAsExisting()
    {
        var document = await SeedDocumentAsync();

        Assert.True(await CreateValidator().IsEntityExistsAsync(nameof(SoftDeletableDocument), document.Id));
    }

    /// <summary>
    /// 端到端：软删除记录的附件经过一整轮清理（孤立引用 + 孤儿文件）之后，引用、计数与文件都还在。
    /// </summary>
    [Fact]
    public async Task AFullCleanupPass_KeepsTheAttachmentOfASoftDeletedRecord()
    {
        var file = await SeedFileAsync();
        var document = await SeedDocumentAsync();
        await SeedReferenceAsync(file.Id, document.Id);
        await SoftDeleteAsync(document);

        var cleanup = CreateCleanupService();
        var referencesDeleted = await cleanup.CleanupOrphanReferencesAsync();
        var filesDeleted = await cleanup.CleanupOrphanFilesAsync();

        Assert.Equal(0, referencesDeleted);
        Assert.Equal(0, filesDeleted);
        DbContext.ChangeTracker.Clear();
        var record = await DbContext.FileRecords.FindAsync(file.Id);
        Assert.NotNull(record);
        Assert.Equal(1, record.ReferenceCount);
        Assert.True(await _storage.ExistsAsync(record.Path!));
    }

    private async Task<SoftDeletableDocument> SeedDocumentAsync()
    {
        var document = new SoftDeletableDocument { Id = Guid.NewGuid() };
        DbContext.Documents.Add(document);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return document;
    }

    /// <summary>走生产的软删路径：Remove 由审计钩子改写成「置 IsDeleted」，行留在表里。</summary>
    private async Task SoftDeleteAsync(SoftDeletableDocument document)
    {
        var tracked = await DbContext.Documents.FindAsync(document.Id);
        DbContext.Documents.Remove(tracked!);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var row = await DbContext.Documents.IgnoreQueryFilters().SingleAsync(d => d.Id == document.Id);
        Assert.True(row.IsDeleted);
        DbContext.ChangeTracker.Clear();
    }

    private async Task<FileRecord> SeedFileAsync()
    {
        var path = await _storage.UploadAsync("attachment.txt", new MemoryStream("attachment"u8.ToArray()), "text/plain");
        var record = new FileRecord
        {
            Id = Guid.NewGuid(),
            FileName = "attachment.txt",
            OriginalName = "attachment.txt",
            Extension = ".txt",
            Size = 10,
            Path = path,
            Provider = _storage.ProviderName,
            ContentType = "text/plain",
            ReferenceCount = 1,
            CreationTime = DateTime.UtcNow.AddHours(-200)
        };
        DbContext.FileRecords.Add(record);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return record;
    }

    private async Task SeedReferenceAsync(Guid fileId, Guid documentId)
    {
        DbContext.FileReferences.Add(new FileReference
        {
            Id = Guid.NewGuid(),
            FileId = fileId,
            EntityType = nameof(SoftDeletableDocument),
            EntityId = documentId,
            FieldName = nameof(SoftDeletableDocument.AttachmentId),
            IsTemporary = false,
            CreationTime = DateTime.UtcNow.AddHours(-200)
        });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private EntityManagerOrphanReferenceValidator CreateValidator()
    {
        var entityManager = new Mock<IEntityManager>();
        entityManager.Setup(m => m.GetAllEntityTypes()).Returns([typeof(SoftDeletableDocument)]);
        entityManager.Setup(m => m.GetDbContextTypeForEntity(typeof(SoftDeletableDocument))).Returns(typeof(SoftDeleteProbeDbContext));

        return new EntityManagerOrphanReferenceValidator(
            entityManager.Object,
            ServiceProvider,
            NullLogger<EntityManagerOrphanReferenceValidator>.Instance);
    }

    private FileCleanupService CreateCleanupService()
        => new(
            new EFCoreRepository<SoftDeleteProbeDbContext, FileRecord, Guid>(DbContext, serviceProvider: ServiceProvider),
            new EFCoreRepository<SoftDeleteProbeDbContext, FileReference, Guid>(DbContext, serviceProvider: ServiceProvider),
            _storage,
            new Tnzi.MultiTenancy.CurrentTenant(),
            new StaticOptionsMonitor<StorageOptions>(_options),
            ServiceProvider,
            new OrphanReferenceScanCursor(),
            null,
            Microsoft.Extensions.Options.Options.Create(new Tnzi.MultiTenancy.MultiTenancyOptions { Enabled = false }),
            CreateValidator());

    public override void Dispose()
    {
        base.Dispose();
        try
        {
            if (Directory.Exists(_storagePath))
                Directory.Delete(_storagePath, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清不掉不影响断言
        }
    }
}
