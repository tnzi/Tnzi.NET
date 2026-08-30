namespace Tnzi.Storage.Workspace.Tests.Integration;

/// <summary>
/// 目录面的行级授权：<b>自己的</b> 或 <b>持对应权限码</b>。
/// </summary>
/// <remarks>
/// <para>
/// 这一层此前完全不存在：<c>FileFolderService</c> 一个授权判据都没有，而它的用户端控制器
/// 只挂了裸 <c>[ApiAuthorize]</c>（登录即可）。于是任何已登录用户都能读到整个部署的目录树、
/// 改名 / 移动 / 删除别人的目录，以及 —— 影响最大的一条 —— 把任意 id 的文件重新归档或抽出目录。
/// </para>
/// <para>
/// 这些用例的当前用户是 <c>TestHelper.DefaultTestUserId</c>，且**没有注册 IPermissionChecker**
/// （宿主未加载 Authorization 模块），所以「持码」那条分支恒假 —— 判据只剩归属，
/// 正是那个「没有权限体系的部署里归属是唯一可信判据」的场景。
/// </para>
/// </remarks>
public class FolderAuthorizationTests : WorkspaceIntegrationTestBase
{
    private static readonly Guid Stranger = Guid.Parse("99999999-9999-9999-9999-999999999999");

    /// <summary>直接经 DbContext 插入一条属于别人的目录（绕开审计钩子对 CreatorId 的赋值）。</summary>
    private async Task<FileFolder> SeedForeignFolderAsync(string name = "theirs")
    {
        var folder = new FileFolder
        {
            Id = Guid.NewGuid(),
            Name = name,
            Path = "/" + name,
            CreatorId = Stranger,
        };

        DbContext.FileFolders.Add(folder);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return folder;
    }

    private async Task<FileRecord> SeedFileAsync(Guid? folderId)
    {
        var record = new FileRecord
        {
            Id = Guid.NewGuid(),
            FileName = "report.pdf",
            OriginalName = "report.pdf",
            Extension = ".pdf",
            ContentType = "application/pdf",
            Size = 4,
            Path = "seed/report.pdf",
            FolderId = folderId,
            CreatorId = Stranger,
        };

        DbContext.FileRecords.Add(record);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return record;
    }

    // ==================== 写路径：重新归档别人的文件 ====================

    /// <summary>
    /// ★ 这条是本轮修的主缺陷：把任意 id 的文件抽出目录，此前返回 200 并真的写了下去。
    /// </summary>
    [Fact]
    public async Task MoveFilesToFolder_RefusesFilesTheCallerCannotWrite()
    {
        var owner = (await CreateFolderService().CreateAsync(new CreateFileFolderDto { Name = "victim" })).Data!;
        var record = await SeedFileAsync(owner.Id);

        // 看得见但不许改 —— 正是这个缺陷的形状。
        var service = CreateFolderService(TestFileAccessAuthorizer.ReadOnly());

        var result = await service.MoveFilesToFolderAsync([record.Id], null);

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);

        // 同时断言状态：只返回错误却照样写下去的实现也能骗过前一条。
        DbContext.ChangeTracker.Clear();
        Assert.Equal(owner.Id, DbContext.FileRecords.Single(f => f.Id == record.Id).FolderId);
    }

    /// <summary>一条不行就整批拒绝：批量归档是一次意图，做一半会留下调用方无从得知的中间状态。</summary>
    [Fact]
    public async Task MoveFilesToFolder_RefusesTheWholeBatchWhenOneFileIsDenied()
    {
        var folder = (await CreateFolderService().CreateAsync(new CreateFileFolderDto { Name = "dest" })).Data!;
        var mine = await SeedFileAsync(null);
        var theirs = await SeedFileAsync(null);

        var service = CreateFolderService(TestFileAccessAuthorizer.ReadOnly());

        var result = await service.MoveFilesToFolderAsync([mine.Id, theirs.Id], folder.Id);

        Assert.False(result.Succeeded);
        DbContext.ChangeTracker.Clear();
        Assert.Null(DbContext.FileRecords.Single(f => f.Id == mine.Id).FolderId);
        Assert.Null(DbContext.FileRecords.Single(f => f.Id == theirs.Id).FolderId);
    }

    /// <summary>放行那一半必须仍然工作，否则这道门就是把功能关掉而不是修好。</summary>
    [Fact]
    public async Task MoveFilesToFolder_StillMovesFilesTheCallerMayWrite()
    {
        var folder = (await CreateFolderService().CreateAsync(new CreateFileFolderDto { Name = "dest" })).Data!;
        var record = await SeedFileAsync(null);

        var result = await CreateFolderService().MoveFilesToFolderAsync([record.Id], folder.Id);

        Assert.True(result.Succeeded);
        DbContext.ChangeTracker.Clear();
        Assert.Equal(folder.Id, DbContext.FileRecords.Single(f => f.Id == record.Id).FolderId);
    }

    /// <summary>目标目录不属于你，也不能往里塞东西。</summary>
    [Fact]
    public async Task MoveFilesToFolder_RefusesAForeignTargetFolder()
    {
        var foreign = await SeedForeignFolderAsync();
        var record = await SeedFileAsync(null);

        var result = await CreateFolderService().MoveFilesToFolderAsync([record.Id], foreign.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
        DbContext.ChangeTracker.Clear();
        Assert.Null(DbContext.FileRecords.Single(f => f.Id == record.Id).FolderId);
    }

    // ==================== 目录自身的 CRUD ====================

    [Fact]
    public async Task Update_Delete_And_Move_RefuseAForeignFolder()
    {
        var foreign = await SeedForeignFolderAsync();
        var service = CreateFolderService();

        var renamed = await service.UpdateAsync(foreign.Id, new UpdateFileFolderDto { Name = "hijacked" });
        var deleted = await service.DeleteAsync(foreign.Id);
        var moved = await service.MoveAsync(foreign.Id, null);

        Assert.False(renamed.Succeeded);
        Assert.False(deleted.Succeeded);
        Assert.False(moved.Succeeded);

        // 404 而不是 403：403 会确认这个 id 上确实有目录，而 id 是顺序 GUID。
        Assert.Equal(404, renamed.Code);

        DbContext.ChangeTracker.Clear();
        var still = DbContext.FileFolders.Single(f => f.Id == foreign.Id);
        Assert.Equal("theirs", still.Name);
        Assert.False(still.IsDeleted);
    }

    [Fact]
    public async Task Get_RefusesAForeignFolder_ButReturnsYourOwn()
    {
        var foreign = await SeedForeignFolderAsync();
        var service = CreateFolderService();
        var mine = (await service.CreateAsync(new CreateFileFolderDto { Name = "mine" })).Data!;

        Assert.False((await service.GetAsync(foreign.Id)).Succeeded);
        Assert.True((await service.GetAsync(mine.Id)).Succeeded);
    }

    [Fact]
    public async Task Create_RefusesToNestUnderAForeignParent()
    {
        var foreign = await SeedForeignFolderAsync();

        var result = await CreateFolderService()
            .CreateAsync(new CreateFileFolderDto { Name = "sneaky", ParentId = foreign.Id });

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
    }

    // ==================== 列举 ====================

    /// <summary>
    /// 目录树此前把整个部署的目录名 / 路径 / 描述 / 文件数交给任何已登录用户。
    /// </summary>
    [Fact]
    public async Task GetTree_ListsOnlyYourOwnFolders()
    {
        await SeedForeignFolderAsync("their-clients");
        var service = CreateFolderService();
        await service.CreateAsync(new CreateFileFolderDto { Name = "my-stuff" });
        DbContext.ChangeTracker.Clear();

        var tree = await service.GetTreeAsync();

        Assert.True(tree.Succeeded);
        Assert.Single(tree.Data!);
        Assert.Equal("my-stuff", tree.Data![0].Name);
    }
}
