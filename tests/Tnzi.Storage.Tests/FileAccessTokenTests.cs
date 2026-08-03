namespace Tnzi.Storage.Tests;

/// <summary>
/// 令牌签发路径。守的是「签发即授权」这条:令牌绕过 Authorization 头,所以只有此刻
/// 确实读得了这个文件的人才配拿到它 —— 否则等于把 1aa59e10 堵的洞换个入口重新打开。
/// </summary>
public class FileAccessTokenTests
{
    private readonly Mock<IRepository<FileRecord, Guid>> _files = new();
    private readonly Mock<IRepository<FileReference, Guid>> _references = new();
    private readonly Mock<IFileStorage> _storage = new();
    private readonly Mock<IServiceProvider> _serviceProvider = new();
    private readonly StorageOptions _options = new();

    public FileAccessTokenTests()
    {
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        _serviceProvider.Setup(sp => sp.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);
    }

    private FileStorageService CreateSut(IFileAccessAuthorizer authorizer)
    {
        var monitor = new Mock<IOptionsMonitor<StorageOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(_options);
        return new FileStorageService(
            _files.Object,
            _references.Object,
            _storage.Object,
            monitor.Object,
            authorizer,
            TestPublicFileFieldResolver.Empty(),
            new TestFileUrlSigner(),
            _serviceProvider.Object);
    }

    private static FileRecord Record(Guid id) => new() { Id = id, FileName = "photo.jpg" };

    [Fact]
    public async Task AReadableFile_YieldsAToken()
    {
        var id = Guid.NewGuid();
        _files.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(Record(id));
        var sut = CreateSut(TestFileAccessAuthorizer.AllowAll());

        var result = await sut.CreateAccessTokenAsync(id);

        Assert.True(result.Succeeded);
        Assert.Equal(id, result.Data!.FileId);
        Assert.NotEmpty(result.Data.Token);
        Assert.True(result.Data.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task AFileTheCallerCannotRead_YieldsNoToken()
    {
        // 且以 404 掩盖存在性,与其它读路径一致 —— 403 会告诉调用者这个 id 上确实有东西。
        var id = Guid.NewGuid();
        _files.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(Record(id));
        var sut = CreateSut(TestFileAccessAuthorizer.DenyAll());

        var result = await sut.CreateAccessTokenAsync(id);

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
    }

    [Fact]
    public async Task AMissingFile_YieldsNoToken()
    {
        var id = Guid.NewGuid();
        _files.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((FileRecord?)null);
        var sut = CreateSut(TestFileAccessAuthorizer.AllowAll());

        var result = await sut.CreateAccessTokenAsync(id);

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
    }

    [Fact]
    public async Task TheDefaultTtlComesFromOptions()
    {
        var id = Guid.NewGuid();
        _files.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(Record(id));
        _options.SignedUrlTtlSeconds = 120;
        var sut = CreateSut(TestFileAccessAuthorizer.AllowAll());

        var result = await sut.CreateAccessTokenAsync(id);

        Assert.InRange(result.Data!.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(60), DateTimeOffset.UtcNow.AddSeconds(180));
    }

    [Fact]
    public async Task BatchMinting_OmitsFilesTheCallerCannotRead()
    {
        // 一页图片里混进一个不该看的 id 时,其余图片仍应正常显示;省略本身也不透露
        // 那个 id 上是否真有文件。
        var readable = Guid.NewGuid();
        var forbidden = Guid.NewGuid();
        _files.Setup(r => r.ToListAsync(It.IsAny<Expression<Func<FileRecord, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Record(readable), Record(forbidden)]);
        var sut = CreateSut(TestFileAccessAuthorizer.ReadableOnly(readable));

        var result = await sut.CreateAccessTokensAsync([readable, forbidden]);

        Assert.True(result.Succeeded);
        var token = Assert.Single(result.Data!);
        Assert.Equal(readable, token.FileId);
    }

    [Fact]
    public async Task TheRequestedTtl_CannotExceedTheConfiguredCeiling()
    {
        // 没有这条上限,`?expiresInSeconds=999999999` 就能把几分钟的凭据变成几十年的 ——
        // 而 TTL 是这套机制唯一的止损面(URL 会进浏览器历史,且失去权限后令牌仍有效)。
        var id = Guid.NewGuid();
        _files.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(Record(id));
        _options.SignedUrlTtlSeconds = 600;
        var sut = CreateSut(TestFileAccessAuthorizer.AllowAll());

        var result = await sut.CreateAccessTokenAsync(id, expiresInSeconds: 999_999_999);

        Assert.True(result.Data!.ExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(601));
    }

    [Fact]
    public async Task AShorterTtl_IsStillHonoured()
    {
        var id = Guid.NewGuid();
        _files.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(Record(id));
        _options.SignedUrlTtlSeconds = 600;
        var sut = CreateSut(TestFileAccessAuthorizer.AllowAll());

        var result = await sut.CreateAccessTokenAsync(id, expiresInSeconds: 60);

        Assert.True(result.Data!.ExpiresAt < DateTimeOffset.UtcNow.AddSeconds(120));
    }

    [Fact]
    public async Task MintingUsesTheMintCheck_NotThePlainReadCheck()
    {
        // 签发不认「签名令牌」那条判据,否则一个 10 分钟的渲染凭据就能在到期前
        // 换一张新的、无限续期。授权器用两个不同的方法表达这件事,服务必须调对那个。
        var id = Guid.NewGuid();
        _files.Setup(r => r.GetAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(Record(id));
        var authorizer = new Mock<IFileAccessAuthorizer>();
        authorizer.Setup(a => a.CanReadAsync(It.IsAny<FileRecord>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        authorizer.Setup(a => a.CanMintAccessTokenAsync(It.IsAny<FileRecord>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var sut = CreateSut(authorizer.Object);

        var result = await sut.CreateAccessTokenAsync(id);

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
    }

    [Fact]
    public async Task BatchMinting_RejectsAnOversizedRequest()
    {
        // 上限而不是静默截断:截断会让调用方以为"这些 id 都不可读"。
        // 每个 id 都可能触发一次引用表查询,所以这条上限也是放大攻击的闸门。
        var sut = CreateSut(TestFileAccessAuthorizer.AllowAll());
        var tooMany = Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()).ToList();

        var result = await sut.CreateAccessTokensAsync(tooMany);

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        _files.Verify(r => r.ToListAsync(It.IsAny<Expression<Func<FileRecord, bool>>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BatchMinting_WithNoIds_ShortCircuits()
    {
        var sut = CreateSut(TestFileAccessAuthorizer.AllowAll());

        var result = await sut.CreateAccessTokensAsync([]);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Data!);
        _files.Verify(r => r.ToListAsync(It.IsAny<Expression<Func<FileRecord, bool>>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
