using System.Threading.Channels;

namespace Tnzi.Tests.BackgroundJobs;

/// <summary>
/// 通道批处理骨架的停机语义：停机信号到达时，正在落库的批次与通道里还没读的元素
/// 在一段有限的排空时间内照常写完；只有排空超时才放弃，并记为 Warning 而不是 Error。
/// </summary>
public class ChannelBatchProcessorBaseTests
{
    private sealed class RecordingLogger : ILogger
    {
        private readonly ConcurrentQueue<(LogLevel Level, string Message)> _entries = new();
        public IReadOnlyList<(LogLevel Level, string Message)> Entries => [.. _entries];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => _entries.Enqueue((logLevel, formatter(state, exception)));
    }

    /// <summary>
    /// 记录落库元素的处理器。第一批会停在 <see cref="FirstBatchGate"/> 上，直到测试放行，
    /// 用来制造「停机信号到达时还有一批在落库、通道里还有元素」的现场。
    /// </summary>
    private sealed class RecordingProcessor(Channel<int> channel, RecordingLogger logger, TimeSpan drainTimeout)
        : ChannelBatchProcessorBase<int>(channel.Reader, new ServiceCollection().BuildServiceProvider(), logger)
    {
        private int _batches;

        public ConcurrentQueue<int> Stored { get; } = new();
        public TaskCompletionSource FirstBatchEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FirstBatchGate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool BlockForever { get; init; }

        protected override int BatchSize => 2;

        protected override TimeSpan ShutdownDrainTimeout => drainTimeout;

        protected override async Task ProcessBatchAsync(IReadOnlyList<int> batch, IServiceProvider scopedServices, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _batches) == 1)
            {
                FirstBatchEntered.TrySetResult();
                if (BlockForever)
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }

                await FirstBatchGate.Task.WaitAsync(cancellationToken);
            }

            foreach (var item in batch)
            {
                Stored.Enqueue(item);
            }
        }
    }

    [Fact]
    public async Task Stop_FinishesTheInFlightBatch_AndDrainsTheItemsStillInTheChannel()
    {
        var channel = Channel.CreateUnbounded<int>();
        var logger = new RecordingLogger();
        var processor = new RecordingProcessor(channel, logger, TimeSpan.FromSeconds(30));
        for (var i = 1; i <= 5; i++)
        {
            channel.Writer.TryWrite(i);
        }

        await processor.StartAsync(CancellationToken.None);
        await processor.FirstBatchEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var stopping = processor.StopAsync(CancellationToken.None);
        processor.FirstBatchGate.TrySetResult();
        await stopping.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal([1, 2, 3, 4, 5], processor.Stored.Order().ToArray());
        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Stop_GivesUpAfterTheDrainTimeout_AndReportsItAsAWarning()
    {
        var channel = Channel.CreateUnbounded<int>();
        var logger = new RecordingLogger();
        var processor = new RecordingProcessor(channel, logger, TimeSpan.FromMilliseconds(200)) { BlockForever = true };
        for (var i = 1; i <= 5; i++)
        {
            channel.Writer.TryWrite(i);
        }

        await processor.StartAsync(CancellationToken.None);
        await processor.FirstBatchEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await processor.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(processor.Stored);
        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Error);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("drain timeout"));
    }

    [Fact]
    public async Task ItemsWrittenWhileRunning_AreStored_BeforeAnIdleStop()
    {
        var channel = Channel.CreateUnbounded<int>();
        var logger = new RecordingLogger();
        var processor = new RecordingProcessor(channel, logger, TimeSpan.FromSeconds(30));
        processor.FirstBatchGate.TrySetResult();

        await processor.StartAsync(CancellationToken.None);
        for (var i = 1; i <= 3; i++)
        {
            channel.Writer.TryWrite(i);
        }

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (processor.Stored.Count < 3 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        await processor.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal([1, 2, 3], processor.Stored.Order().ToArray());
    }

    [Fact]
    public async Task ACompletedChannel_EndsTheLoop_InsteadOfSpinning()
    {
        var channel = Channel.CreateUnbounded<int>();
        var logger = new RecordingLogger();
        var processor = new RecordingProcessor(channel, logger, TimeSpan.FromSeconds(30));
        processor.FirstBatchGate.TrySetResult();
        channel.Writer.TryWrite(1);
        channel.Writer.Complete();

        await processor.StartAsync(CancellationToken.None);

        await processor.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal([1], processor.Stored.ToArray());
    }
}
