
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
    public async Task ExecuteAsync_WhenCronExpressionSet_SchedulesByCron()
    {
        // Arrange — Cron 已配置（有效）→ 应按 cron 调度并记录 info（含 "cron expression"）
        var options = new StorageOptions
        {
            Cleanup = new CleanupOptions { Enabled = true, CronExpression = "0 3 * * *", IntervalMinutes = 60 }
        };
        var service = CreateService(options);
        var cts = new CancellationTokenSource();

        // Act
        var task = service.StartAsync(cts.Token);
        await Task.Delay(200);
        cts.Cancel();

        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // 预期的取消异常
        }

        // Assert — 记录了 cron 调度 info（表明 CronExpression 真正生效，而非回退）
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("cron expression")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenInvalidCronExpression_LogsErrorAndFallsBack()
    {
        // Arrange — 非法 Cron → 应记录 error 并回退到 IntervalMinutes
        var options = new StorageOptions
        {
            Cleanup = new CleanupOptions { Enabled = true, CronExpression = "not-a-valid-cron", IntervalMinutes = 60 }
        };
        var service = CreateService(options);
        var cts = new CancellationTokenSource();

        // Act
        var task = service.StartAsync(cts.Token);
        await Task.Delay(200);
        cts.Cancel();

        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // 预期的取消异常
        }

        // Assert — 记录了非法 Cron 的 error
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Invalid Cleanup.CronExpression")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
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