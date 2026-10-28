using Tnzi.EFCore;
using Tnzi.EventBus;
using Tnzi.Storage.Events;

namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// 解绑让计数归零的文件：记录与对象一起走，而不是只删对象、把记录留成一条指向空处的「幽灵」。
/// </summary>
/// <remarks>
/// <para>
/// 此前 <c>PublishDeleteEventsAsync</c> 只发一个删对象的事件，记录原样留着（处理器只删字节，类注释写着「数据库记录由服务层处理」，
/// 而这条路径上没有任何服务层代码删它；唯一会收它的孤儿回收默认关闭）。于是管理端列表、统计、用户用量照计这条记录，
/// 完整性校验报 Missing，下载在本地 404、在对象存储上 500；任何实体再次写入这个 id 时计数 0→1，
/// 读权限探针因记录存在而放行，实体指向不存在的字节。
/// </para>
/// <para>
/// 现在在同一个事务里用条件删除收掉记录（计数仍 &lt;= 0、没有正式引用行、不是归档进目录的存量临时记录），
/// 删掉了才请求删对象；删不掉（期间又被绑定、或计数不可信）就什么都不发。
/// </para>
/// </remarks>
public class UnbindReclaimsRecordTests : StorageIntegrationTestBase
{
    private const string EntityType = "Profile";
    private const string FieldName = "Avatar";

    private readonly List<object> _published = [];

    private IEventBus RecordingBus()
    {
        var bus = new Mock<IEventBus>();
        bus.Setup(b => b.PublishAsync(It.IsAny<FileDeleteRequestedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<FileDeleteRequestedEvent, CancellationToken>((e, _) => _published.Add(e))
            .Returns(Task.CompletedTask);
        bus.Setup(b => b.PublishAsync(It.IsAny<FileRecordDeletedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<FileRecordDeletedEvent, CancellationToken>((e, _) => _published.Add(e))
            .Returns(Task.CompletedTask);
        return bus.Object;
    }

    private FileReferenceProcessor Processor(IEventBus? bus, StorageOptions? options = null)
        => new(
            new EFCoreRepository<StorageTestDbContext, FileReference, Guid>(DbContext, serviceProvider: ServiceProvider),
            new EFCoreRepository<StorageTestDbContext, FileRecord, Guid>(DbContext, serviceProvider: ServiceProvider),
            NullLogger<FileReferenceProcessor>.Instance,
            new StaticOptionsMonitor<StorageOptions>(options ?? StorageOptions),
            bus);

    private static FileReferenceChange Change(FileReferenceChangeType type, Guid fileId, Guid entityId) => new()
    {
        ChangeType = type,
        EntityType = EntityType,
        EntityId = entityId.ToString(),
        FieldName = FieldName,
        FileId = fileId
    };

    private async Task<FileRecord> UploadTemporaryAsync(string name)
    {
        var result = await CreateStorageService().SaveAsync(name, new MemoryStream("avatar bytes"u8.ToArray()), isTemporary: true);
        Assert.True(result.Succeeded, result.Message);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return result.Data!;
    }

    private async Task BindAsync(Guid fileId, Guid entityId)
    {
        await Processor(bus: null).ProcessChangesAsync([Change(FileReferenceChangeType.Create, fileId, entityId)]);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    /// <summary>与 <c>TnziDbContextHelper</c> 同一个顺序：处理变更、flush、再发删除。</summary>
    private async Task UnbindAsync(FileReferenceProcessor processor, Guid fileId, Guid entityId)
    {
        IReadOnlyList<FileReferenceChange> changes = [Change(FileReferenceChangeType.Delete, fileId, entityId)];
        await processor.ProcessChangesAsync(changes);
        await DbContext.SaveChangesAsync();
        await processor.PublishDeleteEventsAsync(changes);
    }

    private async Task<FileRecord?> ReloadAsync(Guid id)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.FileRecords.AsNoTracking().SingleOrDefaultAsync(f => f.Id == id);
    }

    [Fact]
    public async Task TheLastUnbind_RemovesTheRecord_AndRequestsTheObjectsDeletion()
    {
        var file = await UploadTemporaryAsync("old-avatar.txt");
        var profile = Guid.NewGuid();
        await BindAsync(file.Id, profile);

        await UnbindAsync(Processor(RecordingBus()), file.Id, profile);

        Assert.Null(await ReloadAsync(file.Id));
        var objectDeletion = Assert.Single(_published.OfType<FileDeleteRequestedEvent>());
        Assert.Equal(file.Id, objectDeletion.FileId);
        Assert.Equal(file.Path, objectDeletion.FilePath);
        var recordDeletion = Assert.Single(_published.OfType<FileRecordDeletedEvent>());
        Assert.Equal(file.Id, recordDeletion.FileId);
    }

    [Fact]
    public async Task ARecordReboundBeforeTheDeleteStep_IsKept_AndNothingIsDeleted()
    {
        // 解绑的计数更新已经 flush，删除这一步之前另一条记录绑上了它：条件删除读到的是 1，什么都不做。
        var file = await UploadTemporaryAsync("shared.txt");
        var first = Guid.NewGuid();
        await BindAsync(file.Id, first);

        var processor = Processor(RecordingBus());
        IReadOnlyList<FileReferenceChange> unbind = [Change(FileReferenceChangeType.Delete, file.Id, first)];
        await processor.ProcessChangesAsync(unbind);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        await BindAsync(file.Id, Guid.NewGuid());

        await processor.PublishDeleteEventsAsync(unbind);

        var record = await ReloadAsync(file.Id);
        Assert.NotNull(record);
        Assert.Equal(1, record!.ReferenceCount);
        Assert.Empty(_published);
        Assert.True(await Storage.ExistsAsync(record.Path!));
    }

    [Fact]
    public async Task ACountThatDisagreesWithTheReferenceRows_DeletesNothing()
    {
        // 计数说 0，可还有一行正式引用（并发下丢过一次递增）：不可信的计数不能成为删除的依据。
        var file = await UploadTemporaryAsync("drifted.txt");
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await BindAsync(file.Id, first);
        await BindAsync(file.Id, second);
        await DbContext.FileRecords.Where(f => f.Id == file.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.ReferenceCount, 1));
        DbContext.ChangeTracker.Clear();

        await UnbindAsync(Processor(RecordingBus()), file.Id, first);

        Assert.NotNull(await ReloadAsync(file.Id));
        Assert.Empty(_published);
    }

    [Fact]
    public async Task ATemporaryRecordFiledInAFolder_IsKept()
    {
        // 文件库里归档的存量临时记录（挪进目录只写了 FolderId 的那段时期）不是孤儿，孤儿回收也跳过它们。
        var file = await UploadTemporaryAsync("library.txt");
        await DbContext.FileRecords.Where(f => f.Id == file.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.FolderId, Guid.NewGuid()));
        DbContext.ChangeTracker.Clear();
        var profile = Guid.NewGuid();
        await BindAsync(file.Id, profile);

        await UnbindAsync(Processor(RecordingBus()), file.Id, profile);

        Assert.NotNull(await ReloadAsync(file.Id));
        Assert.Empty(_published);
    }

    [Fact]
    public async Task WithReferenceTrackingTurnedOff_NothingIsDeleted()
    {
        // 追踪关掉时框架不管文件的生死：字段清空不等于文件该删（计数根本没被维护）。
        var file = await UploadTemporaryAsync("untracked.txt");
        var options = new StorageOptions
        {
            MaxFileSize = StorageOptions.MaxFileSize,
            AllowedExtensions = StorageOptions.AllowedExtensions,
            AutoGenerateThumbnail = false,
            EnableFileReference = false
        };

        await UnbindAsync(Processor(RecordingBus(), options), file.Id, Guid.NewGuid());

        Assert.NotNull(await ReloadAsync(file.Id));
        Assert.Empty(_published);
    }

    [Fact]
    public async Task WithoutAnEventBus_TheRecordStaysForOrphanCleanup()
    {
        // 没有人会去删对象时，记录也不能先走：那会留下一个谁也找不到的对象。孤儿回收会连记录带对象一起收。
        var file = await UploadTemporaryAsync("no-bus.txt");
        var profile = Guid.NewGuid();
        await BindAsync(file.Id, profile);

        await UnbindAsync(Processor(bus: null), file.Id, profile);

        var record = await ReloadAsync(file.Id);
        Assert.NotNull(record);
        Assert.Equal(0, record!.ReferenceCount);
    }
}
