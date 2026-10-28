using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Tnzi.EFCore;

namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// 引用计数在并发下不丢、不被覆盖；回收与删除在动手的那一刻按库里的当前值复核。
/// </summary>
/// <remarks>
/// <para>
/// <b>被保护的缺陷</b>：计数一律「读出来 ++ / -- 再整行写回」，而 <see cref="FileRecord"/> 没有并发令牌。
/// 两条记录并发绑定同一份文件，都读到 1、都写回 2，实际持有者却是 3 个，之后两次释放就归零、文件被物理删除；
/// 改名这类整行写回同样会把别人刚加上的计数覆盖回旧值。孤儿回收先读出候选、再逐条照快照删除，
/// 期间一次 MD5 复用命中、或一次实体绑定刚把它重新持有，用户刚拿到的那条记录随即连对象一起没了。
/// </para>
/// <para>
/// 这里的并发是确定性地排出来的：第二个作用域（同一个 SQLite 连接上的另一个 DbContext）先把记录读进自己的变更跟踪器，
/// 第一个作用域再改，第二个作用域随后在过期的那份上动手；数据库侧的「刚刚提交的并发修改」由一个命令拦截器
/// 在目标语句执行之前插进去。
/// </para>
/// </remarks>
public class ReferenceCountConcurrencyTests : StorageIntegrationTestBase
{
    private readonly BeforeCommandInterceptor _interceptor = new();

    protected override void ConfigureDbContextOptions(DbContextOptionsBuilder options)
        => options.AddInterceptors(_interceptor);

    private string RecordTable => DbContext.Model.FindEntityType(typeof(FileRecord))!.GetTableName()!;

    private async Task<int?> PersistedCountAsync(Guid id)
        => await DbContext.FileRecords.AsNoTracking().Where(f => f.Id == id).Select(f => (int?)f.ReferenceCount).SingleOrDefaultAsync();

    private static FileReferenceChange Bind(Guid fileId, Guid entityId) => new()
    {
        ChangeType = FileReferenceChangeType.Create,
        EntityType = "Document",
        EntityId = entityId.ToString(),
        FieldName = "Attachment",
        FileId = fileId
    };

    private static FileReferenceProcessor ProcessorOver(StorageTestDbContext context, IServiceProvider services, StorageOptions options)
        => new(
            new EFCoreRepository<StorageTestDbContext, FileReference, Guid>(context, serviceProvider: services),
            new EFCoreRepository<StorageTestDbContext, FileRecord, Guid>(context, serviceProvider: services),
            NullLogger<FileReferenceProcessor>.Instance,
            new StaticOptionsMonitor<StorageOptions>(options));

    private FileStorageService StorageServiceOver(StorageTestDbContext context, IServiceProvider services)
    {
        var options = new StaticOptionsMonitor<StorageOptions>(StorageOptions);
        return new FileStorageService(
            new EFCoreRepository<StorageTestDbContext, FileRecord, Guid>(context, serviceProvider: services),
            new EFCoreRepository<StorageTestDbContext, FileReference, Guid>(context, serviceProvider: services),
            Storage,
            options,
            TestFileAccessAuthorizer.AllowAll(),
            TestPublicFileFieldResolver.Empty(),
            new TestFileUrlSigner(),
            services,
            CreateUploadGuard(StorageOptions),
            new FileThumbnailGenerator(Storage, options, NullLogger<FileThumbnailGenerator>.Instance),
            UnlimitedStorageQuota.Instance);
    }

    /// <summary>两条记录绑定同一份文件，第二个作用域手里是绑定之前读到的那份：两次绑定都要算数。</summary>
    [Fact]
    public async Task TwoBindingsOfTheSameFile_FromScopesThatReadTheSameCount_BothCount()
    {
        var file = await CreateStoredFileAsync("shared.txt", "shared"u8.ToArray());
        DbContext.ChangeTracker.Clear();

        using var scope = ServiceProvider.CreateScope();
        var other = scope.ServiceProvider.GetRequiredService<StorageTestDbContext>();
        Assert.Equal(1, (await other.FileRecords.FindAsync(file.Id))!.ReferenceCount);

        await CreateReferenceProcessor().ProcessChangesAsync([Bind(file.Id, Guid.NewGuid())]);
        await DbContext.SaveChangesAsync();

        await ProcessorOver(other, scope.ServiceProvider, StorageOptions).ProcessChangesAsync([Bind(file.Id, Guid.NewGuid())]);
        await other.SaveChangesAsync();

        Assert.Equal(3, await PersistedCountAsync(file.Id));
    }

    /// <summary>改名是一次整行写回：它不能把别人在它读与写之间加上的计数写回旧值。</summary>
    [Fact]
    public async Task ARenameFromAStaleCopy_DoesNotOverwriteACountAddedInBetween()
    {
        var file = await CreateStoredFileAsync("report.txt", "report"u8.ToArray());
        DbContext.ChangeTracker.Clear();

        using var scope = ServiceProvider.CreateScope();
        var other = scope.ServiceProvider.GetRequiredService<StorageTestDbContext>();
        await other.FileRecords.FindAsync(file.Id);

        await CreateReferenceProcessor().ProcessChangesAsync([Bind(file.Id, Guid.NewGuid())]);
        await DbContext.SaveChangesAsync();

        var renamed = await StorageServiceOver(other, scope.ServiceProvider).RenameAsync(file.Id, "renamed.txt");
        Assert.True(renamed.Succeeded, renamed.Message);
        await other.SaveChangesAsync();

        Assert.Equal(2, await PersistedCountAsync(file.Id));
        Assert.Equal("renamed.txt", (await DbContext.FileRecords.AsNoTracking().SingleAsync(f => f.Id == file.Id)).OriginalName);
    }

    /// <summary>
    /// 孤儿回收读出候选之后、删除之前，这份文件被重新持有了（一次复用命中刚刚提交）：记录与对象都要留下。
    /// </summary>
    [Fact]
    public async Task OrphanCleanup_KeepsARecordThatWasHeldAgainAfterItWasListed()
    {
        using var stream = new MemoryStream("orphan"u8.ToArray());
        var path = await Storage.UploadAsync("orphan.txt", stream, "text/plain");
        var record = new FileRecord
        {
            Id = Guid.NewGuid(),
            FileName = "orphan.txt",
            OriginalName = "orphan.txt",
            Extension = ".txt",
            Size = 6,
            Path = path,
            Provider = Storage.ProviderName,
            ContentType = "text/plain",
            ReferenceCount = 0,
            CreationTime = DateTime.UtcNow.AddHours(-(StorageOptions.Cleanup.OrphanFileRetentionHours + 1))
        };
        DbContext.FileRecords.Add(record);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        _interceptor.Arm("DELETE", RecordTable, $"UPDATE \"{RecordTable}\" SET \"ReferenceCount\" = \"ReferenceCount\" + 1");

        var deleted = await CreateCleanupService().CleanupOrphanFilesAsync();

        Assert.True(_interceptor.Fired, "the concurrent re-hold was never injected");
        Assert.Equal(0, deleted);
        Assert.Equal(1, await PersistedCountAsync(record.Id));
        Assert.True(await Storage.ExistsAsync(path));
    }

    /// <summary>
    /// MD5 复用命中一条记录、正要给它加计数时，那条记录刚被回收掉了：不能把一条已经不存在的记录交给上传者，
    /// 要按一次新上传处理。
    /// </summary>
    [Fact]
    public async Task Md5Reuse_OfARecordRemovedJustBeforeItIsHeld_FallsBackToANewUpload()
    {
        var service = CreateStorageService();
        var bytes = "same bytes"u8.ToArray();
        var first = await service.SaveFromBytesAsync("first.txt", bytes, "text/plain");
        Assert.True(first.Succeeded, first.Message);

        _interceptor.Arm("UPDATE", RecordTable, $"DELETE FROM \"{RecordTable}\"");

        var second = await service.SaveFromBytesAsync("second.txt", bytes, "text/plain");

        Assert.True(_interceptor.Fired, "the concurrent removal was never injected");
        Assert.True(second.Succeeded, second.Message);
        Assert.NotEqual(first.Data!.Id, second.Data!.Id);
        Assert.Equal(1, await PersistedCountAsync(second.Data.Id));
        Assert.Null(await PersistedCountAsync(first.Data.Id));
    }

    /// <summary>
    /// 记录与绑定在同一个工作单元里（记录还没落库）：计数随插入一起写下去，不因原子更新找不到行而丢掉。
    /// </summary>
    [Fact]
    public async Task BindingARecordThatIsNotYetSaved_CountsOnInsert()
    {
        var record = new FileRecord
        {
            Id = Guid.NewGuid(),
            FileName = "pending.txt",
            OriginalName = "pending.txt",
            Extension = ".txt",
            Provider = Storage.ProviderName,
            IsTemporary = true,
            ReferenceCount = 0
        };
        DbContext.FileRecords.Add(record);

        await CreateReferenceProcessor().ProcessChangesAsync([Bind(record.Id, Guid.NewGuid())]);
        await DbContext.SaveChangesAsync();

        Assert.Equal(1, await PersistedCountAsync(record.Id));
    }

    /// <summary>
    /// 同一个事务里上传又删掉（记录还没落库）：条件删除在库里找不到它，也不能让它在提交时被插进去、指着一个已经删掉的对象。
    /// </summary>
    [Fact]
    public async Task DeletingAnUploadThatIsNotYetSaved_LeavesNoRecordBehind()
    {
        var service = CreateStorageService();
        await using var transaction = await DbContext.Database.BeginTransactionAsync();

        var upload = await service.SaveAsync("scratch.txt", new MemoryStream("scratch"u8.ToArray()), isTemporary: true);
        Assert.True(upload.Succeeded, upload.Message);
        var deleted = await service.DeleteAsync(upload.Data!.Id);
        Assert.True(deleted.Succeeded, deleted.Message);

        await DbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        Assert.Null(await PersistedCountAsync(upload.Data.Id));
        Assert.False(await Storage.ExistsAsync(upload.Data.Path!));
    }

    /// <summary>
    /// 临时记录转正（文件库挪目录）与 +1 是同一条条件语句：两次并发的转正只加一个持有者，
    /// 否则删除时要多删一次才真的删得掉。
    /// </summary>
    [Fact]
    public async Task PromotingATemporaryRecordTwice_AddsOneHolder()
    {
        var upload = await CreateStorageService().SaveAsync("filed.txt", new MemoryStream("filed"u8.ToArray()), isTemporary: true);
        Assert.True(upload.Succeeded, upload.Message);
        DbContext.ChangeTracker.Clear();
        var files = new EFCoreRepository<StorageTestDbContext, FileRecord, Guid>(DbContext, serviceProvider: ServiceProvider);

        Assert.True(await FileReferenceCounts.TryPromoteAsync(files, upload.Data!.Id));
        Assert.False(await FileReferenceCounts.TryPromoteAsync(files, upload.Data.Id));

        var record = await DbContext.FileRecords.AsNoTracking().SingleAsync(f => f.Id == upload.Data.Id);
        Assert.False(record.IsTemporary);
        Assert.Equal(1, record.ReferenceCount);
    }

    /// <summary>
    /// 手动清理按过期的临时引用行找到一条无人持有的记录：记录、它名下的引用行一起删掉，同一份文件的两条引用行不让清理失败。
    /// </summary>
    [Fact]
    public async Task ManualTemporaryCleanup_ByExpiredReferenceRows_RemovesTheUnheldRecordOnce()
    {
        var file = await CreateStoredFileAsync("draft.txt", "draft"u8.ToArray());
        await DbContext.FileRecords.Where(f => f.Id == file.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.ReferenceCount, 0));
        foreach (var field in new[] { "Cover", "Banner" })
        {
            DbContext.FileReferences.Add(new FileReference
            {
                Id = Guid.NewGuid(),
                FileId = file.Id,
                EntityType = "Draft",
                EntityId = Guid.NewGuid(),
                FieldName = field,
                IsTemporary = true,
                CreationTime = DateTime.UtcNow.AddHours(-30)
            });
        }
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var service = new FileReferenceService(
            new EFCoreRepository<StorageTestDbContext, FileRecord, Guid>(DbContext, serviceProvider: ServiceProvider),
            new EFCoreRepository<StorageTestDbContext, FileReference, Guid>(DbContext, serviceProvider: ServiceProvider),
            ServiceProvider);
        var result = await service.CleanupTemporaryFilesAsync(TimeSpan.FromHours(24));

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(1, result.Data);
        Assert.Null(await PersistedCountAsync(file.Id));
        Assert.False(await DbContext.FileReferences.AnyAsync(r => r.FileId == file.Id));
    }

    /// <summary>
    /// 在目标语句（按动词与表名认）执行之前，在同一个连接与事务上先执行一段 SQL：模拟一次刚刚提交的并发修改。只触发一次。
    /// </summary>
    private sealed class BeforeCommandInterceptor : DbCommandInterceptor
    {
        private string? _verb;
        private string? _table;
        private string? _sql;

        public bool Fired { get; private set; }

        public void Arm(string verb, string table, string sql)
        {
            _verb = verb;
            _table = table;
            _sql = sql;
            Fired = false;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await FireAsync(command, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await FireAsync(command, cancellationToken);
            return result;
        }

        private async Task FireAsync(DbCommand command, CancellationToken cancellationToken)
        {
            if (_sql == null
                || !command.CommandText.TrimStart().StartsWith(_verb!, StringComparison.OrdinalIgnoreCase)
                || !command.CommandText.Contains($"\"{_table}\"", StringComparison.Ordinal))
            {
                return;
            }

            var sql = _sql;
            _sql = null;

            await using var injected = command.Connection!.CreateCommand();
            injected.Transaction = command.Transaction;
            injected.CommandText = sql;
            await injected.ExecuteNonQueryAsync(cancellationToken);
            Fired = true;
        }
    }
}
