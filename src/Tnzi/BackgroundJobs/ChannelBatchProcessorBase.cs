using Microsoft.Extensions.Hosting;
using System.Threading.Channels;

namespace Tnzi.BackgroundJobs;

/// <summary>
/// 「读通道 → 攒一批 → 在独立 DI 作用域里落库」这一类后台服务的骨架。
/// </summary>
/// <typeparam name="TItem">通道里流动的元素类型</typeparam>
/// <remarks>
/// 适用形态：请求线程把遥测数据（登录日志 / 访问日志 / 审计操作）投进一个有界
/// <see cref="Channel{T}"/> 就立刻返回，由本服务在后台成批写库，从而不让写日志的耗时
/// 落在请求链路上。此前 Identity / System / Audit 三处各自实现了逐行几乎一致的这套循环。
///
/// ★**批处理失败是记日志后丢弃这一批，而不是让服务崩掉**。这是遥测数据的既定取舍：
/// 数据库抖动时宁可丢几条访问日志，也不能让后台服务退出导致此后**所有**日志都写不进去
/// （通道满了之后连投递方也会开始丢）。真正需要"一条都不能丢"的场景请用 Outbox。
///
/// ★**每批一个 DI 作用域**：仓储与 DbContext 是 Scoped，而后台服务是 Singleton，
/// 不开作用域就会跨批共用同一个 DbContext（变更跟踪器无限增长 + 并发访问）。
///
/// ★**停机先排空再退出**：停机信号不会打断正在落库的批次，通道里剩下的元素也照常成批写完，
/// 整段排空以 <see cref="ShutdownDrainTimeout"/> 为限；只有超时才放弃剩余元素并记 Warning。
/// 子类收到的 <c>cancellationToken</c> 因此只在排空超时后才会取消。
/// </remarks>
public abstract class ChannelBatchProcessorBase<TItem> : BackgroundService
{
    private readonly ChannelReader<TItem> _reader;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;

    /// <summary>
    /// 初始化通道批处理后台服务。
    /// </summary>
    /// <param name="reader">通道读取端</param>
    /// <param name="serviceProvider">根服务提供者（用于每批开作用域）</param>
    /// <param name="logger">日志记录器</param>
    protected ChannelBatchProcessorBase(ChannelReader<TItem> reader, IServiceProvider serviceProvider, ILogger logger)
    {
        _reader = Check.NotNull(reader);
        _serviceProvider = Check.NotNull(serviceProvider);
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 日志记录器，供子类在批处理过程中记录降级信息。
    /// </summary>
    protected ILogger Logger => _logger;

    /// <summary>
    /// 单批最大条数。子类可覆盖（例如从 <c>IOptionsMonitor</c> 热读）。
    /// </summary>
    protected virtual int BatchSize => 100;

    /// <summary>
    /// 循环层出错后的退避间隔，避免数据库长时间不可用时空转刷屏。
    /// </summary>
    protected virtual TimeSpan ErrorRetryDelay => TimeSpan.FromSeconds(5);

    /// <summary>
    /// 停机信号到达后，把正在落库的批次与通道里剩下的元素写完的时间上限。
    /// 超时仍未写完的元素被放弃（记 Warning）。须明显小于宿主的 <c>HostOptions.ShutdownTimeout</c>（默认 30 秒），
    /// 否则宿主会先于排空结束放弃等待。
    /// </summary>
    protected virtual TimeSpan ShutdownDrainTimeout => TimeSpan.FromSeconds(5);

    /// <summary>
    /// 日志中使用的服务名。
    /// </summary>
    protected virtual string ServiceName => GetType().Name;

    /// <summary>
    /// 处理一批元素。<paramref name="scopedServices"/> 是本批独占的 DI 作用域，
    /// 从中解析仓储 / 存储服务。抛出的异常由基类捕获并记录，这一批被丢弃。
    /// </summary>
    protected abstract Task ProcessBatchAsync(
        IReadOnlyList<TItem> batch,
        IServiceProvider scopedServices,
        CancellationToken cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// ★落库用的令牌与停机信号是两个：停机信号只让循环不再等新元素，正在落库的批次不被打断；
    /// 循环退出后把通道里剩下的元素照常成批写完，整段排空以 <see cref="ShutdownDrainTimeout"/> 为限，
    /// 到点才取消落库令牌。否则每次发布 / 重启都会丢掉停机前最后几秒的审计与登录记录。
    /// </remarks>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("{Service} is starting.", ServiceName);

        using var processingCts = new CancellationTokenSource();
        var drainTimeout = ShutdownDrainTimeout < TimeSpan.Zero ? TimeSpan.Zero : ShutdownDrainTimeout;
        using var stopRegistration = stoppingToken.Register(() => processingCts.CancelAfter(drainTimeout));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await _reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
                {
                    // 写入端已完成且通道已空：不会再有元素，继续等只会空转
                    break;
                }

                await DrainOneBatchAsync(processingCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // 正常停机
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in {Service}.", ServiceName);

                try
                {
                    await Task.Delay(ErrorRetryDelay, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        await DrainOnShutdownAsync(processingCts.Token).ConfigureAwait(false);

        _logger.LogInformation("{Service} is stopping.", ServiceName);
    }

    /// <summary>
    /// 停机时把通道里剩下的元素成批写完。一批失败就停下：数据库此刻不可用，
    /// 继续逐批重试只会在停机窗口里刷出一串同样的错误。
    /// </summary>
    private async Task DrainOnShutdownAsync(CancellationToken processingToken)
    {
        while (!processingToken.IsCancellationRequested)
        {
            var outcome = await DrainOneBatchAsync(processingToken).ConfigureAwait(false);
            if (outcome == BatchOutcome.Empty)
                return;
            if (outcome != BatchOutcome.Stored)
                break;
        }

        if (_reader.TryPeek(out _))
        {
            _logger.LogWarning("{Service} stopped with items still queued; they were dropped because the shutdown drain could not finish.",
                ServiceName);
        }
    }

    private async Task<BatchOutcome> DrainOneBatchAsync(CancellationToken processingToken)
    {
        var batchSize = Math.Max(1, BatchSize);
        var batch = new List<TItem>(batchSize);

        while (batch.Count < batchSize && _reader.TryRead(out var item))
        {
            batch.Add(item);
        }

        if (batch.Count == 0)
            return BatchOutcome.Empty;

        try
        {
            using var scope = _serviceProvider.CreateScope();
            await ProcessBatchAsync(batch, scope.ServiceProvider, processingToken).ConfigureAwait(false);
            _logger.LogDebug("{Service} processed {Count} items.", ServiceName, batch.Count);
            return BatchOutcome.Stored;
        }
        catch (OperationCanceledException) when (processingToken.IsCancellationRequested)
        {
            // 落库令牌只在停机排空超时后才会取消
            _logger.LogWarning("{Service} dropped a batch of {Count} items because the shutdown drain timeout ({Timeout}) elapsed before it was stored.",
                ServiceName, batch.Count, ShutdownDrainTimeout);
            return BatchOutcome.Dropped;
        }
        catch (Exception ex)
        {
            // 丢弃这一批：遥测数据不值得把后台服务拖垮（见类型注释）。
            _logger.LogError(ex, "{Service} failed to process a batch of {Count} items; the batch was dropped.",
                ServiceName, batch.Count);
            return BatchOutcome.Dropped;
        }
    }

    private enum BatchOutcome
    {
        Empty,
        Stored,
        Dropped
    }
}
