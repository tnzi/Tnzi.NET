namespace Tnzi.Identity.Services;

/// <summary>
/// 登录日志消费者接口
/// </summary>
public interface ILoginLogConsumer
{
    ChannelReader<LoginLog> Reader { get; }
}

/// <summary>
/// 登录日志发送者和消费者实现：登录路径把 <see cref="LoginLog"/> 投进有界内存队列即返回，后台服务成批落库。
/// </summary>
/// <remarks>
/// <para>
/// ★ <b>队列满时丢新来的，并且丢弃必须可见。</b>此前是 <c>DropWrite</c> 且零记账：<c>WriteAsync</c> 永远立即成功，
/// 被丢掉的那条既不返回也不可观测。<c>LoginSecurityService</c> 拿这张表做「频繁尝试」「异常来源地址」这类安全判定，
/// 而把 <see cref="Capacity"/> 格队列塞满的恰恰是一次撞库 —— 溢出的尝试无声消失，判定在最需要它的时刻少算，
/// 事后也没人看得出记录不全。
/// </para>
/// <para>
/// 与 <c>Tnzi.Audit</c> 的 <c>AuditSender</c> 同款：<c>Wait</c> 模式 + <c>TryWrite</c>（<c>DropWrite</c> 模式下
/// <c>TryWrite</c> 会「成功」，丢弃就不可见了），失败逐条计数，第一次与之后每 <see cref="DropLogInterval"/> 次记一条
/// <b>Error</b>（登录记录是安全判定的输入，不是遥测）。累计丢弃数经 <see cref="DroppedCount"/> 暴露给诊断。
/// 丢新不丢旧：旧记录是已经发生、正等待落库的证据。
/// </para>
/// </remarks>
public class LoginLogSender : ILoginLogSender, ILoginLogConsumer
{
    /// <summary>队列容量。</summary>
    public const int Capacity = 2000;

    /// <summary>每丢弃多少条记一次日志（第一次总是记）。</summary>
    public const long DropLogInterval = 1_000;

    private readonly Channel<LoginLog> _channel;
    private readonly ILogger<LoginLogSender> _logger;
    private readonly int _capacity;
    private long _dropped;

    /// <summary>初始化一个 <see cref="LoginLogSender"/> 实例。</summary>
    public LoginLogSender(ILogger<LoginLogSender> logger) : this(logger, Capacity)
    {
    }

    /// <summary>以指定容量初始化（测试用；生产走 <see cref="Capacity"/>）。</summary>
    internal LoginLogSender(ILogger<LoginLogSender> logger, int capacity)
    {
        _logger = Check.NotNull(logger);
        _capacity = Check.Positive(capacity);
        _channel = Channel.CreateBounded<LoginLog>(new BoundedChannelOptions(_capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <inheritdoc />
    public ChannelReader<LoginLog> Reader => _channel.Reader;

    /// <summary>累计丢弃条数（诊断用）。</summary>
    public long DroppedCount => Interlocked.Read(ref _dropped);

    /// <inheritdoc />
    /// <remarks>
    /// 永不阻塞调用方：队列满时丢掉这一条并记账，而不是让登录等待日志落库。
    /// </remarks>
    public Task SendAsync(LoginLog log)
    {
        Check.NotNull(log);

        if (_channel.Writer.TryWrite(log))
            return Task.CompletedTask;

        var dropped = Interlocked.Increment(ref _dropped);
        if (dropped == 1 || dropped % DropLogInterval == 0)
        {
            _logger.LogError(
                "Login log queue is full ({Capacity}); {Dropped} login record(s) dropped so far. "
                + "Identity_LoginLog is missing records while this persists, so login security checks undercount "
                + "(latest dropped: {Status} for {UserName} from {IpAddress}).",
                _capacity,
                dropped,
                log.Status,
                log.UserName,
                log.IpAddress);
        }

        return Task.CompletedTask;
    }
}
