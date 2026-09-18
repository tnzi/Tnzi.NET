namespace Tnzi.Audit.Services;

/// <summary>
/// 审计日志发送者实现：请求线程把 <see cref="AuditOperation"/> 投进有界内存队列即返回，后台服务成批落库。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>队列满时丢新来的，并且丢弃必须可见。</b>此前是 <c>DropOldest</c> 且零记账：<c>WriteAsync</c> 永远立即成功，
/// 被挤掉的那条既不返回也不可观测 —— 而挤掉的是<b>已经排队等着落库的旧记录</b>，恰好是这波高峰之前发生的、
/// 事后要回溯的那些。本模块自己的纪律是「审计数据真实缺失须可见」（条目截断记 Warning），通道溢出是同一类缺失，
/// 且对一张合规证据表，静默少一段比少一段本身更糟：没有任何地方看得出少了多少。
/// </para>
/// <para>
/// 与 <c>Tnzi.Feature</c> 的 <c>FeatureUsageSender</c> 同款：<c>Wait</c> 模式 + <c>TryWrite</c>（<c>DropWrite</c> 模式下
/// <c>TryWrite</c> 会「成功」，丢弃就不可见了），失败逐条计数，第一次与之后每 <see cref="DropLogInterval"/> 次记一条
/// <b>Error</b>（用量是遥测记 Warning 够了，审计不是）。累计丢弃数经 <see cref="DroppedCount"/> 暴露给诊断。
/// 丢新不丢旧：旧记录是已经发生、正等待落库的证据。
/// </para>
/// <para>
/// 队列容量固化进 Channel 是刻意的（BoundedChannel 容量运行时不可变）；
/// 用 Monitor 在实例创建时点读一次，语义等价且不触发热消费审计告警。
/// </para>
/// </remarks>
public class AuditSender : IAuditSender, IAuditConsumer
{
    /// <summary>每丢弃多少条记一次日志（第一次总是记）。</summary>
    public const long DropLogInterval = 1_000;

    private readonly Channel<AuditOperation> _channel;
    private readonly ILogger<AuditSender> _logger;
    private readonly int _capacity;
    private long _dropped;

    /// <summary>初始化一个 <see cref="AuditSender"/> 实例。</summary>
    public AuditSender(IOptionsMonitor<AuditOptions> options, ILogger<AuditSender> logger)
    {
        Check.NotNull(options);
        _logger = Check.NotNull(logger);

        _capacity = options.CurrentValue.ChannelCapacity;
        _channel = _capacity > 0
            ? Channel.CreateBounded<AuditOperation>(new BoundedChannelOptions(_capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            })
            : Channel.CreateUnbounded<AuditOperation>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
    }

    /// <summary>
    /// 获取通道读取器
    /// </summary>
    public ChannelReader<AuditOperation> Reader => _channel.Reader;

    /// <summary>累计丢弃条数（诊断用）。</summary>
    public long DroppedCount => Interlocked.Read(ref _dropped);

    /// <inheritdoc />
    /// <remarks>
    /// 永不阻塞调用方：队列满时丢掉这一条并记账，而不是让请求等待审计落库。
    /// </remarks>
    public Task SendAsync(AuditOperation operation)
    {
        Check.NotNull(operation);

        if (_channel.Writer.TryWrite(operation))
            return Task.CompletedTask;

        var dropped = Interlocked.Increment(ref _dropped);
        if (dropped == 1 || dropped % DropLogInterval == 0)
        {
            _logger.LogError(
                "Audit queue is full ({Capacity}); {Dropped} audit operation(s) dropped so far. "
                + "Audit_Operation is missing records while this persists (latest dropped: {HttpMethod} {Url}).",
                _capacity,
                dropped,
                operation.HttpMethod,
                operation.Url);
        }

        return Task.CompletedTask;
    }
}
