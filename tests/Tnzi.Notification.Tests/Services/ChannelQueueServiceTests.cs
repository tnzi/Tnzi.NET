using Tnzi.Notification.Services.Internal;


namespace Tnzi.Notification.Tests.Services;

public class ChannelQueueServiceTests
{
    private readonly ChannelQueueService _queueService;
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly Mock<ILogger<ChannelQueueService>> _loggerMock;
    private readonly Mock<IOptionsMonitor<NotificationOptions>> _optionsMock;
    private readonly Mock<IServiceScopeFactory> _scopeFactoryMock;
    private readonly Mock<IServiceScope> _scopeMock;

    public ChannelQueueServiceTests()
    {
        _serviceProviderMock = new Mock<IServiceProvider>();
        _loggerMock = new Mock<ILogger<ChannelQueueService>>();
        _optionsMock = new Mock<IOptionsMonitor<NotificationOptions>>();
        _scopeFactoryMock = new Mock<IServiceScopeFactory>();
        _scopeMock = new Mock<IServiceScope>();

        _optionsMock.Setup(x => x.CurrentValue).Returns(new NotificationOptions
        {
            Queue = new QueueOptions { QueueCapacity = 10000 }
        });

        // Setup Scope Factory
        _serviceProviderMock.Setup(x => x.GetService(typeof(IServiceScopeFactory)))
            .Returns(_scopeFactoryMock.Object);
        _scopeFactoryMock.Setup(x => x.CreateScope())
            .Returns(_scopeMock.Object);
        _scopeMock.Setup(x => x.ServiceProvider)
            .Returns(_serviceProviderMock.Object);

        _queueService = new ChannelQueueService(_serviceProviderMock.Object, _loggerMock.Object, _optionsMock.Object);
    }

    [Fact]
    public async Task EnqueueAsync_Should_Accept_WorkItem()
    {
        // Arrange
        bool executed = false;
        var workItem = new NotificationWorkItem(null, (sp, ct) =>
        {
            executed = true;
            return Task.CompletedTask;
        });

        // Act
        await _queueService.EnqueueAsync(workItem);
        
        // Start processing background task
        using var cts = new CancellationTokenSource();
        var task = _queueService.StartAsync(cts.Token);
        
        // Wait a bit for the work item to be processed
        // We poll until executed is true or timeout
        int retry = 0;
        while (!executed && retry < 10)
        {
            await Task.Delay(50);
            retry++;
        }
        
        cts.Cancel();
        try 
        {
            await task; 
        }
        catch (OperationCanceledException) {} // Expected

        // Assert
        executed.ShouldBeTrue();
    }

    [Fact]
    public async Task EnqueueAsync_Should_Process_Multiple_WorkItems()
    {
        // Arrange
        int executedCount = 0;
        var workItems = new List<NotificationWorkItem>();

        for (int i = 0; i < 5; i++)
        {
            workItems.Add(new NotificationWorkItem(null, (sp, ct) =>
            {
                Interlocked.Increment(ref executedCount);
                return Task.CompletedTask;
            }));
        }

        // Act
        foreach (var workItem in workItems)
        {
            await _queueService.EnqueueAsync(workItem);
        }

        using var cts = new CancellationTokenSource();
        var task = _queueService.StartAsync(cts.Token);

        // Wait for all work items to be processed
        int retry = 0;
        while (executedCount < 5 && retry < 20)
        {
            await Task.Delay(50);
            retry++;
        }

        cts.Cancel();
        try
        {
            await task;
        }
        catch (OperationCanceledException) { } // Expected

        // Assert
        executedCount.ShouldBe(5);
    }

    [Fact]
    public async Task StopAsync_Should_Stop_Processing()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var startTask = _queueService.StartAsync(cts.Token);

        // Act
        await _queueService.StopAsync(CancellationToken.None);

        // Assert
        // Should complete without throwing
        await Should.NotThrowAsync(async () => await startTask);
    }

    /// <summary>
    /// ★★ A message scheduled two months out: one Task.Delay of that length throws, which used to be logged as an
    /// error with the work item dropped (left to the dispatch recovery after its grace period). It has to keep waiting.
    /// </summary>
    [Fact]
    public async Task ADelayLongerThanTheTimerCeiling_KeepsWaitingInsteadOfFailing()
    {
        using var cts = new CancellationTokenSource();
        await _queueService.StartAsync(cts.Token);

        await _queueService.EnqueueWithDelayAsync(
            new NotificationWorkItem(null, (_, _) => Task.CompletedTask), TimeSpan.FromDays(60));
        await Task.Delay(200);

        _loggerMock.Invocations.ToArray()
            .Any(i => i.Method.Name == nameof(ILogger.Log) && Equals(i.Arguments[0], LogLevel.Error))
            .ShouldBeFalse("the delayed work item failed instead of waiting");

        await _queueService.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task LongDelay_FarBeyondTheTimerCeiling_WaitsInSlicesUntilCancelled()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Should.ThrowAsync<OperationCanceledException>(() => LongDelay.DelayAsync(TimeSpan.FromDays(400), cts.Token));
    }

    [Fact]
    public async Task LongDelay_OfAnEnormousSpan_DoesNotOverflow()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Should.ThrowAsync<OperationCanceledException>(() => LongDelay.DelayAsync(TimeSpan.MaxValue, cts.Token));
    }
}
