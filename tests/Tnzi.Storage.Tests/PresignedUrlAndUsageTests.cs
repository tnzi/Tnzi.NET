
namespace Tnzi.Storage.Tests;

/// <summary>
/// Tests for presigned URL generation and user storage usage statistics
/// </summary>
public class PresignedUrlAndUsageTests
{
    private readonly Mock<IRepository<FileRecord, Guid>> _mockFileRepository;
    private readonly Mock<IRepository<FileReference, Guid>> _mockReferenceRepository;
    private readonly Mock<IFileStorage> _mockStorage;
    private readonly StorageOptions _options;
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<ILoggerFactory> _mockLoggerFactory;

    public PresignedUrlAndUsageTests()
    {
        _mockFileRepository = new Mock<IRepository<FileRecord, Guid>>();
        _mockReferenceRepository = new Mock<IRepository<FileReference, Guid>>();
        _mockStorage = new Mock<IFileStorage>();
        _options = new StorageOptions();
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockLoggerFactory = new Mock<ILoggerFactory>();

        _mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);
        _mockServiceProvider.Setup(sp => sp.GetService(typeof(ILoggerFactory)))
            .Returns(_mockLoggerFactory.Object);
    }

    private FileStorageService CreateStorageService(IFileAccessAuthorizer? authorizer = null)
    {
        var optionsMonitor = new Mock<IOptionsMonitor<StorageOptions>>();
        optionsMonitor.Setup(x => x.CurrentValue).Returns(_options);
        return new FileStorageService(
            _mockFileRepository.Object,
            _mockReferenceRepository.Object,
            _mockStorage.Object,
            optionsMonitor.Object,
            authorizer ?? TestFileAccessAuthorizer.AllowAll(),
            TestPublicFileFieldResolver.Empty(),
            new TestFileUrlSigner(),
            _mockServiceProvider.Object,
            new UploadGuard(optionsMonitor.Object),
            new FileThumbnailGenerator(_mockStorage.Object, optionsMonitor.Object));
    }

    #region Presigned URL Tests

    [Fact]
    public async Task GetPresignedUrlAsync_WithNonExistingFile_ReturnsFail()
    {
        // Arrange
        var service = CreateStorageService();
        var fileId = Guid.NewGuid();
        _mockFileRepository.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileRecord?)null);

        // Act
        var result = await service.GetPresignedUrlAsync(fileId);

        // Assert
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task GetPresignedUrlAsync_WithExistingFile_ReturnsUrl()
    {
        // Arrange
        var service = CreateStorageService();
        var fileId = Guid.NewGuid();
        var fileRecord = new FileRecord
        {
            Id = fileId,
            FileName = "test.jpg",
            Path = "uploads/test.jpg",
            Extension = ".jpg",
            Size = 1024
        };

        _mockFileRepository.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileRecord);

        _mockStorage.Setup(s => s.GetPresignedUrlAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync("https://s3.example.com/presigned-url");

        // Act
        var result = await service.GetPresignedUrlAsync(fileId);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Contains("presigned-url", result.Data);
    }

    [Fact]
    public async Task GetPresignedUrlAsync_WhenProviderReturnsNull_ReturnsFallbackUrl()
    {
        // Arrange
        var service = CreateStorageService();
        var fileId = Guid.NewGuid();
        var fileRecord = new FileRecord
        {
            Id = fileId,
            FileName = "test.jpg",
            Path = "uploads/test.jpg",
            Extension = ".jpg",
            Size = 1024
        };

        _mockFileRepository.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileRecord);

        // Provider returns null (e.g., local storage)
        _mockStorage.Setup(s => s.GetPresignedUrlAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync((string?)null);

        // Act
        var result = await service.GetPresignedUrlAsync(fileId);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        // Should return a fallback URL containing the file ID
        Assert.Contains(fileId.ToString(), result.Data);
    }

    [Fact]
    public async Task GetPresignedUrlAsync_WithInvalidExpiration_ReturnsFail()
    {
        // Arrange
        var service = CreateStorageService();
        var fileId = Guid.NewGuid();

        // Act
        var result = await service.GetPresignedUrlAsync(fileId, expiresInSeconds: -1);

        // Assert
        Assert.False(result.Succeeded);
    }

    #endregion

    #region Presigned URL verb authorization

    // 预签名 URL 一旦签出就完全绕过应用层：GET 是读凭据，PUT 是**写**凭据（三个云 provider
    // 都按动词签出可覆盖对象字节的 URL）。签发前的判据必须与凭据的能力对齐：
    // 读凭据看 CanRead，写凭据看 CanWrite；此前两者都只看 CanRead，于是只持 storage.file.view
    // 的人能换到一条可以改写任意文件字节的直传 URL，而 UploadGuard 与 .update 码一次都没被问过。

    private FileRecord SetupExistingRecord()
    {
        var record = new FileRecord
        {
            Id = Guid.NewGuid(),
            FileName = "test.jpg",
            Path = "uploads/test.jpg",
            Extension = ".jpg",
            Size = 1024
        };
        _mockFileRepository.Setup(r => r.GetAsync(record.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        _mockStorage.Setup(s => s.GetPresignedUrlAsync(record.Path!, It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync("https://s3.example.com/presigned-url");
        return record;
    }

    [Fact]
    public async Task GetPresignedUrlAsync_Put_WithoutWritePermission_Returns404AndNeverAsksProvider()
    {
        var service = CreateStorageService(TestFileAccessAuthorizer.ReadOnly());
        var record = SetupExistingRecord();

        var result = await service.GetPresignedUrlAsync(record.Id, 3600, "PUT");

        Assert.False(result.Succeeded);
        // 与其它写路径同口径：以 404 掩盖存在性，不向只读调用方承认这个 id 上有东西。
        Assert.Equal(404, result.Code);
        _mockStorage.Verify(s => s.GetPresignedUrlAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetPresignedUrlAsync_Put_WithWritePermission_ForwardsPutToProvider()
    {
        var service = CreateStorageService(TestFileAccessAuthorizer.AllowAll());
        var record = SetupExistingRecord();

        var result = await service.GetPresignedUrlAsync(record.Id, 3600, "put");

        Assert.True(result.Succeeded);
        // 动词规范化后再交给 provider：provider 各自做的是不区分大小写的比较，
        // 但日志与调用方看到的应是同一个形态。
        _mockStorage.Verify(s => s.GetPresignedUrlAsync(record.Path!, 3600, "PUT"), Times.Once);
    }

    [Fact]
    public async Task GetPresignedUrlAsync_Get_WithReadOnlyPermission_Succeeds()
    {
        var service = CreateStorageService(TestFileAccessAuthorizer.ReadOnly());
        var record = SetupExistingRecord();

        var result = await service.GetPresignedUrlAsync(record.Id, 3600, "GET");

        Assert.True(result.Succeeded);
        _mockStorage.Verify(s => s.GetPresignedUrlAsync(record.Path!, 3600, "GET"), Times.Once);
    }

    [Fact]
    public async Task GetPresignedUrlAsync_Get_WithoutReadPermission_Returns404()
    {
        var service = CreateStorageService(TestFileAccessAuthorizer.DenyAll());
        var record = SetupExistingRecord();

        var result = await service.GetPresignedUrlAsync(record.Id, 3600, "GET");

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
        _mockStorage.Verify(s => s.GetPresignedUrlAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetPresignedUrlAsync_Get_WithReadOnlyRequestCredential_Returns404AndNeverAsksProvider()
    {
        // 一条 presigned GET URL 是访问令牌的同族：签出后绕过本 API、无需 Authorization 头、
        // 到期才失效（云端的过期还不受 SignedUrlTtlSeconds 约束）。签发它必须走签发判据 ——
        // 否则持一条 10 分钟的 ?sig= 渲染凭据或一条限次数的分享链接的人，能在这里换到一条
        // 有效期由自己填的云端 URL（或本地回退里一条可以喂回同一入口无限续期的新签名链接）。
        var service = CreateStorageService(TestFileAccessAuthorizer.ReadableButNotMintable());
        var record = SetupExistingRecord();

        var result = await service.GetPresignedUrlAsync(record.Id, 3600, "GET");

        Assert.False(result.Succeeded);
        Assert.Equal(404, result.Code);
        _mockStorage.Verify(s => s.GetPresignedUrlAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData("DELETE")]
    [InlineData("POST")]
    [InlineData("")]
    [InlineData("  ")]
    public async Task GetPresignedUrlAsync_UnknownVerb_Returns400AndNeverAsksProvider(string verb)
    {
        // 未知动词不能落到「当 GET 处理」：provider 对非 PUT 一律签 GET，于是一个拼错的
        // 动词会安静地拿到一条读凭据 —— 那与调用方以为自己要到的东西不一样。
        var service = CreateStorageService(TestFileAccessAuthorizer.AllowAll());
        var record = SetupExistingRecord();

        var result = await service.GetPresignedUrlAsync(record.Id, 3600, verb);

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Code);
        _mockStorage.Verify(s => s.GetPresignedUrlAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetPresignedUrlAsync_Put_OnLocalProvider_DoesNotFallBackToSignedReadUrl()
    {
        // 本地 provider 没有直传：签名令牌回退是一条**只读**链接，把它当作 PUT 的答案交出去
        // 就是「要写凭据、拿到读凭据、状态 200」。
        var service = CreateStorageService(TestFileAccessAuthorizer.AllowAll());
        var record = SetupExistingRecord();
        _mockStorage.Setup(s => s.GetPresignedUrlAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
            .ReturnsAsync((string?)null);

        var result = await service.GetPresignedUrlAsync(record.Id, 3600, "PUT");

        Assert.False(result.Succeeded);
        Assert.Equal(501, result.Code);
    }

    #endregion

    #region IFileStorage Default Interface Method Tests

    [Fact]
    public async Task IFileStorage_GetPresignedUrlAsync_DefaultReturnsNull()
    {
        // Arrange - test that the default interface method returns null
        var mockStorage = new Mock<IFileStorage>();
        mockStorage.Setup(s => s.GetPresignedUrlAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
            .CallBase(); // Call the default interface method

        // Act
        var result = await mockStorage.Object.GetPresignedUrlAsync("test.jpg");

        // Assert
        Assert.Null(result);
    }

    #endregion
}
