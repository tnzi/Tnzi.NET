
namespace Tnzi.Audit.Services;

/// <summary>
/// 审计日志发送者实现
/// </summary>
public class AuditSender : IAuditSender, IAuditConsumer
{
    private readonly Channel<AuditOperation> _channel;

    // 队列容量固化进 Channel 是刻意的（BoundedChannel 容量运行时不可变）；
    // 用 Monitor 在实例创建时点读一次，语义等价且不触发热消费审计告警。
    public AuditSender(IOptionsMonitor<Audit.Options.AuditOptions> options)
    {
        Check.NotNull(options);

        var capacity = options.CurrentValue.ChannelCapacity;
        _channel = capacity > 0
            ? Channel.CreateBounded<AuditOperation>(new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
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

    public async Task SendAsync(AuditOperation operation)
    {
        await _channel.Writer.WriteAsync(operation);
    }
}
