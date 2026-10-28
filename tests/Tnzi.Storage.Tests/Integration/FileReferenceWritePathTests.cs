using Tnzi.Data;
using Tnzi.Domain.Entities;
using Tnzi.EFCore;
using Tnzi.MultiTenancy;
using Tnzi.Storage.Entities.Configs;

namespace Tnzi.Storage.Tests.Integration;

/// <summary>带 [FileField] 的业务实体（多租户）。</summary>
public class TenantFileDoc : FullAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }

    public string Title { get; set; } = string.Empty;

    [FileField]
    public Guid? AttachmentId { get; set; }
}

/// <summary>多租户开启的最小上下文：文件记录、文件引用与一个带文件字段的业务实体。</summary>
public class TenantFileDbContext : TnziDbContext<TenantFileDbContext>
{
    public TenantFileDbContext(DbContextOptions<TenantFileDbContext> options, ICurrentUser currentUser, ICurrentTenant? currentTenant = null)
        : base(options, currentUser, currentTenant)
    {
    }

    public DbSet<FileRecord> FileRecords => Set<FileRecord>();

    public DbSet<FileReference> FileReferences => Set<FileReference>();

    public DbSet<TenantFileDoc> Docs => Set<TenantFileDoc>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new FileRecordConfiguration());
        modelBuilder.ApplyConfiguration(new FileReferenceConfiguration());
        modelBuilder.Entity<TenantFileDoc>(b =>
        {
            b.ToTable("TenantFileDoc");
            b.HasKey(x => x.Id);
            b.Property(x => x.Title).HasMaxLength(200);
        });

        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

/// <summary>
/// 真实 <see cref="FileReferenceProcessor"/> 在两条写路径上的引用账：事务里保存、以及「不跟踪读出 → 换文件 → 写回」。
/// </summary>
/// <remarks>
/// <para>
/// ★ 事务里（全局工作单元 / <c>ExecuteInUnitOfWorkAsync</c>）处理器经仓储新增的引用记录只经主保存之后的补存落库。
/// 那次补存若不套审计，多租户下引用行的 <c>TenantId</c> 是 NULL：严格等值过滤让写入它的租户自己都查不到它，
/// 按引用授权读文件被拒、删记录时找不到引用（计数永不递减）、再存同一文件时去重失效。
/// </para>
/// <para>
/// ★ 仓储读方法默认不跟踪，写回的实例附加时原值即新值，换文件不产生任何引用变更：新文件的计数停在原值、
/// 开了孤儿清理就会被物理删除；旧文件的引用永不释放。
/// </para>
/// </remarks>
public class FileReferenceWritePathTests : IntegratedTestBase<TenantFileDbContext>
{
    private static readonly Guid TenantId = Guid.NewGuid();

    protected override void ConfigureDbContextOptions(DbContextOptionsBuilder options)
        => options.UseTnziMultiTenancy(true);

    protected override void ConfigureServices(IServiceCollection services)
    {
        var tenant = new Mock<ICurrentTenant>();
        tenant.Setup(t => t.Id).Returns(TenantId);
        tenant.Setup(t => t.IsAvailable).Returns(true);
        services.AddScoped(_ => tenant.Object);

        var entityManager = new Mock<IEntityManager>();
        entityManager.Setup(m => m.GetAllDbContextTypes()).Returns(new[] { typeof(TenantFileDbContext) });
        services.AddSingleton(_ => entityManager.Object);
        services.AddScoped<IPostCommitActionQueue, PostCommitActionQueue>();
        services.AddScoped<IUnitOfWorkManager, UnitOfWorkManager>();

        services.AddScoped<IFileReferenceProcessor>(sp =>
        {
            var context = sp.GetRequiredService<TenantFileDbContext>();
            return new FileReferenceProcessor(
                new EFCoreRepository<TenantFileDbContext, FileReference, Guid>(context, serviceProvider: sp),
                new EFCoreRepository<TenantFileDbContext, FileRecord, Guid>(context, serviceProvider: sp),
                NullLogger<FileReferenceProcessor>.Instance,
                new StaticOptionsMonitor<StorageOptions>(new StorageOptions()));
        });
    }

    private EFCoreRepository<TenantFileDbContext, TenantFileDoc, Guid> Docs()
        => new(DbContext, serviceProvider: ServiceProvider);

    [Fact]
    public async Task SavingAFileFieldInsideATransaction_CreatesAReferenceOwnedByTheTenant()
    {
        var file = await SeedFileAsync();
        var manager = ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

        manager.EnableTransaction();
        await Docs().InsertAsync(new TenantFileDoc { Title = "doc", AttachmentId = file.Id });
        await manager.CommitTransactionAsync();

        DbContext.ChangeTracker.Clear();
        var reference = await DbContext.FileReferences.IgnoreQueryFilters().AsNoTracking().SingleAsync(r => r.FileId == file.Id);
        Assert.Equal(TenantId, reference.TenantId);
        Assert.NotEqual(default, reference.CreationTime);

        // 写入它的租户看得见它（按引用授权、删除时找引用、保存时去重都走这条过滤）
        Assert.True(await DbContext.FileReferences.AnyAsync(r => r.FileId == file.Id));
        Assert.Equal(1, (await DbContext.FileRecords.AsNoTracking().SingleAsync(f => f.Id == file.Id)).ReferenceCount);
    }

    [Fact]
    public async Task ReplacingTheFileOnANoTrackingRead_MovesTheReference()
    {
        var oldFile = await SeedFileAsync();
        var newFile = await SeedFileAsync();
        var doc = new TenantFileDoc { Title = "doc", AttachmentId = oldFile.Id };
        await Docs().InsertAsync(doc);
        DbContext.ChangeTracker.Clear();

        var detached = await Docs().FirstOrDefaultAsync(d => d.Id == doc.Id);
        detached!.AttachmentId = newFile.Id;
        await Docs().UpdateAsync(detached);

        DbContext.ChangeTracker.Clear();
        var references = await DbContext.FileReferences.AsNoTracking().Where(r => r.EntityId == doc.Id).ToListAsync();
        Assert.Equal([newFile.Id], references.Select(r => r.FileId));
        Assert.Equal(0, (await DbContext.FileRecords.AsNoTracking().SingleAsync(f => f.Id == oldFile.Id)).ReferenceCount);
        Assert.Equal(1, (await DbContext.FileRecords.AsNoTracking().SingleAsync(f => f.Id == newFile.Id)).ReferenceCount);
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
        };

        DbContext.FileRecords.Add(record);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return record;
    }
}
