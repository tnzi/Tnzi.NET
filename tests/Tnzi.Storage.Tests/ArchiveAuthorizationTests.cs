namespace Tnzi.Storage.Tests;

/// <summary>
/// 打包 / 解包路径的读权限判定。
/// </summary>
/// <remarks>
/// <para>
/// 「拒绝落服务层、一律 404」那条结论此前只覆盖了 <c>FileStorageService</c> 的六个方法
/// （下载 / 预览 / 缩略图 / 取记录 / 删除 / 改名等），而 compress / decompress 是后来长出来的
/// 两个入口，从头到尾没有调过 <c>IFileAccessAuthorizer</c> —— 不是回归，是同一个洞在新入口上重现。
/// </para>
/// <para>
/// <b>为什么打包等于交出字节</b>：新 zip 的 <c>CreatorId</c> 是调用者自己（框架的审计属性填充
/// 就是这么做的），于是他随后走 <c>IsOwner</c> 分支正常下载 —— 7 条判据链被整体绕过，
/// 只需要知道一个 fileId。解包同理，而且只需要一个 zip 的 id 就能拿到整包内容。
/// </para>
/// </remarks>
public class ArchiveAuthorizationTests
{
    private readonly Mock<IRepository<FileRecord, Guid>> _files = new();
    private readonly Mock<IRepository<FileReference, Guid>> _references = new();
    private readonly Mock<IFileStorage> _storage = new();
    private readonly Mock<IServiceProvider> _serviceProvider = new();
    private readonly StorageOptions _options = new();

    public ArchiveAuthorizationTests()
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

    private static FileRecord Record(Guid id) => new() { Id = id, FileName = $"{id}.pdf", OriginalName = $"{id}.pdf", Path = $"2026/01/01/{id}.pdf" };

    /// <summary>不可读的文件不得被打包 —— 否则它的字节就换个 id 交出去了。</summary>
    [Fact]
    public async Task Compress_NeverReadsAFileTheCallerMayNotRead()
    {
        var foreign = Guid.NewGuid();
        _files.Setup(r => r.GetAsync(foreign, It.IsAny<CancellationToken>())).ReturnsAsync(Record(foreign));

        var sut = CreateSut(TestFileAccessAuthorizer.DenyAll());

        await sut.CompressAsync([foreign], "bundle.zip");

        _storage.Verify(
            s => s.DownloadAsync(It.IsAny<string>()),
            Times.Never,
            "越权文件的字节一次都不该被读出来 —— 打包等于把它们交到调用者手上");
    }

    /// <summary>
    /// 判定是<b>逐个</b>做的：一批里混进一个越权 id，其余仍应正常打包。
    /// </summary>
    /// <remarks>
    /// 越权 id 静默省略而不是让整批失败，与 <c>CreateAccessTokensAsync</c> 同一取舍：
    /// 省略本身不透露那个 id 上是否真有文件（不可读与不存在在观测上不可区分）。
    /// </remarks>
    [Fact]
    public async Task Compress_SkipsOnlyTheUnreadableOnes()
    {
        var mine = Guid.NewGuid();
        var foreign = Guid.NewGuid();
        _files.Setup(r => r.GetAsync(mine, It.IsAny<CancellationToken>())).ReturnsAsync(Record(mine));
        _files.Setup(r => r.GetAsync(foreign, It.IsAny<CancellationToken>())).ReturnsAsync(Record(foreign));
        _storage.Setup(s => s.DownloadAsync(It.IsAny<string>()))
            .ReturnsAsync(() => new MemoryStream([1, 2, 3]));

        var sut = CreateSut(TestFileAccessAuthorizer.ReadableOnly(mine));

        await sut.CompressAsync([mine, foreign], "bundle.zip");

        _storage.Verify(
            s => s.DownloadAsync(It.Is<string>(p => p.Contains(mine.ToString()))),
            Times.Once);
        _storage.Verify(
            s => s.DownloadAsync(It.Is<string>(p => p.Contains(foreign.ToString()))),
            Times.Never);
    }

    /// <summary>解包是单文件操作，与其它单文件操作同形：不可读一律 404，不泄露存在性。</summary>
    [Fact]
    public async Task Decompress_RejectsAnArchiveTheCallerMayNotRead()
    {
        var foreign = Guid.NewGuid();
        _files.Setup(r => r.GetAsync(foreign, It.IsAny<CancellationToken>())).ReturnsAsync(Record(foreign));

        var sut = CreateSut(TestFileAccessAuthorizer.DenyAll());

        var result = await sut.DecompressAsync(foreign);

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
        _storage.Verify(
            s => s.DownloadAsync(It.IsAny<string>()),
            Times.Never);
    }
}
