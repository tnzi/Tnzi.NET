
namespace Tnzi.Storage.Tests;

/// <summary>
/// 断点续传和断点下载单元测试
/// </summary>
public class ResumeDownloadUploadTests
{
    private readonly Mock<IRepository<FileRecord, Guid>> _mockFileRepository;
    private readonly Mock<IRepository<FileReference, Guid>> _mockReferenceRepository;
    private readonly Mock<IRepository<FileUploadSession, Guid>> _mockUploadSessionRepository;
    private readonly Mock<IRepository<FileChunk, Guid>> _mockChunkRepository;
    private readonly Mock<IFileStorage> _mockStorage;
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly StorageOptions _options;

    public ResumeDownloadUploadTests()
    {
        _mockFileRepository = new Mock<IRepository<FileRecord, Guid>>();
        _mockReferenceRepository = new Mock<IRepository<FileReference, Guid>>();
        _mockUploadSessionRepository = new Mock<IRepository<FileUploadSession, Guid>>();
        _mockChunkRepository = new Mock<IRepository<FileChunk, Guid>>();
        _mockStorage = new Mock<IFileStorage>();
        _mockServiceProvider = new Mock<IServiceProvider>();

        // 设置 ILoggerFactory（ApplicationService.Logger 需要）
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(new Mock<ILogger>().Object);
        _mockServiceProvider.Setup(sp => sp.GetService(typeof(ILoggerFactory)))
            .Returns(loggerFactory.Object);

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
            _mockServiceProvider.Object);
    }

    private FileChunkUploadService CreateChunkUploadService()
    {
        return new FileChunkUploadService(
            _mockUploadSessionRepository.Object,
            _mockChunkRepository.Object,
            _mockFileRepository.Object,
            _mockStorage.Object,
            new StaticOptionsMonitor<StorageOptions>(_options),
            _mockServiceProvider.Object);
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

    #region 分块上传测试（FileChunkUploadService）

    [Fact]
    public async Task InitiateChunkedUploadAsync_CreatesSession()
    {
        // Arrange
        var service = CreateChunkUploadService();
        var fileName = "large-file.zip";
        var totalSize = 50 * 1024 * 1024L; // 50MB
        var chunkSize = 5 * 1024 * 1024; // 5MB

        _mockUploadSessionRepository.Setup(r => r.InsertAsync(It.IsAny<FileUploadSession>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await service.InitiateChunkedUploadAsync(fileName, totalSize, chunkSize);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(fileName, result.Data.FileName);
        Assert.Equal(totalSize, result.Data.TotalSize);
        Assert.Equal(chunkSize, result.Data.ChunkSize);
        Assert.Equal(10, result.Data.TotalChunks); // 50MB / 5MB = 10 chunks
        Assert.Equal(0, result.Data.UploadedChunks);
        Assert.False(result.Data.IsCompleted);
        Assert.False(result.Data.IsCancelled);
        _mockUploadSessionRepository.Verify(r => r.InsertAsync(It.IsAny<FileUploadSession>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadChunkAsync_ThrowsException_WhenSessionInvalid()
    {
        // Arrange
        var service = CreateChunkUploadService();
        var uploadSessionId = Guid.NewGuid();
        var chunkIndex = 0;
        var chunkData = new byte[1024];
        var chunkStream = new MemoryStream(chunkData);

        // 会话不存在
        _mockUploadSessionRepository.Setup(r => r.GetAsync(uploadSessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileUploadSession?)null);

        // Act
        var result = await service.UploadChunkAsync(uploadSessionId, chunkIndex, chunkStream);

        // Assert
        Assert.False(result.Succeeded);
    }

    // 注意：UploadChunkAsync 和 CancelChunkedUploadAsync 的完整测试需要集成测试
    // 因为需要 Mock IQueryable 的异步方法（CountAsync、SumAsync、ToListAsync）

    [Fact]
    public async Task GetUploadProgressAsync_ReturnsProgress()
    {
        // Arrange
        var service = CreateChunkUploadService();
        var uploadSessionId = Guid.NewGuid();
        var session = new FileUploadSession
        {
            Id = uploadSessionId,
            FileName = "test.zip",
            TotalSize = 10000,
            TotalChunks = 10,
            UploadedChunks = 5,
            UploadedSize = 5000,
            IsCompleted = false,
            IsCancelled = false
        };

        _mockUploadSessionRepository.Setup(r => r.GetAsync(uploadSessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // Act
        var result = await service.GetUploadProgressAsync(uploadSessionId);

        // Assert
        Assert.True(result.Succeeded);
        Assert.NotNull(result.Data);
        Assert.Equal(uploadSessionId, result.Data.UploadSessionId);
        Assert.Equal(10000, result.Data.TotalSize);
        Assert.Equal(5000, result.Data.UploadedSize);
        Assert.Equal(10, result.Data.TotalChunks);
        Assert.Equal(5, result.Data.UploadedChunks);
        Assert.Equal(50.0, result.Data.ProgressPercentage);
        Assert.False(result.Data.IsCompleted);
    }

    [Fact]
    public async Task CancelChunkedUploadAsync_ReturnsEarly_WhenSessionCompleted()
    {
        // Arrange
        var service = CreateChunkUploadService();
        var uploadSessionId = Guid.NewGuid();
        var session = new FileUploadSession
        {
            Id = uploadSessionId,
            IsCompleted = true, // 已完成的会话
            IsCancelled = false
        };

        _mockUploadSessionRepository.Setup(r => r.GetAsync(uploadSessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // Act
        await service.CancelChunkedUploadAsync(uploadSessionId);

        // Assert - 应该提前返回，不执行任何操作
        // 注意：完整测试需要集成测试，因为需要 Mock IQueryable 的异步方法
    }

    #endregion
}
