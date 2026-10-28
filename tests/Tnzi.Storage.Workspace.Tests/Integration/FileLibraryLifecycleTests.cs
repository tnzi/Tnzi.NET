namespace Tnzi.Storage.Workspace.Tests.Integration;

/// <summary>
/// 归档进文件库的文件不是孤儿：把临时上传挪进（或挪出）目录，就是有人要留着它。
/// </summary>
/// <remarks>
/// <para>
/// <b>被保护的缺陷</b>：直传 <c>POST files/upload</c> 落的是临时记录（<c>IsTemporary = true</c>、计数 0），
/// 它的生命周期只由 <c>[FileField]</c> 引用决定；文件库的「先直传、再挪进目录」从头到尾没有任何引用，
/// 而归档只改 <c>FolderId</c>。开启后台清理后，保留期一过，孤儿回收把原件、缩略图、记录一起删掉，
/// 分享链接 404、版本行成了孤儿，没有任何告警。
/// </para>
/// <para>
/// 归档因此是一个持有者：临时记录转正（<c>IsTemporary = false</c>），计数在引用行之外加一个隐式持有者，
/// 与正式上传的语义一致（正式记录 = 隐式持有者 + 引用行）。
/// </para>
/// </remarks>
public class FileLibraryLifecycleTests : WorkspaceIntegrationTestBase
{
    [Fact]
    public async Task MovingATemporaryUploadIntoAFolder_PromotesItToPermanent()
    {
        var upload = await SaveTemporaryAsync("contract.txt");
        var folder = (await CreateFolderService().CreateAsync(new CreateFileFolderDto { Name = "contracts" })).Data!;

        var result = await CreateFolderService().MoveFilesToFolderAsync([upload.Id], folder.Id);

        Assert.True(result.Succeeded);
        var record = await ReloadAsync(upload.Id);
        Assert.Equal(folder.Id, record.FolderId);
        Assert.False(record.IsTemporary);
        Assert.Equal(1, record.ReferenceCount);
    }

    /// <summary>
    /// 挪到根目录同样是在文件库里整理它。没有这一条，此前已经躺在目录里的临时记录（修复之前上传的）
    /// 一旦被挪回根目录，下一轮清理就会删掉它。
    /// </summary>
    [Fact]
    public async Task MovingATemporaryUploadToTheRoot_AlsoPromotesIt()
    {
        var upload = await SaveTemporaryAsync("notes.txt");

        var result = await CreateFolderService().MoveFilesToFolderAsync([upload.Id], null);

        Assert.True(result.Succeeded);
        var record = await ReloadAsync(upload.Id);
        Assert.False(record.IsTemporary);
        Assert.Equal(1, record.ReferenceCount);
    }

    /// <summary>
    /// 转正只发生一次：正式记录再挪一次不多加持有者，否则每挪一次目录，删除时就要多删一次才真的删得掉。
    /// </summary>
    [Fact]
    public async Task MovingAPermanentRecordAgain_DoesNotAddAnotherHolder()
    {
        var upload = await SaveTemporaryAsync("plan.txt");
        var service = CreateFolderService();
        var first = (await service.CreateAsync(new CreateFileFolderDto { Name = "a" })).Data!;
        var second = (await service.CreateAsync(new CreateFileFolderDto { Name = "b" })).Data!;

        await CreateFolderService().MoveFilesToFolderAsync([upload.Id], first.Id);
        DbContext.ChangeTracker.Clear();
        await CreateFolderService().MoveFilesToFolderAsync([upload.Id], second.Id);

        var record = await ReloadAsync(upload.Id);
        Assert.Equal(second.Id, record.FolderId);
        Assert.Equal(1, record.ReferenceCount);
    }

    /// <summary>
    /// 端到端：归档过的文件过了保留期，孤儿回收不碰它；同一时刻另一份没人要的临时上传照常回收（正控制）。
    /// </summary>
    [Fact]
    public async Task AFiledUpload_SurvivesOrphanCleanupPastTheRetention()
    {
        var filed = await SaveTemporaryAsync("filed.txt");
        var abandoned = await SaveTemporaryAsync("abandoned.txt");
        var folder = (await CreateFolderService().CreateAsync(new CreateFileFolderDto { Name = "keep" })).Data!;
        await CreateFolderService().MoveFilesToFolderAsync([filed.Id], folder.Id);
        await AgeAsync(filed.Id, abandoned.Id);

        var deleted = await CreateCleanupService(StorageOptions).CleanupOrphanFilesAsync();

        Assert.Equal(1, deleted);
        Assert.NotNull(await DbContext.FileRecords.FindAsync(filed.Id));
        Assert.Null(await DbContext.FileRecords.FindAsync(abandoned.Id));
    }

    private async Task<FileRecord> SaveTemporaryAsync(string name)
    {
        // 每份内容各不相同：相同字节会被 MD5 去重合并成一条记录
        var content = Encoding.UTF8.GetBytes($"{name}:{Guid.NewGuid()}");
        var result = await CreateStorageService().SaveAsync(name, new MemoryStream(content), isTemporary: true);
        Assert.True(result.Succeeded, result.Message);
        Assert.True(result.Data!.IsTemporary);
        Assert.Equal(0, result.Data.ReferenceCount);
        DbContext.ChangeTracker.Clear();
        return result.Data;
    }

    private async Task<FileRecord> ReloadAsync(Guid id)
    {
        DbContext.ChangeTracker.Clear();
        return (await DbContext.FileRecords.FindAsync(id))!;
    }

    private async Task AgeAsync(params Guid[] ids)
    {
        DbContext.ChangeTracker.Clear();
        var cutoff = DateTime.UtcNow.AddHours(-(StorageOptions.Cleanup.OrphanFileRetentionHours + 24));
        await DbContext.FileRecords
            .Where(f => ids.Contains(f.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.CreationTime, cutoff));
        DbContext.ChangeTracker.Clear();
    }
}
