namespace Tnzi.Storage.Tests.Integration;

/// <summary>
/// 以系统身份放掉文件的全部隐式持有者（文件空间的永久删除用）：没人再持有就连记录带对象删掉，
/// 还被业务记录引用的留下，计数回到引用行数。
/// </summary>
public class FileHoldReleaseTests : StorageIntegrationTestBase
{
    /// <summary>像一条业务记录那样经引用处理器绑定这个文件（正式引用行 + 计数 +1）。</summary>
    private async Task BindToAMessageAsync(Guid fileId)
    {
        await CreateReferenceProcessor().ProcessChangesAsync([new FileReferenceChange
        {
            ChangeType = FileReferenceChangeType.Create,
            EntityType = "ChatMessage",
            EntityId = Guid.NewGuid().ToString(),
            FieldName = "FileId",
            FileId = fileId
        }]);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private async Task<FileRecord?> ReloadAsync(Guid id)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.FileRecords.SingleOrDefaultAsync(f => f.Id == id);
    }

    [Fact]
    public async Task AnUnreferencedFile_IsDeleted_WithItsObject()
    {
        var file = await CreateStoredFileAsync("summary.md", "# summary"u8.ToArray());

        var outcome = await CreateHoldService().ReleaseImplicitHoldsAsync(file.Id);

        Assert.Equal(FileReleaseOutcome.Deleted, outcome);
        Assert.Null(await ReloadAsync(file.Id));
        Assert.False(await Storage.ExistsAsync(file.Path!));
    }

    /// <summary>
    /// ★ 同一个人把同一份字节正式上传了两次，MD5 去重把两次记在同一条记录上（计数 2）：只放一份，
    /// 它会带着另一份回到文件空间的根目录 —— 用户永久删掉的是这个文件，两份一起放。
    /// </summary>
    [Fact]
    public async Task EveryImplicitHolderIsReleased_NotJustOne()
    {
        var file = await CreateStoredFileAsync("twice.txt", [1, 2, 3]);
        await DbContext.FileRecords.Where(f => f.Id == file.Id).ExecuteUpdateAsync(s => s.SetProperty(f => f.ReferenceCount, 2));

        Assert.Equal(FileReleaseOutcome.Deleted, await CreateHoldService().ReleaseImplicitHoldsAsync(file.Id));
        Assert.Null(await ReloadAsync(file.Id));
    }

    /// <summary>还挂在一条消息上的文件：那一份属于消息，不放；文件与字节都留下，只是不再有隐式持有者。</summary>
    [Fact]
    public async Task AReferencedFile_StaysForItsReferences()
    {
        var file = await CreateStoredFileAsync("attachment.png", [9, 9, 9]);
        await BindToAMessageAsync(file.Id);
        Assert.Equal(2, (await ReloadAsync(file.Id))!.ReferenceCount);

        var outcome = await CreateHoldService().ReleaseImplicitHoldsAsync(file.Id);

        Assert.Equal(FileReleaseOutcome.StillReferenced, outcome);
        var reloaded = await ReloadAsync(file.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(1, reloaded!.ReferenceCount);
        Assert.True(await Storage.ExistsAsync(file.Path!));
    }

    [Fact]
    public async Task AMissingRecord_IsReportedAsNotFound()
    {
        Assert.Equal(FileReleaseOutcome.NotFound, await CreateHoldService().ReleaseImplicitHoldsAsync(Guid.NewGuid()));
    }
}
