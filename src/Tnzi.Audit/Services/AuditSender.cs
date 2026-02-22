
namespace Tnzi.Audit.Services;

/// <summary>
/// 审计日志发送者实现
/// </summary>
public class AuditSender : IAuditSender, IAuditConsumer
{
    private readonly Channel<AuditOperation> _channel;

    public AuditSender(IOptions<Audit.Options.AuditOptions> options)
    {
        Check.NotNull(options);

        var capacity = options.Value.ChannelCapacity;
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
