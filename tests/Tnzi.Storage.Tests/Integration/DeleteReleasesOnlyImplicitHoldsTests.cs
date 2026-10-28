namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// <c>DeleteAsync</c> / <c>DeleteManyAsync</c> 只能释放<b>不留引用行的隐式持有者</b>（正式上传本身、MD5 复用），
/// 扣不掉业务记录的引用。
/// </summary>
/// <remarks>
/// <para>
/// <c>ReferenceCount</c> = 引用行数 + 隐式持有者数。删除此前不分是谁的那一份，见数就扣：一个临时上传被两条业务记录
/// 引用（计数 2，两行引用），上传者或管理员删一次，计数变 1、提示「引用数减少」、文件还在列表里；之后任一条记录换图，
/// 解绑把计数扣到 0，文件被物理删除，另一条记录的引用行还在，它的附件从此 404 / 500。
/// </para>
/// <para>
/// 现在：还有隐式持有者就释放一个；只剩引用行时拒绝（409，什么都不改），由调用方先从那些记录里移除它；
/// 没有任何持有者时照常删除。批量删除要么整批按这条规则执行，要么一条都不动。
/// </para>
/// </remarks>
public class DeleteReleasesOnlyImplicitHoldsTests : StorageIntegrationTestBase
{
    private const string EntityType = "Doc";
    private const string FieldName = "Attachment";

    private async Task<FileRecord> SaveAsync(string name, bool isTemporary, string content = "attachment bytes")
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        var result = await CreateStorageService().SaveAsync(name, stream, isTemporary);
        Assert.True(result.Succeeded, result.Message);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return result.Data!;
    }

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

    private Task BindAsync(Guid fileId, Guid entityId) => ApplyChangeAsync(FileReferenceChangeType.Create, fileId, entityId);

    private Task UnbindAsync(Guid fileId, Guid entityId) => ApplyChangeAsync(FileReferenceChangeType.Delete, fileId, entityId);

    private async Task<Tnzi.Results.Result> DeleteAsync(Guid id)
    {
        var result = await CreateStorageService().DeleteAsync(id);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return result;
    }

    private async Task<FileRecord?> ReloadAsync(Guid id)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.FileRecords.AsNoTracking().SingleOrDefaultAsync(f => f.Id == id);
    }

    private Task<int> ReferenceRowsAsync(Guid id)
        => DbContext.FileReferences.AsNoTracking().CountAsync(r => r.FileId == id);

    [Fact]
    public async Task ATemporaryUploadHeldOnlyByRecords_IsNotDeleted_AndKeepsEveryRecordsHold()
    {
        var file = await SaveAsync("scan.txt", isTemporary: true);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await BindAsync(file.Id, first);
        await BindAsync(file.Id, second);

        var deleted = await DeleteAsync(file.Id);

        Assert.False(deleted.Succeeded);
        Assert.Equal(409, deleted.Code);
        var afterDelete = await ReloadAsync(file.Id);
        Assert.Equal(2, afterDelete!.ReferenceCount);
        Assert.Equal(2, await ReferenceRowsAsync(file.Id));

        // 第一条记录换图：第二条记录的那一份必须还在，文件不该归零。
        await UnbindAsync(file.Id, first);
        var afterUnbind = await ReloadAsync(file.Id);
        Assert.Equal(1, afterUnbind!.ReferenceCount);
        Assert.True(await Storage.ExistsAsync(afterUnbind.Path!));
    }

    [Fact]
    public async Task ATemporaryUploadBoundOnce_IsNotForceDeletedFromUnderItsRecord()
    {
        var file = await SaveAsync("avatar.txt", isTemporary: true);
        await BindAsync(file.Id, Guid.NewGuid());

        var deleted = await DeleteAsync(file.Id);

        Assert.False(deleted.Succeeded);
        Assert.Equal(409, deleted.Code);
        var record = await ReloadAsync(file.Id);
        Assert.NotNull(record);
        Assert.Equal(1, record!.ReferenceCount);
        Assert.Equal(1, await ReferenceRowsAsync(file.Id));
        Assert.True(await Storage.ExistsAsync(record.Path!));
    }

    [Fact]
    public async Task APermanentUpload_ReleasesTheUploadersOwnHold_ButNotTheRecordsOne()
    {
        // 正控制：正式上传本身是一个隐式持有者，上传者删一次正好释放它；文件仍被业务记录引用，所以还在。
        var file = await SaveAsync("contract.txt", isTemporary: false);
        await BindAsync(file.Id, Guid.NewGuid());
        Assert.Equal(2, (await ReloadAsync(file.Id))!.ReferenceCount);

        var released = await DeleteAsync(file.Id);
        Assert.True(released.Succeeded, released.Message);
        Assert.Equal(1, (await ReloadAsync(file.Id))!.ReferenceCount);

        // 隐式持有者已经释放完：再删一次扣的就是业务记录那一份了，拒绝。
        var again = await DeleteAsync(file.Id);
        Assert.False(again.Succeeded);
        Assert.Equal(409, again.Code);
        var record = await ReloadAsync(file.Id);
        Assert.Equal(1, record!.ReferenceCount);
        Assert.True(await Storage.ExistsAsync(record.Path!));
    }

    [Fact]
    public async Task AnUnreferencedUpload_IsStillDeleted()
    {
        // 正控制：没有任何持有者的文件照常删除，记录与对象一起。
        var file = await SaveAsync("draft.txt", isTemporary: true);

        var deleted = await DeleteAsync(file.Id);

        Assert.True(deleted.Succeeded, deleted.Message);
        Assert.Null(await ReloadAsync(file.Id));
        Assert.False(await Storage.ExistsAsync(file.Path!));
    }

    [Fact]
    public async Task BatchDelete_WithAFileStillInUse_ChangesNothing()
    {
        var inUse = await SaveAsync("in-use.txt", isTemporary: true, content: "in use");
        await BindAsync(inUse.Id, Guid.NewGuid());
        var free = await SaveAsync("free.txt", isTemporary: true, content: "free");

        var result = await CreateStorageService().DeleteManyAsync([inUse.Id, free.Id]);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Code);
        Assert.Contains("in-use.txt", result.Message);
        Assert.Equal(1, (await ReloadAsync(inUse.Id))!.ReferenceCount);
        Assert.NotNull(await ReloadAsync(free.Id));
        Assert.True(await Storage.ExistsAsync(free.Path!));
    }

    [Fact]
    public async Task BatchDelete_WithNothingInUse_StillWorks()
    {
        // 正控制：一批里既有隐式持有者可释放的，也有无人持有的。
        var permanent = await SaveAsync("kept.txt", isTemporary: false, content: "kept");
        await BindAsync(permanent.Id, Guid.NewGuid());
        var free = await SaveAsync("free.txt", isTemporary: true, content: "free");

        var result = await CreateStorageService().DeleteManyAsync([permanent.Id, free.Id]);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(1, (await ReloadAsync(permanent.Id))!.ReferenceCount);
        Assert.Null(await ReloadAsync(free.Id));
    }
}
