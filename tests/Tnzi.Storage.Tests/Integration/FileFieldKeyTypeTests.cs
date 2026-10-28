using Tnzi.Domain.Entities;
using Tnzi.EFCore;
using Tnzi.Storage.Entities.Configs;

namespace Tnzi.Storage.Tests.Integration;

/// <summary>带 [FileField] 的雪花 long 主键实体。</summary>
public class LongKeyFileDoc : FullAuditedEntity<long>
{
    public string Title { get; set; } = string.Empty;

    [FileField]
    public Guid? AttachmentId { get; set; }
}

/// <summary>带 [FileField] 的数据库自增 int 主键实体（保存前 Id 为 0）。</summary>
public class IntKeyFileDoc : EntityBase<int>
{
    public string Title { get; set; } = string.Empty;

    [FileField]
    public Guid? AttachmentId { get; set; }
}

/// <summary>带 [FileField] 的 Guid 主键实体（对照组）。</summary>
public class GuidKeyFileDoc : FullAuditedEntity<Guid>
{
    public string Title { get; set; } = string.Empty;

    [FileField]
    public Guid? AttachmentId { get; set; }
}

public class FileKeyTypeDbContext : TnziDbContext<FileKeyTypeDbContext>
{
    public FileKeyTypeDbContext(DbContextOptions<FileKeyTypeDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    public DbSet<FileRecord> FileRecords => Set<FileRecord>();

    public DbSet<FileReference> FileReferences => Set<FileReference>();

    public DbSet<LongKeyFileDoc> LongDocs => Set<LongKeyFileDoc>();

    public DbSet<IntKeyFileDoc> IntDocs => Set<IntKeyFileDoc>();

    public DbSet<GuidKeyFileDoc> GuidDocs => Set<GuidKeyFileDoc>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new FileRecordConfiguration());
        modelBuilder.ApplyConfiguration(new FileReferenceConfiguration());
        modelBuilder.Entity<LongKeyFileDoc>(b => { b.ToTable("LongKeyFileDoc"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); });
        modelBuilder.Entity<IntKeyFileDoc>(b => { b.ToTable("IntKeyFileDoc"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedOnAdd(); });
        modelBuilder.Entity<GuidKeyFileDoc>(b => { b.ToTable("GuidKeyFileDoc"); b.HasKey(x => x.Id); });

        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

/// <summary>
/// <c>[FileField]</c> 只在 Guid 主键实体上受支持：文件引用表的 <c>EntityId</c> 是 Guid 列。
/// 其它主键的实体保存文件字段时必须<b>失败</b>，不能安静地跳过。
/// </summary>
/// <remarks>
/// 跳过的后果是数据丢失而不是功能缺失：上传的文件以引用计数 0 落库，保存实体时引用被跳过（只留一条 Warning），
/// 计数停在 0，孤儿清理在保留期过后把文件连同物理存储一起删掉，而实体行仍指着它。
/// 雪花 long 主键是框架一等支持的主键类型，用它的消费方照着文档写就会落进这个坑。
/// </remarks>
public class FileFieldKeyTypeTests : IntegratedTestBase<FileKeyTypeDbContext>
{
    private StorageOptions _storageOptions = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        var entityManager = new Mock<IEntityManager>();
        entityManager.Setup(m => m.GetAllDbContextTypes()).Returns(new[] { typeof(FileKeyTypeDbContext) });
        services.AddSingleton(_ => entityManager.Object);

        services.AddScoped<IFileReferenceProcessor>(sp => CreateProcessor(sp.GetRequiredService<FileKeyTypeDbContext>(), sp));
    }

    private FileReferenceProcessor CreateProcessor(FileKeyTypeDbContext context, IServiceProvider sp)
        => new(
            new EFCoreRepository<FileKeyTypeDbContext, FileReference, Guid>(context, serviceProvider: sp),
            new EFCoreRepository<FileKeyTypeDbContext, FileRecord, Guid>(context, serviceProvider: sp),
            NullLogger<FileReferenceProcessor>.Instance,
            new StaticOptionsMonitor<StorageOptions>(_storageOptions));

    [Fact]
    public async Task SavingAFileOnALongKeyedEntity_FailsAndNamesTheEntity()
    {
        var file = await SeedFileAsync();
        DbContext.LongDocs.Add(new LongKeyFileDoc { Title = "doc", AttachmentId = file.Id });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => DbContext.SaveChangesAsync());

        Assert.Contains(nameof(LongKeyFileDoc), ex.Message);
        Assert.Contains("Guid", ex.Message);
    }

    [Fact]
    public async Task SavingAFileOnADatabaseGeneratedIntKeyedEntity_FailsInsteadOfBeingDropped()
    {
        // 自增主键在保存前是 0：追踪器此前直接跳过整个实体，连 Warning 都没有。
        var file = await SeedFileAsync();
        DbContext.IntDocs.Add(new IntKeyFileDoc { Title = "doc", AttachmentId = file.Id });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => DbContext.SaveChangesAsync());

        Assert.Contains(nameof(IntKeyFileDoc), ex.Message);
    }

    [Fact]
    public async Task ALongKeyedEntityWithoutAFile_SavesNormally()
    {
        DbContext.LongDocs.Add(new LongKeyFileDoc { Title = "no attachment" });

        await DbContext.SaveChangesAsync();

        Assert.Equal(1, await DbContext.LongDocs.CountAsync());
    }

    [Fact]
    public async Task WithFileReferenceTrackingOff_ALongKeyedEntitySavesNormally()
    {
        _storageOptions = new StorageOptions { EnableFileReference = false };
        var file = await SeedFileAsync();
        DbContext.LongDocs.Add(new LongKeyFileDoc { Title = "doc", AttachmentId = file.Id });

        await DbContext.SaveChangesAsync();

        Assert.Equal(1, await DbContext.LongDocs.CountAsync());
    }

    [Fact]
    public async Task DeletingALegacyLongKeyedEntity_DoesNotFail()
    {
        // 修复前保存的行没有任何引用（创建时被跳过），删除它们不该被这条约束挡住。
        var processor = CreateProcessor(DbContext, ServiceProvider);
        var changes = new[]
        {
            new FileReferenceChange
            {
                ChangeType = FileReferenceChangeType.Delete,
                EntityType = nameof(LongKeyFileDoc),
                EntityId = "1234567890123",
                FieldName = nameof(LongKeyFileDoc.AttachmentId),
                FileId = Guid.NewGuid()
            }
        };

        await processor.ProcessChangesAsync(changes);
    }

    [Fact]
    public async Task AGuidKeyedEntity_StillGetsItsReference()
    {
        var file = await SeedFileAsync();
        var doc = new GuidKeyFileDoc { Title = "doc", AttachmentId = file.Id };
        DbContext.GuidDocs.Add(doc);

        await DbContext.SaveChangesAsync();

        DbContext.ChangeTracker.Clear();
        var reference = await DbContext.FileReferences.AsNoTracking().SingleAsync(r => r.FileId == file.Id);
        Assert.Equal(doc.Id, reference.EntityId);
        Assert.Equal(1, (await DbContext.FileRecords.AsNoTracking().SingleAsync(f => f.Id == file.Id)).ReferenceCount);
    }

    private async Task<FileRecord> SeedFileAsync()
    {
        var record = new FileRecord
        {
            Id = Guid.NewGuid(),
            FileName = "a.txt",
            OriginalName = "a.txt",
            Extension = ".txt",
            Size = 1,
            Path = $"files/{Guid.NewGuid():N}.txt",
            Md5Hash = Guid.NewGuid().ToString("N"),
            Provider = "Local",
            ContentType = "text/plain",
            ReferenceCount = 0,
            IsTemporary = true
        };

        DbContext.FileRecords.Add(record);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return record;
    }
}
