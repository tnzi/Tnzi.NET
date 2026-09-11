namespace Tnzi.Feature.Services;

/// <summary>
/// 用量记录的有界内存队列：请求线程 <see cref="TrySend"/> 即返回，后台服务成批落库。
/// </summary>
/// <remarks>
/// <para>
/// 容量 <see cref="Capacity"/>。满了<b>丢最新的</b>并计数：用量是遥测，宁可少几条也不能让功能检查阻塞
/// 或让请求因为写不进用量表而失败。丢弃在第一次与之后每 <see cref="DropLogInterval"/> 次各记一条 Warning ——
/// 静默丢弃会让用量 Tab 上的曲线凭空掉一段而无人知道原因。
/// </para>
/// <para>
/// 与 <c>Tnzi.System</c> 的访问日志、<c>Tnzi.Audit</c> 的操作审计同一形状（通道 + <c>ChannelBatchProcessorBase</c>）。
/// </para>
/// </remarks>
public sealed class FeatureUsageSender : IFeatureUsageSender, IFeatureUsageConsumer
{
    /// <summary>队列容量。</summary>
    public const int Capacity = 5000;

    /// <summary>每丢弃多少条记一次 Warning（第一次总是记）。</summary>
    internal const long DropLogInterval = 10_000;

    private readonly Channel<FeatureUsageRecord> _channel;
    private readonly ILogger<FeatureUsageSender> _logger;
    private long _dropped;

    /// <summary>
    /// 初始化用量队列。
    /// </summary>
    public FeatureUsageSender(ILogger<FeatureUsageSender> logger)
    {
        _logger = Check.NotNull(logger);
        // Wait 模式 + TryWrite：满了 TryWrite 直接返回 false，我们自己计数并记日志；
        // DropWrite 模式下 TryWrite 会「成功」，丢弃就不可见了。
        _channel = Channel.CreateBounded<FeatureUsageRecord>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    /// <inheritdoc />
    public ChannelReader<FeatureUsageRecord> Reader => _channel.Reader;

    /// <summary>累计丢弃条数（诊断用）。</summary>
    public long DroppedCount => Interlocked.Read(ref _dropped);

    /// <inheritdoc />
    public bool TrySend(FeatureUsageRecord record)
    {
        Check.NotNull(record);

        if (_channel.Writer.TryWrite(record))
        {
            return true;
        }

        var dropped = Interlocked.Increment(ref _dropped);
        if (dropped == 1 || dropped % DropLogInterval == 0)
        {
            _logger.LogWarning(
                "Feature usage queue is full ({Capacity}); {Dropped} usage record(s) dropped so far. "
                + "The usage analytics undercount while this persists.",
                Capacity,
                dropped);
        }

        return false;
    }
}
