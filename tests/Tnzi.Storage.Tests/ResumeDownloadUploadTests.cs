
namespace Tnzi.Storage.Tests;

/// <summary>
/// 断点续传和断点下载单元测试
/// </summary>
public class ResumeDownloadUploadTests
{
    private readonly Mock<IRepository<FileRecord, Guid>> _mockFileRepository;
    private readonly Mock<IRepository<FileReference, Guid>> _mockReferenceRepository;
    private readonly Mock<IFileStorage> _mockStorage;
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly StorageOptions _options;

    public ResumeDownloadUploadTests()
    {
        _mockFileRepository = new Mock<IRepository<FileRecord, Guid>>();
        _mockReferenceRepository = new Mock<IRepository<FileReference, Guid>>();
        _mockStorage = new Mock<IFileStorage>();
        _mockServiceProvider = new Mock<IServiceProvider>();

        // 设置 ILoggerFactory（ApplicationService.Logger 需要）
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);
        _mockServiceProvider.Setup(sp => sp.GetService(typeof(ILoggerFactory)))
            .Returns(loggerFactory.Object);

        // 会话归属判定要问「当前用户是谁」——这份夹具此前从不注册它，
        // 于是上传会话的四个端点从来没有在「有人在操作」的语境下被测过。
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(u => u.Id).Returns(TestHelper.DefaultTestUserId);
        currentUser.Setup(u => u.IsAuthenticated).Returns(true);
        _mockServiceProvider.Setup(sp => sp.GetService(typeof(ICurrentUser)))
            .Returns(currentUser.Object);

        _options = new StorageOptions();
    }

    private FileStorageService CreateStorageService()
    {
        var optionsMonitor = new Mock<IOptionsMonitor<StorageOptions>>();
        optionsMonitor.Setup(x => x.CurrentValue).Returns(_options);
        return new FileStorageService(
            _mockFileRepository.Object,
            _mockReferenceRepository.Object,
            _mockStorage.Object,
            optionsMonitor.Object,
            TestFileAccessAuthorizer.AllowAll(),
            TestPublicFileFieldResolver.Empty(),
            new TestFileUrlSigner(),
            _mockServiceProvider.Object,
            new UploadGuard(optionsMonitor.Object));
    }

    #region 断点下载测试

    [Fact]
    public async Task GetRangeAsync_WithRange_ReturnsPartialContent()
    {
        // Arrange
        var service = CreateStorageService();
        var fileId = Guid.NewGuid();
        var fileRecord = new FileRecord
        {
            Id = fileId,
            FileName = "test.jpg",
            Size = 1000,
            Path = "path/to/file.jpg"
        };

        var testData = new byte[1000];
        for (int i = 0; i < 1000; i++)
        {
            testData[i] = (byte)(i % 256);
        }

        var rangeStream = new MemoryStream(testData, 100, 200); // 从位置100开始，长度200

        _mockFileRepository.Setup(r => r.GetAsync(fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileRecord);
        _mockStorage.Setup(s => s.DownloadRangeAsync("path/to/file.jpg", 100, 299))
            .ReturnsAsync((rangeStream, 100L, 299L, 1000L));

        // Act
        var result = await service.GetRangeAsync(fileId, 100, 299);
        var (stream, start, end, totalLength) = result.Data;

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(100, start);
        Assert.Equal(299, end);
        Assert.Equal(1000, totalLength);
        Assert.NotNull(stream);
    }

    [Fact]
    public async Task GetRangeAsync_WithoutRange_ReturnsFullFile()
    {
        // Arrange
        var service = CreateStorageService();
        var fileId = Guid.NewGuid();
        var fileRecord = new FileRecord
        {
            Id = fileId,
            FileName = "test.jpg",
            Size = 1000,
            Path = "path/to/file.jpg"
        };

        var fullStream = new MemoryStream(new byte[1000]);

        _mockFileRepository.Setup(r => r.GetAsync(fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fileRecord);
        _mockStorage.Setup(s => s.DownloadRangeAsync("path/to/file.jpg", null, null))
            .ReturnsAsync((fullStream, 0L, 999L, 1000L));

        // Act
        var result = await service.GetRangeAsync(fileId);
        var (stream, start, end, totalLength) = result.Data;

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(0, start);
        Assert.Equal(999, end);
        Assert.Equal(1000, totalLength);
        Assert.NotNull(stream);
    }

    [Fact]
    public async Task GetRangeAsync_ThrowsException_WhenFileNotFound()
    {
        // Arrange
        var service = CreateStorageService();
        var fileId = Guid.NewGuid();

        _mockFileRepository.Setup(r => r.GetAsync(fileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileRecord?)null);

        // Act
        var result = await service.GetRangeAsync(fileId);

        // Assert
        Assert.False(result.Succeeded);
    }

    #endregion

}
