namespace Tnzi.Storage.Tests;

/// <summary>
/// 覆盖默认文件访问策略本身。这些用例是那道墙的回归网:它们一旦被放宽,
/// "凭 id 就能下载任意文件" 会立刻回来 —— 框架的实体 ID 是顺序 GUID,可枚举。
/// </summary>
public class FileAccessAuthorizerTests
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StrangerId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static FileRecord OwnedFile(bool isPublic = false) => new()
    {
        Id = Guid.NewGuid(),
        FileName = "secret.pdf",
        CreatorId = OwnerId,
        IsPublic = isPublic,
    };

    private static FileAccessAuthorizer CreateSut(
        ICurrentUser currentUser,
        bool allowAnonymousRead = false,
        IPermissionChecker? permissionChecker = null)
    {
        var options = new StorageOptions { AllowAnonymousRead = allowAnonymousRead };
        var monitor = new Mock<IOptionsMonitor<StorageOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(options);
        return new FileAccessAuthorizer(currentUser, monitor.Object, permissionChecker);
    }

    private static ICurrentUser Anonymous()
    {
        var m = new Mock<ICurrentUser>();
        m.SetupGet(u => u.IsAuthenticated).Returns(false);
        m.SetupGet(u => u.Id).Returns((Guid?)null);
        return m.Object;
    }

    private static ICurrentUser SignedInAs(Guid id)
    {
        var m = new Mock<ICurrentUser>();
        m.SetupGet(u => u.IsAuthenticated).Returns(true);
        m.SetupGet(u => u.Id).Returns(id);
        return m.Object;
    }

    private static IPermissionChecker Grants(params string[] codes)
    {
        var m = new Mock<IPermissionChecker>();
        m.Setup(p => p.IsGrantedAsync(It.IsAny<string>()))
            .ReturnsAsync((string code) => codes.Contains(code));
        return m.Object;
    }

    [Fact]
    public async Task Anonymous_CannotRead_APrivateFile()
    {
        var sut = CreateSut(Anonymous());
        Assert.False(await sut.CanReadAsync(OwnedFile()));
    }

    [Fact]
    public async Task Anonymous_CanRead_AFileMarkedPublic()
    {
        var sut = CreateSut(Anonymous());
        Assert.True(await sut.CanReadAsync(OwnedFile(isPublic: true)));
    }

    [Fact]
    public async Task Anonymous_CanRead_WhenTheDeploymentOptsIn()
    {
        var sut = CreateSut(Anonymous(), allowAnonymousRead: true);
        Assert.True(await sut.CanReadAsync(OwnedFile()));
    }

    [Fact]
    public async Task Anonymous_CannotWrite_EvenAPublicFile()
    {
        // Public means readable, never writable: otherwise anyone could delete
        // the site's shared assets.
        var sut = CreateSut(Anonymous(), allowAnonymousRead: true);
        Assert.False(await sut.CanWriteAsync(OwnedFile(isPublic: true)));
    }

    [Fact]
    public async Task Owner_CanReadAndWrite()
    {
        var sut = CreateSut(SignedInAs(OwnerId));
        var file = OwnedFile();
        Assert.True(await sut.CanReadAsync(file));
        Assert.True(await sut.CanWriteAsync(file));
    }

    [Fact]
    public async Task Stranger_CanNeitherReadNorWrite_WithoutPermissions()
    {
        var sut = CreateSut(SignedInAs(StrangerId), permissionChecker: Grants());
        var file = OwnedFile();
        Assert.False(await sut.CanReadAsync(file));
        Assert.False(await sut.CanWriteAsync(file));
    }

    [Fact]
    public async Task Stranger_WithFileViewPermission_CanReadButNotWrite()
    {
        var sut = CreateSut(SignedInAs(StrangerId), permissionChecker: Grants(StoragePermissionNames.FileView));
        var file = OwnedFile();
        Assert.True(await sut.CanReadAsync(file));
        Assert.False(await sut.CanWriteAsync(file));
    }

    [Fact]
    public async Task Stranger_WithFileUpdatePermission_CanWrite()
    {
        var sut = CreateSut(SignedInAs(StrangerId), permissionChecker: Grants(StoragePermissionNames.FileUpdate));
        Assert.True(await sut.CanWriteAsync(OwnedFile()));
    }

    [Fact]
    public async Task OwnerlessFile_IsNotTreatedAsPublic()
    {
        // CreatorId == null happens for records created by background jobs or
        // migrated data. Treating "no owner" as "everyone owns it" would be the
        // same hole in a different shape, so it stays admin-only.
        var file = new FileRecord { Id = Guid.NewGuid(), FileName = "orphan.bin", CreatorId = null };
        var sut = CreateSut(SignedInAs(StrangerId), permissionChecker: Grants());
        Assert.False(await sut.CanReadAsync(file));
        Assert.False(await sut.CanWriteAsync(file));
    }

    [Fact]
    public async Task WithoutAuthorizationModule_OnlyOwnershipCounts()
    {
        // No IPermissionChecker registered (Authorization module not loaded):
        // deny rather than fall open, because ownership is then the only
        // trustworthy signal.
        var sut = CreateSut(SignedInAs(StrangerId), permissionChecker: null);
        Assert.False(await sut.CanReadAsync(OwnedFile()));

        var ownerSut = CreateSut(SignedInAs(OwnerId), permissionChecker: null);
        Assert.True(await ownerSut.CanReadAsync(OwnedFile()));
    }
}
