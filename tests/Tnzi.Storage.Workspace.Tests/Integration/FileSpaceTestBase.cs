namespace Tnzi.Storage.Workspace.Tests.Integration;

/// <summary>
/// 文件空间、回收站、配额用例的共同夹具。当前用户是 <see cref="TestHelper.DefaultTestUserId"/>（<see cref="Me"/>），
/// 别人的东西经系统身份的入口（<c>EnsurePathAsync</c> / <c>SaveForOwnerAsync</c>）或直接插库造出来。
/// </summary>
public abstract class FileSpaceTestBase : WorkspaceIntegrationTestBase
{
    protected static readonly Guid Me = TestHelper.DefaultTestUserId;
    protected static readonly Guid SomeoneElse = Guid.Parse("33333333-3333-3333-3333-333333333333");

    protected FileSpaceTestBase()
    {
        // 文件空间什么类型都收（.md 摘要、.pdf …）；扩展名白名单由别的用例守。
        StorageOptions.AllowedExtensions = [];
    }

    protected async Task<FileFolderDto> CreateFolderAsync(string name, Guid? parentId = null)
    {
        var result = await CreateFolderService().CreateAsync(new CreateFileFolderDto { Name = name, ParentId = parentId });
        Assert.True(result.Succeeded, result.Message);
        return result.Data!;
    }

    protected async Task<FileRecord> UploadAsync(string name, string content, Guid? folderId = null)
    {
        var result = await CreateSpaceService().UploadFileAsync(folderId, name, new MemoryStream(Encoding.UTF8.GetBytes(content)));
        Assert.True(result.Succeeded, result.Message);
        return result.Data!;
    }

    protected async Task<FileRecord> SaveForSomeoneElseAsync(string name, string content, Guid? folderId = null)
    {
        var result = await CreateSpaceService().SaveForOwnerAsync(SomeoneElse, folderId, name, new MemoryStream(Encoding.UTF8.GetBytes(content)));
        Assert.True(result.Succeeded, result.Message);
        return result.Data!;
    }

    /// <summary>
    /// 头像 / 聊天附件形态：先临时上传，再被一条业务记录（<c>[FileField]</c>）引用。它仍是临时记录，计数等于引用行数。
    /// </summary>
    protected async Task<FileRecord> SeedTemporaryAttachmentAsync(string name)
    {
        var saved = await CreateStorageService().SaveAsync(name, new MemoryStream(Encoding.UTF8.GetBytes(name)), isTemporary: true);
        Assert.True(saved.Succeeded, saved.Message);
        await BindToAMessageAsync(saved.Data!.Id);
        return await ReloadFileAsync(saved.Data.Id) ?? throw new InvalidOperationException("seeded file vanished");
    }

    /// <summary>
    /// 正式记录、只剩业务记录在持有：正式上传、被一条消息引用，然后上传者删掉了自己那一份（父模块的 DELETE files/{id}
    /// 只放掉隐式持有者，消息那一份留着）。计数等于引用行数，没有隐式持有者。
    /// </summary>
    protected async Task<FileRecord> SeedFileHeldOnlyByAMessageAsync(string name)
    {
        var saved = await CreateStorageService().SaveAsync(name, new MemoryStream(Encoding.UTF8.GetBytes(name)), new FileSaveOptions { Deduplicate = false });
        Assert.True(saved.Succeeded, saved.Message);
        await BindToAMessageAsync(saved.Data!.Id);
        Assert.True((await CreateStorageService().DeleteAsync(saved.Data.Id)).Succeeded);

        var reloaded = await ReloadFileAsync(saved.Data.Id) ?? throw new InvalidOperationException("seeded file vanished");
        Assert.False(reloaded.IsTemporary);
        Assert.Equal(1, reloaded.ReferenceCount);
        return reloaded;
    }

    /// <summary>像一条业务记录那样经引用处理器绑定这个文件（正式引用行 + 计数 +1，临时记录随之转正）。</summary>
    protected async Task BindToAMessageAsync(Guid fileId)
    {
        var processor = new FileReferenceProcessor(
            Repo<FileReference>(),
            Repo<FileRecord>(),
            NullLogger<FileReferenceProcessor>.Instance,
            new StaticOptionsMonitor<StorageOptions>(StorageOptions));

        await processor.ProcessChangesAsync([new Tnzi.Storage.FileReferenceChange
        {
            ChangeType = Tnzi.Storage.FileReferenceChangeType.Create,
            EntityType = "ChatMessage",
            EntityId = Guid.NewGuid().ToString(),
            FieldName = "FileId",
            FileId = fileId
        }]);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    protected async Task<FileRecord?> ReloadFileAsync(Guid id)
    {
        DbContext.ChangeTracker.Clear();
        return await DbContext.FileRecords.SingleOrDefaultAsync(f => f.Id == id);
    }

    protected async Task<FileSpaceListingDto> ListAsync(Guid? folderId = null, FileSpaceSortField sortBy = FileSpaceSortField.Name, bool descending = false, int pageIndex = 1, int pageSize = 50)
    {
        DbContext.ChangeTracker.Clear();
        var result = await CreateSpaceService().ListAsync(new FileSpaceListRequest
        {
            FolderId = folderId,
            SortBy = sortBy,
            Descending = descending,
            PageIndex = pageIndex,
            PageSize = pageSize
        });
        Assert.True(result.Succeeded, result.Message);
        return result.Data!;
    }

    protected async Task<IList<FileSpaceEntryDto>> SearchAsync(string? keyword = null, string? extension = null, string? contentType = null)
    {
        DbContext.ChangeTracker.Clear();
        var result = await CreateSpaceService().SearchAsync(new FileSpaceSearchRequest
        {
            Keyword = keyword,
            Extension = extension,
            ContentType = contentType
        });
        Assert.True(result.Succeeded, result.Message);
        return result.Data!.Items;
    }

    protected static List<string> Names(FileSpaceListingDto listing) => listing.Entries.Items.Select(e => e.Name).ToList();
}
