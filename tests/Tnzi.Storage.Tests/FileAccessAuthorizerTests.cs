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
        IPermissionChecker? permissionChecker = null,
        IEnumerable<FileReference>? references = null,
        IEnumerable<IFileReferenceAccessResolver>? referenceResolvers = null,
        IFileUrlSigner? urlSigner = null,
        IHttpContextAccessor? httpContextAccessor = null,
        IFileAccessGrantContext? grantContext = null)
    {
        var options = new StorageOptions { AllowAnonymousRead = allowAnonymousRead };
        var monitor = new Mock<IOptionsMonitor<StorageOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(options);
        return new FileAccessAuthorizer(
            currentUser,
            monitor.Object,
            ReferenceRepository(references ?? []),
            referenceResolvers ?? [],
            grantContext ?? new FileAccessGrantContext(),
            permissionChecker,
            urlSigner,
            httpContextAccessor);
    }

    private static IRepository<FileReference, Guid> ReferenceRepository(IEnumerable<FileReference> rows)
    {
        var list = rows.ToList();
        var repo = new Mock<IRepository<FileReference, Guid>>();
        repo.Setup(r => r.ToListAsync(It.IsAny<Expression<Func<FileReference, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Expression<Func<FileReference, bool>>? predicate, CancellationToken _) =>
                predicate == null ? list : list.Where(predicate.Compile()).ToList());
        return repo.Object;
    }

    /// <summary>带一个查询参数的最小 HttpContext，用来喂签名判定。</summary>
    private static IHttpContextAccessor RequestWithSignature(string? token)
    {
        var context = new DefaultHttpContext();
        if (token != null)
        {
            context.Request.QueryString = QueryString.Create(IFileUrlSigner.QueryParameterName, token);
        }
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns(context);
        return accessor.Object;
    }

    /// <summary>只认某一类实体、按固定答案放行的引用判据。</summary>
    private sealed class StubReferenceResolver(string entityType, bool verdict) : IFileReferenceAccessResolver
    {
        public int Calls { get; private set; }

        public bool CanHandle(string type) => type == entityType;

        public Task<bool> CanReadAsync(FileReferenceDescriptor reference, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(verdict);
        }
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

    // ── 签名令牌 ─────────────────────────────────────────────────────────────
    // 存在的理由:<img src> / <a download> 带不了 Authorization 头。没有这条,
    // "私密" 等于 "浏览器渲染不出来",连上传者本人都看不见自己的图。

    [Fact]
    public async Task Anonymous_WithValidSignature_CanReadThatOneFile()
    {
        var file = OwnedFile();
        var sut = CreateSut(
            Anonymous(),
            urlSigner: new TestFileUrlSigner(file.Id),
            httpContextAccessor: RequestWithSignature(TestFileUrlSigner.ValidToken));

        Assert.True(await sut.CanReadAsync(file));
    }

    [Fact]
    public async Task Signature_ForAnotherFile_DoesNotUnlockThisOne()
    {
        // 令牌把 fileId 算进签名载荷,所以 A 的令牌换不到 B。
        var other = Guid.NewGuid();
        var sut = CreateSut(
            Anonymous(),
            urlSigner: new TestFileUrlSigner(other),
            httpContextAccessor: RequestWithSignature(TestFileUrlSigner.ValidToken));

        Assert.False(await sut.CanReadAsync(OwnedFile()));
    }

    [Fact]
    public async Task Signature_DoesNotGrantWrite()
    {
        // 令牌是给浏览器渲染用的,不是授权凭据 —— 拿到一张图的渲染链接
        // 不该能删掉那张图。
        var file = OwnedFile();
        var sut = CreateSut(
            SignedInAs(StrangerId),
            permissionChecker: Grants(),
            urlSigner: new TestFileUrlSigner(file.Id),
            httpContextAccessor: RequestWithSignature(TestFileUrlSigner.ValidToken));

        Assert.True(await sut.CanReadAsync(file));
        Assert.False(await sut.CanWriteAsync(file));
    }

    [Fact]
    public async Task Signature_DoesNotLetTheTokenRenewItself()
    {
        // 一个 10 分钟的渲染凭据若能在到期前换一张新的,TTL 对任何已登录的持有者
        // 就形同虚设 —— 而 TTL 是这套机制唯一的止损面。
        var file = OwnedFile();
        var sut = CreateSut(
            SignedInAs(StrangerId),
            permissionChecker: Grants(),
            urlSigner: new TestFileUrlSigner(file.Id),
            httpContextAccessor: RequestWithSignature(TestFileUrlSigner.ValidToken));

        Assert.True(await sut.CanReadAsync(file));
        Assert.False(await sut.CanMintAccessTokenAsync(file));
    }

    [Fact]
    public async Task Minting_StillFollowsEveryOtherJudgement()
    {
        // 排除的只有签名那一条:归属 / 权限码 / 引用判据照常放行,否则合法用户
        // 会连自己的文件都签发不了。
        var file = OwnedFile();
        Assert.True(await CreateSut(SignedInAs(OwnerId)).CanMintAccessTokenAsync(file));
        Assert.True(await CreateSut(SignedInAs(StrangerId), permissionChecker: Grants(StoragePermissionNames.FileView))
            .CanMintAccessTokenAsync(file));
        Assert.True(await CreateSut(Anonymous()).CanMintAccessTokenAsync(OwnedFile(isPublic: true)));

        var reference = new FileReference
        {
            FileId = file.Id, EntityType = "ChatMessage", EntityId = Guid.NewGuid(), FieldName = "FileId"
        };
        var byReference = CreateSut(
            SignedInAs(StrangerId),
            permissionChecker: Grants(),
            references: [reference],
            referenceResolvers: [new StubReferenceResolver("ChatMessage", verdict: true)]);
        Assert.True(await byReference.CanMintAccessTokenAsync(file));
    }

    [Fact]
    public async Task NoSignatureInRequest_ChangesNothing()
    {
        var sut = CreateSut(
            Anonymous(),
            urlSigner: new TestFileUrlSigner(),
            httpContextAccessor: RequestWithSignature(null));

        Assert.False(await sut.CanReadAsync(OwnedFile()));
    }

    // ── 引用判据 ─────────────────────────────────────────────────────────────
    // 聊天图片的接收方既不是创建者也没有 storage.file.view,但他本来就该看得见。

    [Fact]
    public async Task Stranger_CanRead_WhenAReferenceResolverAllowsIt()
    {
        var file = OwnedFile();
        var reference = new FileReference
        {
            FileId = file.Id, EntityType = "ChatMessage", EntityId = Guid.NewGuid(), FieldName = "FileId"
        };
        var sut = CreateSut(
            SignedInAs(StrangerId),
            permissionChecker: Grants(),
            references: [reference],
            referenceResolvers: [new StubReferenceResolver("ChatMessage", verdict: true)]);

        Assert.True(await sut.CanReadAsync(file));
        // 放行读不等于放行改:附件的查看者不该能删掉别人挂上去的凭据。
        Assert.False(await sut.CanWriteAsync(file));
    }

    [Fact]
    public async Task ReferenceResolver_SayingNo_DoesNotGrantAccess()
    {
        var file = OwnedFile();
        var reference = new FileReference
        {
            FileId = file.Id, EntityType = "ChatMessage", EntityId = Guid.NewGuid(), FieldName = "FileId"
        };
        var sut = CreateSut(
            SignedInAs(StrangerId),
            permissionChecker: Grants(),
            references: [reference],
            referenceResolvers: [new StubReferenceResolver("ChatMessage", verdict: false)]);

        Assert.False(await sut.CanReadAsync(file));
    }

    [Fact]
    public async Task ReferenceResolver_IsNotAskedAboutEntityTypesItDoesNotOwn()
    {
        var file = OwnedFile();
        var reference = new FileReference
        {
            FileId = file.Id, EntityType = "Invoice", EntityId = Guid.NewGuid(), FieldName = "FileId"
        };
        var resolver = new StubReferenceResolver("ChatMessage", verdict: true);
        var sut = CreateSut(
            SignedInAs(StrangerId),
            permissionChecker: Grants(),
            references: [reference],
            referenceResolvers: [resolver]);

        Assert.False(await sut.CanReadAsync(file));
        Assert.Equal(0, resolver.Calls);
    }

    [Fact]
    public async Task ReferenceVerdict_IsResolvedOncePerFile()
    {
        // 列表页会对同一批文件反复问同一个问题,而这条判据是唯一要查库的。
        var file = OwnedFile();
        var reference = new FileReference
        {
            FileId = file.Id, EntityType = "ChatMessage", EntityId = Guid.NewGuid(), FieldName = "FileId"
        };
        var resolver = new StubReferenceResolver("ChatMessage", verdict: true);
        var sut = CreateSut(
            SignedInAs(StrangerId),
            permissionChecker: Grants(),
            references: [reference],
            referenceResolvers: [resolver]);

        Assert.True(await sut.CanReadAsync(file));
        Assert.True(await sut.CanReadAsync(file));
        Assert.Equal(1, resolver.Calls);
    }

    [Fact]
    public async Task WithNoResolversRegistered_TheReferenceCheckIsSkippedEntirely()
    {
        // 没注册解析器时一次查询都不该发 —— 这条判据对绝大多数部署是纯开销。
        var repo = new Mock<IRepository<FileReference, Guid>>(MockBehavior.Strict);
        var monitor = new Mock<IOptionsMonitor<StorageOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(new StorageOptions());

        var sut = new FileAccessAuthorizer(
            SignedInAs(StrangerId), monitor.Object, repo.Object, [], new FileAccessGrantContext(), Grants());

        Assert.False(await sut.CanReadAsync(OwnedFile()));
        repo.VerifyNoOtherCalls();
    }
}
