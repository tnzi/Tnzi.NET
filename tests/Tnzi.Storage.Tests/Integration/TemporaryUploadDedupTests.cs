using Tnzi.EFCore;

namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// 临时上传与 MD5 去重的交界：临时上传不参与去重，每次都落一条自己的记录。
/// </summary>
/// <remarks>
/// <para>
/// 临时上传复用既有记录时曾照样 <c>ReferenceCount + 1</c> 且不留引用行；上传者随后绑定，绑定路径只认识引用行，
/// 再 <c>+1</c> 一次。头一个 +1 谁也释放不了：所有真实持有者都放手之后记录仍停在 1，孤儿回收永远收不走它。
/// </para>
/// <para>
/// 两个方向都要钉住：泄漏（所有人放手之后必须能被回收；临时记录的计数必须等于它的引用行数），
/// 以及「只去掉那个 +1」这种看似更省事的修法会打开的窗口（原持有者删掉自己那份时，临时上传者的文件不能跟着消失）。
/// </para>
/// </remarks>
public class TemporaryUploadDedupTests : StorageIntegrationTestBase
{
    private const string EntityType = "Doc";
    private const string FieldName = "Attachment";

    private static readonly byte[] SharedBytes = "the same bytes, uploaded twice"u8.ToArray();

    private FileReferenceService CreateReferenceService()
        => new(
            new EFCoreRepository<StorageTestDbContext, FileRecord, Guid>(DbContext, serviceProvider: ServiceProvider),
            new EFCoreRepository<StorageTestDbContext, FileReference, Guid>(DbContext, serviceProvider: ServiceProvider),
            ServiceProvider);

    private async Task<FileRecord> SaveAsync(string name, bool isTemporary)
    {
        using var stream = new MemoryStream(SharedBytes);
        var result = await CreateStorageService().SaveAsync(name, stream, isTemporary);
        Assert.True(result.Succeeded, result.Message);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return result.Data!;
    }

    /// <summary>实体保存时 <c>[FileField]</c> 追踪走的那条路：建一行正式引用、计数 +1。</summary>
    private async Task BindAsync(Guid fileId, Guid entityId)
        => await ApplyChangeAsync(FileReferenceChangeType.Create, fileId, entityId);

    /// <summary>实体换掉或清空字段：删掉那行引用、计数 -1。</summary>
    private async Task UnbindAsync(Guid fileId, Guid entityId)
        => await ApplyChangeAsync(FileReferenceChangeType.Delete, fileId, entityId);

    private async Task ApplyChangeAsync(FileReferenceChangeType type, Guid fileId, Guid entityId)
    {
        await CreateReferenceProcessor().ProcessChangesAsync([new FileReferenceChange
        {
            ChangeType = type,
            EntityType = EntityType,
            EntityId = entityId.ToString(),
            FieldName = FieldName,
            FileId = fileId
        }]);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private async Task<FileRecord?> ReloadAsync(Guid id)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.FileRecords.AsNoTracking().SingleOrDefaultAsync(f => f.Id == id);
    }

    [Fact]
    public async Task TemporaryUpload_OfBytesAlreadyStored_GetsItsOwnRecord_AndLeavesTheExistingCountAlone()
    {
        var permanent = await SaveAsync("original.txt", isTemporary: false);

        var temporary = await SaveAsync("draft.txt", isTemporary: true);

        Assert.NotEqual(permanent.Id, temporary.Id);
        Assert.True(temporary.IsTemporary);
        Assert.Equal(0, temporary.ReferenceCount);
        Assert.Equal("draft.txt", temporary.OriginalName);
        Assert.Equal(1, (await ReloadAsync(permanent.Id))!.ReferenceCount);
    }

    [Fact]
    public async Task SharedBytes_OnceEveryHolderLetsGo_AreReclaimedByOrphanCleanup()
    {
        // 一个正式上传者，一个临时上传者（绑定后又解绑）。两人都放手之后，这份字节必须能被回收：
        // 此前临时复用多出来的那个 +1 让记录永远停在 1。
        var permanent = await SaveAsync("original.txt", isTemporary: false);
        var temporary = await SaveAsync("draft.txt", isTemporary: true);
        var entityId = Guid.NewGuid();

        await BindAsync(temporary.Id, entityId);
        await UnbindAsync(temporary.Id, entityId);

        var deleted = await CreateStorageService().DeleteAsync(permanent.Id);
        Assert.True(deleted.Succeeded, deleted.Message);
        await DbContext.SaveChangesAsync();

        // 过了保留期。
        await DbContext.FileRecords
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.CreationTime, DateTime.UtcNow.AddHours(-200)));
        DbContext.ChangeTracker.Clear();

        await CreateCleanupService().CleanupOrphanFilesAsync();

        var remaining = await DbContext.FileRecords.AsNoTracking()
            .Where(f => f.Md5Hash == permanent.Md5Hash)
            .Select(f => new { f.Id, f.ReferenceCount })
            .ToListAsync();
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task DuplicateTemporaryUploads_EachKeepACountEqualToTheirReferenceRows()
    {
        // 「临时记录的计数完全由引用行决定」是同步 / 校验工具对临时记录的前提。
        var first = await SaveAsync("a.txt", isTemporary: true);
        var second = await SaveAsync("b.txt", isTemporary: true);
        Assert.NotEqual(first.Id, second.Id);

        await BindAsync(second.Id, Guid.NewGuid());

        var references = CreateReferenceService();
        var firstValid = await references.ValidateReferenceCountAsync(first.Id);
        var secondValid = await references.ValidateReferenceCountAsync(second.Id);
        Assert.True(firstValid.Data, "an unbound temporary upload must count zero holders");
        Assert.True(secondValid.Data, "a bound temporary upload must count exactly its reference rows");
        Assert.Equal(1, (await ReloadAsync(second.Id))!.ReferenceCount);
    }

    [Fact]
    public async Task TemporaryUpload_SurvivesTheOriginalHolderDeletingTheirCopy()
    {
        // 「保留复用、只去掉 +1」会在这里失败：原持有者一删，计数归零当场物理删除，
        // 临时上传者随后绑定到一条已经不存在的记录上。
        var permanent = await SaveAsync("original.txt", isTemporary: false);
        var temporary = await SaveAsync("draft.txt", isTemporary: true);

        var deleted = await CreateStorageService().DeleteAsync(permanent.Id);
        Assert.True(deleted.Succeeded, deleted.Message);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await BindAsync(temporary.Id, Guid.NewGuid());

        var bound = await ReloadAsync(temporary.Id);
        Assert.NotNull(bound);
        Assert.Equal(1, bound!.ReferenceCount);
        Assert.True(await Storage.ExistsAsync(bound.Path!));

        var content = await CreateStorageService().GetAsync(temporary.Id);
        Assert.True(content.Succeeded, content.Message);
        await using var stream = content.Data!;
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        Assert.Equal(SharedBytes, copy.ToArray());
    }

    [Fact]
    public async Task PermanentUpload_StillReusesTheExistingRecord()
    {
        // 对照组：正式上传的去重语义不变（一条记录、每次 +1）。
        var first = await SaveAsync("a.txt", isTemporary: false);
        var second = await SaveAsync("b.txt", isTemporary: false);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(2, (await ReloadAsync(first.Id))!.ReferenceCount);
    }
}
