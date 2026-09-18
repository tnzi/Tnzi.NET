namespace Tnzi.System.Services;

/// <summary>
/// 访问日志发送者和消费者实现：中间件把 <see cref="AccessLogDto"/> 投进有界内存队列即返回，后台服务成批落库并富化。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>队列满时丢新来的，并且丢弃必须可见。</b>此前是 <c>DropWrite</c> 且零记账：<c>WriteAsync</c> 永远立即成功，
/// 被丢掉的那条既不返回也不可观测。2026-09-12 起 <c>AccessLogMiddleware</c> 每个请求往这里投一行，队列第一次承受真实负载 ——
/// 而把它塞满的恰恰是流量高峰，统计页与趋势图在峰值处凭空少一段，一张以计数为全部用途的表却看不出自己少了多少。
/// </para>
/// <para>
/// 与 <c>Tnzi.Audit</c> 的 <c>AuditSender</c> 同款：<c>Wait</c> 模式 + <c>TryWrite</c>（<c>DropWrite</c> 模式下
/// <c>TryWrite</c> 会「成功」，丢弃就不可见了），失败逐条计数，第一次与之后每 <see cref="DropLogInterval"/> 次记一条
/// <b>Warning</b>（访问日志是统计不是证据，Warning 够了；审计与登录记录才记 Error）。累计丢弃数经 <see cref="DroppedCount"/> 暴露给诊断。
/// 丢新不丢旧：旧记录是已经发生、正等待落库的那些。
/// </para>
/// <para>
/// 队列容量固化进 Channel 是刻意的（BoundedChannel 容量运行时不可变）；
/// 用 Monitor 在实例创建时点读一次 <see cref="AccessLogOptions.QueueCapacity"/>，语义等价且不触发热消费审计告警。
/// </para>
/// </remarks>
public class AccessLogSender : IAccessLogSender, IAccessLogConsumer
{
    /// <summary>每丢弃多少条记一次日志（第一次总是记）。</summary>
    public const long DropLogInterval = 10_000;

    private readonly Channel<AccessLogDto> _channel;
    private readonly ILogger<AccessLogSender> _logger;
    private readonly int _capacity;
    private long _dropped;

    /// <summary>初始化一个 <see cref="AccessLogSender"/> 实例。</summary>
    public AccessLogSender(IOptionsMonitor<AccessLogOptions> options, ILogger<AccessLogSender> logger)
    {
        Check.NotNull(options);
        _logger = Check.NotNull(logger);

        _capacity = Check.Positive(options.CurrentValue.QueueCapacity);
        _channel = Channel.CreateBounded<AccessLogDto>(new BoundedChannelOptions(_capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <inheritdoc />
    public ChannelReader<AccessLogDto> Reader => _channel.Reader;

    /// <summary>累计丢弃条数（诊断用）。</summary>
    public long DroppedCount => Interlocked.Read(ref _dropped);

    /// <inheritdoc />
    /// <remarks>
    /// 永不阻塞调用方：队列满时丢掉这一条并记账，而不是让请求等待访问日志落库。
    /// </remarks>
    public Task SendAsync(AccessLogDto log)
    {
        Check.NotNull(log);

        if (_channel.Writer.TryWrite(log))
            return Task.CompletedTask;

        var dropped = Interlocked.Increment(ref _dropped);
        if (dropped == 1 || dropped % DropLogInterval == 0)
        {
            _logger.LogWarning(
                "Access log queue is full ({Capacity}); {Dropped} access log record(s) dropped so far. "
                + "Sys_AccessLog undercounts while this persists (latest dropped: {Method} {Path}).",
                _capacity,
                dropped,
                log.Method,
                log.Path);
        }

        return Task.CompletedTask;
    }
}
