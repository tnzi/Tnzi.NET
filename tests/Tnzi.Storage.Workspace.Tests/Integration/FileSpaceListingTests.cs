namespace Tnzi.Storage.Workspace.Tests.Integration;

/// <summary>
/// 当前用户浏览与搜索自己的文件空间：先目录后文件一起分页、三种排序、只看得到自己的东西、
/// 根目录不混进头像与附件这类「名下但不是放进来的」文件。
/// </summary>
public class FileSpaceListingTests : FileSpaceTestBase
{
    [Fact]
    public async Task AFolder_ListsFoldersFirst_ThenFiles_PagedAcrossTheBoundary()
    {
        var docs = await CreateFolderAsync("Docs");
        foreach (var name in new[] { "c", "a", "b" })
            await CreateFolderAsync(name, docs.Id);
        foreach (var name in new[] { "z.txt", "x.txt", "y.txt" })
            await UploadAsync(name, name, docs.Id);

        var first = await ListAsync(docs.Id, pageSize: 4);
        var second = await ListAsync(docs.Id, pageSize: 4, pageIndex: 2);

        Assert.Equal(["a", "b", "c", "x.txt"], Names(first));
        Assert.Equal(["y.txt", "z.txt"], Names(second));
        Assert.Equal(6, first.Entries.TotalCount);
        Assert.Equal(FileSpaceEntryKind.Folder, first.Entries.Items[0].Kind);
        Assert.Equal(FileSpaceEntryKind.File, first.Entries.Items[3].Kind);
        Assert.Equal("/Docs", first.Path);
        Assert.Equal(["Docs"], first.Breadcrumbs.Select(b => b.Name));
    }

    [Fact]
    public async Task Files_SortBySize_AndByName_Descending()
    {
        await UploadAsync("small.txt", "1");
        await UploadAsync("large.txt", "1234567890");
        await UploadAsync("medium.txt", "12345");

        Assert.Equal(["large.txt", "medium.txt", "small.txt"], Names(await ListAsync(sortBy: FileSpaceSortField.Size, descending: true)));
        Assert.Equal(["small.txt", "medium.txt", "large.txt"], Names(await ListAsync(sortBy: FileSpaceSortField.Name, descending: true)));
    }

    [Fact]
    public async Task SomeoneElsesFoldersAndFiles_NeverAppear()
    {
        var theirFolder = (await CreateFolderService().EnsurePathAsync(SomeoneElse, "/Theirs")).Data!;
        await SaveForSomeoneElseAsync("their-root.txt", "x");
        await SaveForSomeoneElseAsync("their-filed.txt", "x", theirFolder.Id);
        await UploadAsync("mine.txt", "x");

        Assert.Equal(["mine.txt"], Names(await ListAsync()));
        Assert.Equal(["mine.txt"], (await SearchAsync()).Select(e => e.Name));

        DbContext.ChangeTracker.Clear();
        var listTheirs = await CreateSpaceService().ListAsync(new FileSpaceListRequest { FolderId = theirFolder.Id });
        Assert.Equal(404, listTheirs.Code);
    }

    /// <summary>
    /// ★ 根目录不是「名下所有没放进目录的文件」：头像、聊天附件这类被业务记录引用的临时上传不在里面；
    /// 正式记录里只剩业务记录持有的（上传者删掉了自己那一份、消息还引用着）也不在；还没被任何东西持有的临时上传也不在。
    /// 放进来的（文件空间里的上传）在。
    /// </summary>
    [Fact]
    public async Task TheRoot_HoldsWhatWasPutThere_NotEveryFileIOwn()
    {
        await SeedTemporaryAttachmentAsync("avatar.png.txt");
        await SeedFileHeldOnlyByAMessageAsync("forwarded.txt");
        await CreateStorageService().SaveAsync("draft.txt", new MemoryStream([1]), isTemporary: true);
        await UploadAsync("notes.txt", "notes");

        Assert.Equal(["notes.txt"], Names(await ListAsync()));
        Assert.Equal(["notes.txt"], (await SearchAsync()).Select(e => e.Name));
    }

    /// <summary>放进文件空间的文件后来被一条消息引用了，它仍然在原处：文件空间的那一份持有还在。</summary>
    [Fact]
    public async Task AFileLaterAttachedToAMessage_StaysInTheFileSpace()
    {
        var notes = await UploadAsync("notes.txt", "notes");

        await BindToAMessageAsync(notes.Id);

        Assert.Equal(["notes.txt"], Names(await ListAsync()));
    }

    [Fact]
    public async Task Search_MatchesNameExtensionAndContentType_AndSaysWhereEachFileIs()
    {
        var reports = await CreateFolderAsync("Reports");
        await UploadAsync("Quarterly Summary.md", "q", reports.Id);
        await UploadAsync("summary.txt", "s");
        await UploadAsync("photo.png", "p");

        var byName = await SearchAsync(keyword: "SUMMARY");
        Assert.Equal(2, byName.Count);
        Assert.Contains(byName, e => e.Name == "Quarterly Summary.md" && e.Location == "/Reports" && e.ParentId == reports.Id);
        Assert.Contains(byName, e => e.Name == "summary.txt" && e.Location == "/");

        Assert.Equal(["Quarterly Summary.md"], (await SearchAsync(extension: "md")).Select(e => e.Name));
        Assert.Equal(["photo.png"], (await SearchAsync(contentType: "image/")).Select(e => e.Name));
    }

    /// <summary>文件空间里的上传是独立的条目：同一份字节存进两个目录是两个文件，第二次不会把第一次挪走。</summary>
    [Fact]
    public async Task UploadingTheSameBytesTwice_GivesTwoFiles()
    {
        var a = await CreateFolderAsync("A");
        var b = await CreateFolderAsync("B");

        var first = await UploadAsync("report.txt", "identical", a.Id);
        var second = await UploadAsync("report.txt", "identical", b.Id);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(["report.txt"], Names(await ListAsync(a.Id)));
        Assert.Equal(["report.txt"], Names(await ListAsync(b.Id)));
    }

    [Fact]
    public async Task UploadingIntoSomeoneElsesFolder_IsNotFound()
    {
        var theirs = (await CreateFolderService().EnsurePathAsync(SomeoneElse, "/Theirs")).Data!;

        var result = await CreateSpaceService().UploadFileAsync(theirs.Id, "sneaky.txt", new MemoryStream([1]));

        Assert.Equal(404, result.Code);
        DbContext.ChangeTracker.Clear();
        Assert.False(await DbContext.FileRecords.AnyAsync(f => f.OriginalName == "sneaky.txt"));
    }

    [Fact]
    public async Task SaveForOwner_PutsTheFileInTheirSpace_AndRefusesAFolderThatIsNotTheirs()
    {
        var theirs = (await CreateFolderService().EnsurePathAsync(SomeoneElse, "/AI")).Data!;
        var mine = await CreateFolderAsync("Mine");

        var saved = await SaveForSomeoneElseAsync("summary.md", "# summary", theirs.Id);
        var wrongFolder = await CreateSpaceService().SaveForOwnerAsync(SomeoneElse, mine.Id, "x.md", new MemoryStream([1]));

        Assert.Equal(SomeoneElse, (await ReloadFileAsync(saved.Id))!.CreatorId);
        Assert.Equal(theirs.Id, (await ReloadFileAsync(saved.Id))!.FolderId);
        Assert.Equal(404, wrongFolder.Code);
    }

    /// <summary>超了配额：一条记录都不落，目录里也什么都没多出来。</summary>
    [Fact]
    public async Task UploadOverTheQuota_IsRefused_AndWritesNothing()
    {
        StorageOptions.Quota.DefaultUserAllowanceBytes = 8;
        var service = CreateSpaceService(CreateStorageService(quota: CreateQuotaService()));
        await UploadAsync("first.txt", "12345");

        var result = await service.UploadFileAsync(null, "second.txt", new MemoryStream(Encoding.UTF8.GetBytes("12345")));

        Assert.Equal(413, result.Code);
        Assert.Equal(Tnzi.Exceptions.ErrorCodes.FILE_QUOTA_EXCEEDED, result.ErrorCode);
        Assert.Equal(["first.txt"], Names(await ListAsync()));
    }
}
