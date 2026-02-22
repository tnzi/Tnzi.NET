
namespace Tnzi.Storage.Tests;

/// <summary>
/// FileCleanupBackgroundService 单元测试
/// </summary>
public class FileCleanupBackgroundServiceTests
{
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<IServiceScope> _mockScope;
    private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
    private readonly Mock<IFileCleanupService> _mockCleanupService;
    private readonly Mock<ILogger<FileCleanupBackgroundService>> _mockLogger;

    public FileCleanupBackgroundServiceTests()
    {
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockScope = new Mock<IServiceScope>();
        _mockScopeFactory = new Mock<IServiceScopeFactory>();
        _mockCleanupService = new Mock<IFileCleanupService>();
        _mockLogger = new Mock<ILogger<FileCleanupBackgroundService>>();

        // 设置 scope factory
        _mockServiceProvider
            .Setup(x => x.GetService(typeof(IServiceScopeFactory)))
            .Returns(_mockScopeFactory.Object);

        _mockScopeFactory
            .Setup(x => x.CreateScope())
            .Returns(_mockScope.Object);

        _mockScope
            .Setup(x => x.ServiceProvider)
            .Returns(_mockServiceProvider.Object);

        _mockServiceProvider
            .Setup(x => x.GetService(typeof(IFileCleanupService)))
            .Returns(_mockCleanupService.Object);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDisabled_ShouldNotExecuteCleanup()
    {
        // Arrange
        var options = new StorageOptions
        {
            Cleanup = new CleanupOptions { Enabled = false }
        };
        var service = CreateService(options);
        var cts = new CancellationTokenSource();

        // Act
        var task = service.StartAsync(cts.Token);
        await Task.Delay(100); // 给一点时间执行
        cts.Cancel();

        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // 预期的取消异常
        }

        // Assert - 清理服务不应被调用
        _mockCleanupService.Verify(
            x => x.CleanupAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void Constructor_ShouldNotThrow()
    {
        // Arrange
        var options = new StorageOptions();

        // Act & Assert
        var service = CreateService(options);
        Assert.NotNull(service);
    }

    private FileCleanupBackgroundService CreateService(StorageOptions options)
    {
        var optionsWrapper = Microsoft.Extensions.Options.Options.Create(options);
        return new FileCleanupBackgroundService(
            _mockServiceProvider.Object,
            optionsWrapper,
            _mockLogger.Object);
    }
}