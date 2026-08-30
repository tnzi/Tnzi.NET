namespace Tnzi.Storage.Workspace.Tests.Integration;

/// <summary>
/// T13：目录移动 / 改名时，后代的 <c>Path</c> 必须整体一致地跟着变（多写包在一个工作单元里）。
/// </summary>
/// <remarks>原在父测试项目的 <c>StorageQueryDeleteCopyTests</c> 里，随目录服务搬来，内容一字未改。</remarks>
public class FolderPathAtomicityTests : WorkspaceIntegrationTestBase
{
    // ==================== T13: Folder move atomicity ====================

    [Fact]
    public async Task MoveAsync_UpdatesDescendantPathsConsistently()
    {
        var service = CreateFolderService();
        var parent = (await service.CreateAsync(new CreateFileFolderDto { Name = "parent" })).Data!;
        var child = (await service.CreateAsync(new CreateFileFolderDto { Name = "child", ParentId = parent.Id })).Data!;
        var grandchild = (await service.CreateAsync(new CreateFileFolderDto { Name = "grandchild", ParentId = child.Id })).Data!;
        var newRoot = (await service.CreateAsync(new CreateFileFolderDto { Name = "newroot" })).Data!;
        DbContext.ChangeTracker.Clear();

        var result = await service.MoveAsync(parent.Id, newRoot.Id);

        Assert.True(result.Succeeded);
        DbContext.ChangeTracker.Clear();
        Assert.Equal("/newroot/parent", DbContext.FileFolders.Single(f => f.Id == parent.Id).Path);
        Assert.Equal("/newroot/parent/child", DbContext.FileFolders.Single(f => f.Id == child.Id).Path);
        Assert.Equal("/newroot/parent/child/grandchild", DbContext.FileFolders.Single(f => f.Id == grandchild.Id).Path);
    }

    [Fact]
    public async Task UpdateAsync_Rename_UpdatesDescendantPathsConsistently()
    {
        var service = CreateFolderService();
        var parent = (await service.CreateAsync(new CreateFileFolderDto { Name = "docs" })).Data!;
        var child = (await service.CreateAsync(new CreateFileFolderDto { Name = "sub", ParentId = parent.Id })).Data!;
        DbContext.ChangeTracker.Clear();

        var result = await service.UpdateAsync(parent.Id, new UpdateFileFolderDto { Name = "documents" });

        Assert.True(result.Succeeded);
        DbContext.ChangeTracker.Clear();
        Assert.Equal("/documents", DbContext.FileFolders.Single(f => f.Id == parent.Id).Path);
        Assert.Equal("/documents/sub", DbContext.FileFolders.Single(f => f.Id == child.Id).Path);
    }

    // ==================== T13: Copy server-side fallback ====================
}
