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
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("{Service} is starting.", ServiceName);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await _reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
                {
                    await DrainOneBatchAsync(stoppingToken).ConfigureAwait(false);
                }
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

        _logger.LogInformation("{Service} is stopping.", ServiceName);
    }

    private async Task DrainOneBatchAsync(CancellationToken stoppingToken)
    {
        var batchSize = Math.Max(1, BatchSize);
        var batch = new List<TItem>(batchSize);

        while (batch.Count < batchSize && _reader.TryRead(out var item))
        {
            batch.Add(item);
        }

        if (batch.Count == 0)
            return;

        using var scope = _serviceProvider.CreateScope();

        try
        {
            await ProcessBatchAsync(batch, scope.ServiceProvider, stoppingToken).ConfigureAwait(false);
            _logger.LogDebug("{Service} processed {Count} items.", ServiceName, batch.Count);
        }
        catch (Exception ex)
        {
            // 丢弃这一批：遥测数据不值得把后台服务拖垮（见类型注释）。
            _logger.LogError(ex, "{Service} failed to process a batch of {Count} items; the batch was dropped.",
                ServiceName, batch.Count);
        }
    }
}
