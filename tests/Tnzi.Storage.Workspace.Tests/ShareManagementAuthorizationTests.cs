using System.Linq.Expressions;
using Tnzi.Security.Authorization;
using Tnzi.Storage.Permissions;

namespace Tnzi.Storage.Workspace.Tests;

/// <summary>
/// 谁能<b>管理</b>一条分享链接（撤销它、看它的管理视角）：与创建它所要求的是同一份权利。
/// </summary>
/// <remarks>
/// <para>
/// 此前 <c>RevokeShareAsync</c> 与 <c>GetShareAsync</c> 一个判据都没有：任何已登录用户拿到一个泄漏的令牌，
/// 就能把别人发出去的链接撤掉（创建者毫不知情），或读出这条链接背后的 <c>FileId</c> 与计数 ——
/// 哪怕链接早已撤销 / 过期。对照 <c>CreateShareAsync</c>：它要求对文件有<b>写</b>权限。
/// </para>
/// <para>
/// 拒绝一律 404「Share not found」，与令牌不存在同一句 —— 区分开就等于告诉试探者这个令牌是真的。
/// 收件人自己的视角是 <c>share/{token}/info</c>（<c>FileSharePreviewDto</c>，不含 FileId），不受本批影响。
/// </para>
/// </remarks>
public class ShareManagementAuthorizationTests
{
    private static readonly Guid Me = TestHelper.DefaultTestUserId;
    private static readonly Guid SomeoneElse = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private readonly Mock<IRepository<FileShare, Guid>> _shares = new();
    private readonly Mock<IRepository<FileRecord, Guid>> _files = new();
    private readonly Mock<IServiceProvider> _services = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly FileAccessGrantContext _grants = new();

    public ShareManagementAuthorizationTests()
    {
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        _services.Setup(sp => sp.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);

        _currentUser.Setup(u => u.Id).Returns(Me);
        _currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        _services.Setup(sp => sp.GetService(typeof(ICurrentUser))).Returns(_currentUser.Object);
    }

    // ── 撤销 ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Revoke_ByAStrangerHoldingTheToken_IsRefused_AndTheShareStaysEnabled()
    {
        // 令牌泄漏了（贴进工单、抄进聊天记录）。拿到它的人既不是创建者也写不了那个文件。
        var share = SeedShare(creator: SomeoneElse, fileOwner: SomeoneElse);
        var service = CreateService(TestFileAccessAuthorizer.ReadOnly());

        var result = await service.RevokeShareAsync(share.ShareToken);

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
        Assert.True(share.IsEnabled);
        _shares.Verify(r => r.UpdateAsync(It.IsAny<FileShare>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Revoke_ByTheCreator_Succeeds_EvenWithoutFileWriteRights()
    {
        // 创建者后来失去了对文件的变更权（转岗、文件被移交）：他发出去的链接仍归他收回。
        var share = SeedShare(creator: Me, fileOwner: SomeoneElse);
        var service = CreateService(TestFileAccessAuthorizer.ReadOnly());

        var result = await service.RevokeShareAsync(share.ShareToken);

        Assert.True(result.Succeeded, result.Message);
        Assert.False(share.IsEnabled);
    }

    [Fact]
    public async Task Revoke_BySomeoneWhoCanWriteTheFile_Succeeds()
    {
        // 文件的所有者 / 持 storage.file.update 的管理员：创建这条链接要的就是这份权利。
        var share = SeedShare(creator: SomeoneElse, fileOwner: SomeoneElse);
        var service = CreateService(TestFileAccessAuthorizer.AllowAll());

        var result = await service.RevokeShareAsync(share.ShareToken);

        Assert.True(result.Succeeded, result.Message);
        Assert.False(share.IsEnabled);
    }

    [Fact]
    public async Task Revoke_WhenTheFileIsGone_FallsBackToTheUpdatePermission()
    {
        // 文件已删而分享行还在：授权器没有 FileRecord 可问，只剩权限码这一条路。
        var share = SeedShare(creator: SomeoneElse, fileOwner: null);
        var withoutPermissions = CreateService(TestFileAccessAuthorizer.AllowAll());
        Assert.Equal(404, (await withoutPermissions.RevokeShareAsync(share.ShareToken)).Code);
        Assert.True(share.IsEnabled);

        GrantPermission(StoragePermissionNames.FileUpdate);
        var asAdmin = CreateService(TestFileAccessAuthorizer.AllowAll());
        Assert.True((await asAdmin.RevokeShareAsync(share.ShareToken)).Succeeded);
        Assert.False(share.IsEnabled);
    }

    [Fact]
    public async Task Revoke_ByAnAnonymousCaller_IsRefused()
    {
        _currentUser.Setup(u => u.Id).Returns((Guid?)null);
        _currentUser.Setup(u => u.IsAuthenticated).Returns(false);
        var share = SeedShare(creator: SomeoneElse, fileOwner: SomeoneElse);
        var service = CreateService(TestFileAccessAuthorizer.ReadOnly());

        Assert.Equal(404, (await service.RevokeShareAsync(share.ShareToken)).Code);
        Assert.True(share.IsEnabled);
    }

    // ── 管理视角 ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetShare_ByAStranger_IsNotFound_EvenWhileTheLinkIsLive()
    {
        // 管理视角带 FileId 与计数。令牌持有者要看的东西在 share/{token}/info 里，不在这里。
        var share = SeedShare(creator: SomeoneElse, fileOwner: SomeoneElse);
        var service = CreateService(TestFileAccessAuthorizer.ReadOnly());

        var result = await service.GetShareAsync(share.ShareToken);

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task GetShare_OfARevokedLink_ByAStranger_IsNotFound()
    {
        // 这条是审计点名的形态：撤销 / 过期的链接此前照样把 FileId 与状态交给任何登录用户。
        var share = SeedShare(creator: SomeoneElse, fileOwner: SomeoneElse);
        share.IsEnabled = false;
        share.ExpiresAt = DateTime.UtcNow.AddDays(-1);
        var service = CreateService(TestFileAccessAuthorizer.ReadOnly());

        Assert.Equal(404, (await service.GetShareAsync(share.ShareToken)).Code);
    }

    [Fact]
    public async Task GetShare_ByTheCreator_ShowsTheLink_IncludingARevokedOne()
    {
        // 管理视角的意义就是看状态：创建者要看得到「这条已经停用、被用了几次」，
        // 把停用的链接对他也折叠成 404 会让 DTO 上的 IsEnabled 永远为 true。
        var share = SeedShare(creator: Me, fileOwner: SomeoneElse);
        share.IsEnabled = false;
        share.AccessCount = 3;
        var service = CreateService(TestFileAccessAuthorizer.ReadOnly());

        var result = await service.GetShareAsync(share.ShareToken);

        Assert.True(result.Succeeded, result.Message);
        Assert.False(result.Data!.IsEnabled);
        Assert.Equal(3, result.Data.AccessCount);
        Assert.Equal(share.FileId, result.Data.FileId);
    }

    [Fact]
    public async Task GetShare_WithinTheRequestThatValidatedTheLink_ShowsIt()
    {
        // 下载流程是 Validate → Get：收件人没有账号，校验通过后本次请求已被授予该文件，
        // 控制器要凭 GetShare 拿到 FileId 才取得到字节。这条不能被管理判据挡掉。
        var share = SeedShare(creator: SomeoneElse, fileOwner: SomeoneElse);
        _currentUser.Setup(u => u.Id).Returns((Guid?)null);
        _currentUser.Setup(u => u.IsAuthenticated).Returns(false);
        var service = CreateService(TestFileAccessAuthorizer.DenyAll());

        Assert.Equal(404, (await service.GetShareAsync(share.ShareToken)).Code);

        _grants.Grant(share.FileId);

        var result = await service.GetShareAsync(share.ShareToken);
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(share.FileId, result.Data!.FileId);
    }

    [Fact]
    public async Task AGrant_DoesNotLetTheRecipientRevoke()
    {
        // 授予是「这一次请求可以读这个文件」，不是「你是这条链接的主人」。
        var share = SeedShare(creator: SomeoneElse, fileOwner: SomeoneElse);
        _grants.Grant(share.FileId);
        var service = CreateService(TestFileAccessAuthorizer.DenyAll());

        Assert.Equal(404, (await service.RevokeShareAsync(share.ShareToken)).Code);
        Assert.True(share.IsEnabled);
    }

    // ── 夹具 ────────────────────────────────────────────────────────────────

    private FileShareService CreateService(IFileAccessAuthorizer authorizer)
        => new(
            _shares.Object,
            _files.Object,
            authorizer,
            _grants,
            new StaticOptionsMonitor<StorageOptions>(new StorageOptions()),
            _services.Object);

    /// <summary>一条分享 + 它指向的文件（<paramref name="fileOwner"/> 为 null 表示文件已被删掉）。</summary>
    private FileShare SeedShare(Guid? creator, Guid? fileOwner)
    {
        var fileId = Guid.NewGuid();
        var share = new FileShare
        {
            Id = Guid.NewGuid(),
            FileId = fileId,
            ShareToken = "token-" + Guid.NewGuid().ToString("N"),
            IsEnabled = true,
            CreatorId = creator
        };

        _shares.Setup(r => r.FindAsync(It.IsAny<Expression<Func<FileShare, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);
        _shares.Setup(r => r.GetAsync(share.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(share);
        _shares.Setup(r => r.UpdateAsync(It.IsAny<FileShare>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _files.Setup(r => r.GetAsync(fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileOwner is null ? null : new FileRecord { Id = fileId, CreatorId = fileOwner });

        return share;
    }

    private void GrantPermission(string permissionName)
    {
        var checker = new Mock<IPermissionChecker>();
        checker.Setup(c => c.IsGrantedAsync(permissionName)).ReturnsAsync(true);
        _services.Setup(sp => sp.GetService(typeof(IPermissionChecker))).Returns(checker.Object);
    }
}
