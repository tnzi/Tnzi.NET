namespace Tnzi.Storage.Workspace.Tests.Integration;

/// <summary>
/// 目录路径按所有者唯一：每个用户有自己的命名空间，两个人都可以有 <c>/Documents</c>，
/// 也不会有哪个回答顺带告诉一个人别人的目录叫什么。
/// </summary>
/// <remarks>
/// 此前路径按租户唯一：用户 B 建不了 A 已有的 <c>/Documents</c>，那个 409 还确认了 A 有这个目录。
/// </remarks>
public class FolderOwnerNamespaceTests : FileSpaceTestBase
{
    [Fact]
    public async Task TwoUsers_CanBothHaveTheSamePath()
    {
        var theirs = (await CreateFolderService().EnsurePathAsync(SomeoneElse, "/Documents")).Data!;

        var mine = await CreateFolderAsync("Documents");

        Assert.NotEqual(theirs.Id, mine.Id);
        Assert.Equal("/Documents", mine.Path);
        Assert.Equal(Me, mine.OwnerId);
        Assert.Equal(SomeoneElse, theirs.OwnerId);
    }

    [Fact]
    public async Task TheSamePathTwice_InMyOwnNamespace_IsStillAConflict()
    {
        await CreateFolderAsync("Documents");

        var second = await CreateFolderService().CreateAsync(new CreateFileFolderDto { Name = "Documents" });

        Assert.False(second.Succeeded);
        Assert.Equal(409, second.Code);
    }

    [Fact]
    public async Task RenamingIntoANameSomeoneElseUses_IsAllowed()
    {
        await CreateFolderService().EnsurePathAsync(SomeoneElse, "/Reports");
        var mine = await CreateFolderAsync("Drafts");

        var renamed = await CreateFolderService().UpdateAsync(mine.Id, new UpdateFileFolderDto { Name = "Reports" });

        Assert.True(renamed.Succeeded, renamed.Message);
        Assert.Equal("/Reports", renamed.Data!.Path);
    }

    [Fact]
    public async Task MyTree_HoldsOnlyMyFolders_AndAskingForSomeoneElsesTreeIsEmpty()
    {
        await CreateFolderService().EnsurePathAsync(SomeoneElse, "/Secret/Plans");
        await CreateFolderAsync("Mine");

        var tree = (await CreateFolderService().GetTreeAsync()).Data!;
        var theirs = (await CreateFolderService().GetTreeAsync(ownerId: SomeoneElse)).Data!;

        Assert.Equal(["Mine"], tree.Select(f => f.Name));
        Assert.Empty(theirs);
    }

    [Fact]
    public async Task EnsurePath_CreatesEveryMissingLevel_ForTheOwner_AndIsIdempotent()
    {
        var first = await CreateFolderService().EnsurePathAsync(SomeoneElse, "/Apps/Summaries");
        var second = await CreateFolderService().EnsurePathAsync(SomeoneElse, "Apps/Summaries/");

        Assert.True(first.Succeeded, first.Message);
        Assert.Equal(first.Data!.Id, second.Data!.Id);
        Assert.Equal("/Apps/Summaries", first.Data.Path);

        DbContext.ChangeTracker.Clear();
        var rows = await DbContext.FileFolders.Where(f => f.CreatorId == SomeoneElse).OrderBy(f => f.Path).ToListAsync();
        Assert.Equal(["/Apps", "/Apps/Summaries"], rows.Select(f => f.Path));
        Assert.Equal(rows[0].Id, rows[1].ParentId);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Apps/..")]
    public async Task EnsurePath_RefusesPathsThatNameNoFolder_OrUseIllegalNames(string path)
    {
        var result = await CreateFolderService().EnsurePathAsync(SomeoneElse, path);

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
    }

    /// <summary>
    /// ★ 把一个只剩消息在持有的正式文件放进目录，文件空间要持有它：不添这一份，消息一删计数归零、文件被物理删除，
    /// 而它还在用户的目录里；挪回根目录则它直接从文件空间消失。
    /// </summary>
    [Fact]
    public async Task FilingAFileHeldOnlyByAMessage_GivesTheFileSpaceItsOwnHold()
    {
        var file = await SeedFileHeldOnlyByAMessageAsync("forwarded.txt");
        var folder = await CreateFolderAsync("Inbox");

        Assert.True((await CreateFolderService().MoveFilesToFolderAsync([file.Id], folder.Id)).Succeeded);
        Assert.Equal(2, (await ReloadFileAsync(file.Id))!.ReferenceCount);

        // 再挪一次（包括挪回根目录）不再加：已经有隐式持有者了。
        Assert.True((await CreateFolderService().MoveFilesToFolderAsync([file.Id], null)).Succeeded);
        Assert.Equal(2, (await ReloadFileAsync(file.Id))!.ReferenceCount);
        Assert.Contains("forwarded.txt", Names(await ListAsync()));
    }

    /// <summary>聊天附件（被消息引用的临时上传）放进目录：转为正式记录，文件空间同样多一份持有。</summary>
    [Fact]
    public async Task FilingAnAttachment_PromotesIt_AndTheFileSpaceHoldsIt()
    {
        var attachment = await SeedTemporaryAttachmentAsync("attachment.txt");
        Assert.True(attachment.IsTemporary);
        var folder = await CreateFolderAsync("Attachments");

        Assert.True((await CreateFolderService().MoveFilesToFolderAsync([attachment.Id], folder.Id)).Succeeded);

        var filed = (await ReloadFileAsync(attachment.Id))!;
        Assert.False(filed.IsTemporary);
        Assert.Equal(2, filed.ReferenceCount);
        Assert.Equal(["attachment.txt"], Names(await ListAsync(folder.Id)));
    }
}

/// <summary>
/// 管理员（持全部存储权限码）在别人的目录上动手：建出的子目录归那个用户，不能把目录挂到另一个人的树上。
/// </summary>
public class FolderOwnerNamespaceAdminTests : FileSpaceTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        var permissions = new Mock<Tnzi.Security.Authorization.IPermissionChecker>();
        permissions.Setup(p => p.IsGrantedAsync(It.IsAny<string>())).ReturnsAsync(true);
        services.AddSingleton(permissions.Object);
    }

    [Fact]
    public async Task ASubfolderCreatedInSomeoneElsesFolder_BelongsToThem()
    {
        var theirs = (await CreateFolderService().EnsurePathAsync(SomeoneElse, "/Projects")).Data!;

        var created = await CreateFolderAsync("Q4", theirs.Id);

        Assert.Equal(SomeoneElse, created.OwnerId);
        Assert.Equal("/Projects/Q4", created.Path);
    }

    [Fact]
    public async Task AFolderCannotBeMovedUnderAnotherOwnersFolder()
    {
        var theirs = (await CreateFolderService().EnsurePathAsync(SomeoneElse, "/Projects")).Data!;
        var mine = await CreateFolderAsync("Mine");

        var moved = await CreateFolderService().MoveAsync(mine.Id, theirs.Id);

        Assert.False(moved.Succeeded);
        Assert.Equal(400, moved.Code);
    }

    [Fact]
    public async Task AnAdministrator_CanNarrowTheTreeToOneOwner()
    {
        await CreateFolderService().EnsurePathAsync(SomeoneElse, "/Projects");
        await CreateFolderAsync("Mine");

        var tree = (await CreateFolderService().GetTreeAsync(ownerId: SomeoneElse)).Data!;

        Assert.Equal(["Projects"], tree.Select(f => f.Name));
    }
}
