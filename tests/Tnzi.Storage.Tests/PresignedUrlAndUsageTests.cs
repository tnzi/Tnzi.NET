
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
