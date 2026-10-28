namespace Tnzi.Storage.Workspace.Tests.Integration;

/// <summary>
/// 回收站：删除进所有者的回收站（目录连同其中的一切），列表 / 搜索里随之消失；还原回原处，原处不在了回根目录；
/// 永久删除放掉文件空间的持有，没人引用的连字节一起删；过期自动清除。
/// </summary>
public class RecycleBinTests : FileSpaceTestBase
{
    private async Task TrashAsync(IReadOnlyCollection<Guid>? files = null, IReadOnlyCollection<Guid>? folders = null)
    {
        DbContext.ChangeTracker.Clear();
        var result = await CreateRecycleBinService().MoveToRecycleBinAsync(files, folders);
        Assert.True(result.Succeeded, result.Message);
        DbContext.ChangeTracker.Clear();
    }

    private async Task<IList<RecycleBinEntryDto>> BinAsync()
    {
        DbContext.ChangeTracker.Clear();
        var result = await CreateRecycleBinService().GetEntriesAsync(new RecycleBinQueryDto());
        Assert.True(result.Succeeded, result.Message);
        return result.Data!.Items;
    }

    private async Task<RecycleBinRestoreResult> RestoreAsync(params Guid[] entryIds)
    {
        DbContext.ChangeTracker.Clear();
        var result = await CreateRecycleBinService().RestoreAsync(entryIds);
        Assert.True(result.Succeeded, result.Message);
        DbContext.ChangeTracker.Clear();
        return result.Data!;
    }

    private async Task PurgeAsync(params Guid[] entryIds)
    {
        DbContext.ChangeTracker.Clear();
        var result = await CreateRecycleBinService().PurgeAsync(entryIds);
        Assert.True(result.Succeeded, result.Message);
        DbContext.ChangeTracker.Clear();
    }

    [Fact]
    public async Task ADeletedFile_LeavesTheListingAndSearch_AndShowsUpInTheBin()
    {
        var docs = await CreateFolderAsync("Docs");
        var file = await UploadAsync("plan.txt", "plan", docs.Id);

        await TrashAsync(files: [file.Id]);

        Assert.Empty(Names(await ListAsync(docs.Id)));
        Assert.Empty(await SearchAsync(keyword: "plan"));
        var entry = Assert.Single(await BinAsync());
        Assert.Equal(RecycleBinItemType.File, entry.ItemType);
        Assert.Equal("plan.txt", entry.Name);
        Assert.Equal("/Docs", entry.OriginalLocation);
        Assert.Equal(4, entry.Size);
        Assert.NotNull(entry.PurgeAt);
    }

    /// <summary>回收站里的文件在清除之前一直计入配额：字节还在，回收站本来就是为了能拿回来。</summary>
    [Fact]
    public async Task FilesInTheBin_StillCountTowardsTheQuota()
    {
        var file = await UploadAsync("big.txt", "1234567890");

        await TrashAsync(files: [file.Id]);

        Assert.Equal(10, await CreateQuotaService().GetUsedBytesAsync(Me));
    }

    [Fact]
    public async Task RestoringAFile_PutsItBackInItsFolder()
    {
        var docs = await CreateFolderAsync("Docs");
        var file = await UploadAsync("plan.txt", "plan", docs.Id);
        await TrashAsync(files: [file.Id]);

        var result = await RestoreAsync((await BinAsync()).Single().Id);

        Assert.Equal(1, result.Restored);
        Assert.Empty(result.RestoredToRoot);
        Assert.Equal(["plan.txt"], Names(await ListAsync(docs.Id)));
        Assert.Empty(await BinAsync());
    }

    [Fact]
    public async Task RestoringAFileWhoseFolderIsGone_PutsItAtTheRoot()
    {
        var docs = await CreateFolderAsync("Docs");
        var file = await UploadAsync("plan.txt", "plan", docs.Id);
        await TrashAsync(files: [file.Id]);
        await TrashAsync(folders: [docs.Id]);

        var fileEntry = (await BinAsync()).Single(e => e.ItemType == RecycleBinItemType.File);
        var result = await RestoreAsync(fileEntry.Id);

        Assert.Equal([fileEntry.Id], result.RestoredToRoot);
        Assert.Equal(["plan.txt"], Names(await ListAsync()));
    }

    /// <summary>删一个非空目录：它、它的子目录、里面的文件一起进回收站，还原时一起回来。</summary>
    [Fact]
    public async Task ANonEmptyFolder_GoesToTheBinWithEverythingInIt_AndComesBackTheSameWay()
    {
        var docs = await CreateFolderAsync("Docs");
        var sub = await CreateFolderAsync("Sub", docs.Id);
        await UploadAsync("top.txt", "top", docs.Id);
        await UploadAsync("deep.txt", "deep", sub.Id);

        await TrashAsync(folders: [docs.Id]);

        Assert.Empty(Names(await ListAsync()));
        Assert.Empty(await SearchAsync());
        var entry = Assert.Single(await BinAsync());
        Assert.Equal(RecycleBinItemType.Folder, entry.ItemType);
        Assert.Equal(2, entry.FileCount);
        Assert.Equal(7, entry.Size);

        await RestoreAsync(entry.Id);

        Assert.Equal(["Docs"], Names(await ListAsync()));
        Assert.Equal(["Sub", "top.txt"], Names(await ListAsync(docs.Id)));
        Assert.Equal(["deep.txt"], Names(await ListAsync(sub.Id)));
        Assert.Equal(2, (await SearchAsync()).Count);
    }

    /// <summary>目录删掉之后又建了一个同名的：还原的那个改名加序号，两个都是用户的东西。</summary>
    [Fact]
    public async Task RestoringAFolderWhoseNameIsTakenAgain_RenamesIt()
    {
        var docs = await CreateFolderAsync("Docs");
        await TrashAsync(folders: [docs.Id]);
        await CreateFolderAsync("Docs");

        var entry = Assert.Single(await BinAsync());
        var result = await RestoreAsync(entry.Id);

        Assert.Equal("Docs (2)", result.Renamed[entry.Id]);
        Assert.Equal(["Docs", "Docs (2)"], Names(await ListAsync()));
    }

    /// <summary>一个子目录先单独删了、后来上级也删了：还原上级时它留在回收站里（它是单独删的）。</summary>
    [Fact]
    public async Task ASubfolderDeletedOnItsOwn_StaysInTheBin_WhenItsParentIsRestored()
    {
        var docs = await CreateFolderAsync("Docs");
        var old = await CreateFolderAsync("Old", docs.Id);
        await TrashAsync(folders: [old.Id]);
        await TrashAsync(folders: [docs.Id]);

        var parentEntry = (await BinAsync()).Single(e => e.ItemId == docs.Id);
        await RestoreAsync(parentEntry.Id);

        Assert.Empty(Names(await ListAsync(docs.Id)));
        Assert.Contains(await BinAsync(), e => e.ItemId == old.Id);
    }

    [Fact]
    public async Task PurgingAFile_DeletesItsRecordAndItsBytes()
    {
        var file = await UploadAsync("gone.txt", "bytes");
        await TrashAsync(files: [file.Id]);

        await PurgeAsync((await BinAsync()).Single().Id);

        Assert.Null(await ReloadFileAsync(file.Id));
        Assert.False(await Storage.ExistsAsync(file.Path!));
        Assert.Empty(await BinAsync());
        Assert.Equal(0, await CreateQuotaService().GetUsedBytesAsync(Me));
    }

    /// <summary>
    /// ★ 还挂在一条消息上的文件：永久删除让它离开文件空间（不再出现在任何目录里），记录与字节为那条消息留着。
    /// </summary>
    [Fact]
    public async Task PurgingAFileStillAttachedToAMessage_KeepsItForTheMessage_ButNotInTheFileSpace()
    {
        var docs = await CreateFolderAsync("Docs");
        var file = await UploadAsync("shared.txt", "shared", docs.Id);
        await BindToAMessageAsync(file.Id);
        await TrashAsync(files: [file.Id]);

        await PurgeAsync((await BinAsync()).Single().Id);

        var kept = await ReloadFileAsync(file.Id);
        Assert.NotNull(kept);
        Assert.Null(kept!.FolderId);
        Assert.Equal(1, kept.ReferenceCount);
        Assert.True(await Storage.ExistsAsync(file.Path!));
        Assert.Empty(Names(await ListAsync(docs.Id)));
        Assert.Equal(["Docs"], Names(await ListAsync()));
        Assert.Empty(await SearchAsync());
    }

    [Fact]
    public async Task PurgingAFolder_DeletesTheFilesThatWereInIt()
    {
        var docs = await CreateFolderAsync("Docs");
        var file = await UploadAsync("inside.txt", "inside", docs.Id);
        await TrashAsync(folders: [docs.Id]);

        await PurgeAsync((await BinAsync()).Single().Id);

        Assert.Null(await ReloadFileAsync(file.Id));
        Assert.Empty(await BinAsync());
        Assert.Empty(Names(await ListAsync()));
    }

    [Fact]
    public async Task EmptyingTheBin_PurgesEverythingInIt()
    {
        var a = await UploadAsync("a.txt", "a");
        var b = await UploadAsync("b.txt", "b");
        await TrashAsync(files: [a.Id, b.Id]);

        var emptied = await CreateRecycleBinService().EmptyAsync();

        Assert.Equal(2, emptied.Data);
        Assert.Null(await ReloadFileAsync(a.Id));
        Assert.Null(await ReloadFileAsync(b.Id));
    }

    [Fact]
    public async Task ExpiredEntriesArePurgedAutomatically_NewerOnesAreKept()
    {
        var old = await UploadAsync("old.txt", "old");
        var recent = await UploadAsync("recent.txt", "recent");
        await TrashAsync(files: [old.Id, recent.Id]);
        await DbContext.RecycleBinEntries
            .Where(e => e.ItemId == old.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.CreationTime, DateTime.UtcNow.AddDays(-31)));

        var purged = await CreateRecycleBinService().PurgeExpiredAsync(100);

        Assert.Equal(1, purged);
        Assert.Null(await ReloadFileAsync(old.Id));
        Assert.NotNull(await ReloadFileAsync(recent.Id));
    }

    [Fact]
    public async Task ARetentionOfZero_NeverPurgesAutomatically()
    {
        var file = await UploadAsync("keep.txt", "keep");
        await TrashAsync(files: [file.Id]);
        await DbContext.RecycleBinEntries.ExecuteUpdateAsync(s => s.SetProperty(e => e.CreationTime, DateTime.UtcNow.AddYears(-5)));
        StorageOptions.RecycleBin.RetentionDays = 0;

        Assert.Equal(0, await CreateRecycleBinService().PurgeExpiredAsync(100));
        Assert.NotNull(await ReloadFileAsync(file.Id));
        Assert.Null(Assert.Single(await BinAsync()).PurgeAt);
    }

    /// <summary>删掉的东西进<b>所有者</b>的回收站：别人（这里是替他删的人）看不到也还原不了。</summary>
    [Fact]
    public async Task SomeoneElsesBinEntries_AreNotMine()
    {
        var theirs = await SaveForSomeoneElseAsync("theirs.txt", "x");
        await TrashAsync(files: [theirs.Id]);

        Assert.Empty(await BinAsync());
        var entryId = (await DbContext.RecycleBinEntries.SingleAsync()).Id;
        Assert.Equal(SomeoneElse, (await DbContext.RecycleBinEntries.SingleAsync()).OwnerId);
        Assert.Equal(404, (await CreateRecycleBinService().RestoreAsync([entryId])).Code);
        Assert.Equal(404, (await CreateRecycleBinService().PurgeAsync([entryId])).Code);
    }

    [Fact]
    public async Task AFileThatIsNotInTheFileSpace_CannotBeDeletedThroughIt()
    {
        var attachment = await SeedTemporaryAttachmentAsync("attachment.txt");
        var forwarded = await SeedFileHeldOnlyByAMessageAsync("forwarded.txt");

        Assert.Equal(404, (await CreateRecycleBinService().MoveToRecycleBinAsync([forwarded.Id], null)).Code);
        var result = await CreateRecycleBinService().MoveToRecycleBinAsync([attachment.Id], null);

        Assert.Equal(404, result.Code);
        Assert.Empty(await BinAsync());
    }

    [Fact]
    public async Task OneUnknownIdRefusesTheWholeBatch()
    {
        var file = await UploadAsync("a.txt", "a");

        var result = await CreateRecycleBinService().MoveToRecycleBinAsync([file.Id, Guid.NewGuid()], null);

        Assert.Equal(404, result.Code);
        Assert.Empty(await BinAsync());
    }

    /// <summary>删掉的文件，发出去的分享链接随之失效；还原之后恢复（链接没有被撤销）。</summary>
    [Fact]
    public async Task AShareLinkStopsWorkingWhileItsFileIsInTheBin()
    {
        var file = await UploadAsync("shared.txt", "shared");
        var share = (await CreateShareService().CreateShareAsync(file.Id)).Data!;
        await TrashAsync(files: [file.Id]);

        Assert.False((await CreateShareService().ValidateShareAccessAsync(share.ShareToken)).Data);
        Assert.Equal(404, (await CreateShareService().GetSharePreviewAsync(share.ShareToken)).Code);

        await RestoreAsync((await BinAsync()).Single().Id);

        Assert.True((await CreateShareService().ValidateShareAccessAsync(share.ShareToken)).Data);
    }

    [Fact]
    public async Task AFileInTheBin_CannotBeMovedBackOutOfIt()
    {
        var docs = await CreateFolderAsync("Docs");
        var file = await UploadAsync("a.txt", "a");
        await TrashAsync(files: [file.Id]);

        var moved = await CreateFolderService().MoveFilesToFolderAsync([file.Id], docs.Id);

        Assert.Equal(404, moved.Code);
    }

    /// <summary>界面上看起来是空的目录（里面的文件单独删进了回收站）可以照常删。</summary>
    [Fact]
    public async Task AFolderWhoseFilesAreAllInTheBin_CountsAsEmpty()
    {
        var docs = await CreateFolderAsync("Docs");
        var file = await UploadAsync("a.txt", "a", docs.Id);
        await TrashAsync(files: [file.Id]);

        var deleted = await CreateFolderService().DeleteAsync(docs.Id);

        Assert.True(deleted.Succeeded, deleted.Message);
    }

    /// <summary>父模块那条路径直接删掉了回收站里的文件：条目随记录一起收掉，回收站里不留指着空的一行。</summary>
    [Fact]
    public async Task AFileDeletedOutsideTheBin_TakesItsEntryWithIt()
    {
        var file = await UploadAsync("a.txt", "a");
        await TrashAsync(files: [file.Id]);
        DbContext.ChangeTracker.Clear();
        Assert.True((await CreateStorageService().DeleteAsync(file.Id)).Succeeded);

        var cleaner = new DeletedFileWorkspaceCleaner(
            Repo<FileRecord>(), Repo<FileVersion>(), Repo<FileShare>(), Repo<RecycleBinEntry>(), Storage,
            NullLogger<DeletedFileWorkspaceCleaner>.Instance);
        await cleaner.PurgeAsync(file.Id);

        Assert.Empty(await BinAsync());
    }
}
