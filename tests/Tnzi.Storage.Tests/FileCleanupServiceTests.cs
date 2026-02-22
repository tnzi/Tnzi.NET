
namespace Tnzi.Storage.Tests;

/// <summary>
/// FileCleanupService 单元测试
/// </summary>
public class FileCleanupServiceTests
{
    private readonly Mock<IRepository<FileRecord, Guid>> _mockFileRepository;
    private readonly Mock<IRepository<FileReference, Guid>> _mockReferenceRepository;
    private readonly Mock<IFileStorage> _mockStorage;
    private readonly Mock<ILogger<FileCleanupService>> _mockLogger;
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly StorageOptions _options;

    public FileCleanupServiceTests()
    {
        _mockFileRepository = new Mock<IRepository<FileRecord, Guid>>();
        _mockReferenceRepository = new Mock<IRepository<FileReference, Guid>>();
        _mockStorage = new Mock<IFileStorage>();
        _mockLogger = new Mock<ILogger<FileCleanupService>>();
        _mockServiceProvider = new Mock<IServiceProvider>();
        _options = new StorageOptions();

        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(_mockLogger.Object);
        _mockServiceProvider.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactoryMock.Object);
    }

    private FileCleanupService CreateService()
    {
        var optionsWrapper = Microsoft.Extensions.Options.Options.Create(_options);
        return new FileCleanupService(
            _mockFileRepository.Object,
            _mockReferenceRepository.Object,
            _mockStorage.Object,
            optionsWrapper,
            _mockServiceProvider.Object);
    }

    [Fact]
    public void CleanupResult_TotalDeleted_ShouldSumAllCategories()
    {
        // Arrange
        var result = new CleanupResult
        {
            TemporaryFilesDeleted = 5,
            OrphanFilesDeleted = 3,
            OrphanReferencesDeleted = 2
        };

        // Act & Assert
        Assert.Equal(10, result.TotalDeleted);
    }

    [Fact]
    public void CleanupResult_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var result = new CleanupResult();

        // Assert
        Assert.True(result.Success);
        Assert.Empty(result.Errors);
        Assert.Equal(0, result.TemporaryFilesDeleted);
        Assert.Equal(0, result.OrphanFilesDeleted);
        Assert.Equal(0, result.OrphanReferencesDeleted);
    }

    [Fact]
    public void CreateService_ShouldNotThrow()
    {
        // Act
        var service = CreateService();

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void CleanupOptions_EnableOrphanFileCleanup_DefaultsToTrue()
    {
        // Assert
        Assert.True(_options.Cleanup.EnableOrphanFileCleanup);
    }

    [Fact]
    public void CleanupOptions_EnableOrphanReferenceCleanup_DefaultsToFalse()
    {
        // Assert
        Assert.False(_options.Cleanup.EnableOrphanReferenceCleanup);
    }

    [Fact]
    public async Task CleanupOrphanReferencesAsync_ReturnsZero_WhenNotImplemented()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.CleanupOrphanReferencesAsync();

        // Assert - 此功能暂未完整实现，总是返回0
        Assert.Equal(0, result);
    }
}