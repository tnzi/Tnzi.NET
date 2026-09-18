namespace Tnzi.Storage.Workspace.Tests.Integration;

/// <summary>
/// 目录名不得含路径分隔符；改名 / 移动时后代路径的碰撞要以 409 回答，而不是让唯一索引炸成 500。
/// </summary>
/// <remarks>
/// <para>
/// <c>FileFolder.Path</c> 是把名字用 <c>/</c> 拼起来的，而它上面有一条唯一索引。名字里一旦允许 <c>/</c>，
/// 任何登录用户都能在根下造出 <c>{name:"archive/alpha"}</c> —— 一条 <c>ParentId</c> 为空、
/// <c>Path</c> 却长在别人前缀下的行。受害者把自己的 <c>/projects</c>（含 <c>/projects/alpha</c>）
/// 改名成 <c>archive</c> 时，自身路径的预检查通过（<c>/archive</c> 不存在），后代改写却撞上
/// <c>/archive/alpha</c>：唯一索引在提交那一刻抛异常，整个工作单元回滚，500，且每次重试都一样。
/// </para>
/// <para>
/// 两层修法：名字在进入路径拼装之前就拒掉分隔符与 <c>.</c>/<c>..</c>（400）；改名 / 移动在写之前
/// 先算出所有后代的新路径并与子树之外的现存行比对（409）。后者对存量脏行仍然有效 ——
/// 这里用直接插库的行模拟那种形态，因为修好之后服务层已经造不出它了。
/// </para>
/// </remarks>
public class FolderNameAndCollisionTests : WorkspaceIntegrationTestBase
{
    // ── 名字里的分隔符：400 ────────────────────────────────────────────────

    [Theory]
    [InlineData("archive/alpha")]
    [InlineData("archive\\alpha")]
    [InlineData("/archive")]
    [InlineData("archive/")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("tab\there")]
    public async Task CreateAsync_NameWithSeparatorOrDotOnly_Returns400(string name)
    {
        var service = CreateFolderService();

        var result = await service.CreateAsync(new CreateFileFolderDto { Name = name });

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        Assert.Contains("invalid characters", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(await DbContext.FileFolders.AnyAsync(), "no row may be written for a rejected name");
    }

    [Theory]
    [InlineData("archive/alpha")]
    [InlineData("..")]
    public async Task UpdateAsync_NameWithSeparatorOrDotOnly_Returns400(string name)
    {
        var service = CreateFolderService();
        var folder = (await service.CreateAsync(new CreateFileFolderDto { Name = "projects" })).Data!;
        DbContext.ChangeTracker.Clear();

        var result = await service.UpdateAsync(folder.Id, new UpdateFileFolderDto { Name = name });

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        DbContext.ChangeTracker.Clear();
        Assert.Equal("/projects", DbContext.FileFolders.Single(f => f.Id == folder.Id).Path);
    }

    [Fact]
    public async Task CreateAsync_NameWithInnerSpacesAndDots_IsStillAccepted()
    {
        // 拒的是分隔符与「只有点」的名字，不是普通带点的名字：`v1.2 release` 这种要照常建。
        var service = CreateFolderService();

        var result = await service.CreateAsync(new CreateFileFolderDto { Name = "v1.2 release" });

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("/v1.2 release", result.Data!.Path);
    }

    // ── 后代路径碰撞：409 而不是唯一索引的 500 ─────────────────────────────

    [Fact]
    public async Task UpdateAsync_RenameWhoseDescendantCollides_Returns409NotThrows()
    {
        var service = CreateFolderService();
        var projects = (await service.CreateAsync(new CreateFileFolderDto { Name = "projects" })).Data!;
        _ = (await service.CreateAsync(new CreateFileFolderDto { Name = "alpha", ParentId = projects.Id })).Data!;
        // 存量脏行：根下一条 Path 长在别人前缀里的目录（修好之后服务层已造不出它）。
        DbContext.FileFolders.Add(new FileFolder { Name = "archive/alpha", Path = "/archive/alpha" });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var result = await service.UpdateAsync(projects.Id, new UpdateFileFolderDto { Name = "archive" });

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Code);
        DbContext.ChangeTracker.Clear();
        Assert.Equal("/projects", DbContext.FileFolders.Single(f => f.Id == projects.Id).Path);
        Assert.Equal("/projects/alpha", DbContext.FileFolders.Single(f => f.Name == "alpha").Path);
    }

    [Fact]
    public async Task MoveAsync_DescendantCollision_Returns409()
    {
        var service = CreateFolderService();
        var projects = (await service.CreateAsync(new CreateFileFolderDto { Name = "projects" })).Data!;
        _ = (await service.CreateAsync(new CreateFileFolderDto { Name = "alpha", ParentId = projects.Id })).Data!;
        var archive = (await service.CreateAsync(new CreateFileFolderDto { Name = "archive" })).Data!;
        DbContext.FileFolders.Add(new FileFolder { Name = "projects/alpha", Path = "/archive/projects/alpha" });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var result = await service.MoveAsync(projects.Id, archive.Id);

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Code);
        DbContext.ChangeTracker.Clear();
        Assert.Equal("/projects", DbContext.FileFolders.Single(f => f.Id == projects.Id).Path);
    }

    [Fact]
    public async Task UpdateAsync_Rename_RebuildsDescendantPathsFromTheParentChain()
    {
        // 后代按 ParentId 闭包重建路径，而不是按字符串前缀改写：一条恰好以旧路径开头、
        // 却不属于这棵子树的行（存量脏行）不能被顺手改掉。
        var service = CreateFolderService();
        var projects = (await service.CreateAsync(new CreateFileFolderDto { Name = "projects" })).Data!;
        var child = (await service.CreateAsync(new CreateFileFolderDto { Name = "alpha", ParentId = projects.Id })).Data!;
        var stranger = new FileFolder { Name = "projects/beta", Path = "/projects/beta" };
        DbContext.FileFolders.Add(stranger);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var result = await service.UpdateAsync(projects.Id, new UpdateFileFolderDto { Name = "archive" });

        Assert.True(result.Succeeded, result.Message);
        DbContext.ChangeTracker.Clear();
        Assert.Equal("/archive/alpha", DbContext.FileFolders.Single(f => f.Id == child.Id).Path);
        Assert.Equal("/projects/beta", DbContext.FileFolders.Single(f => f.Id == stranger.Id).Path);
    }
}
