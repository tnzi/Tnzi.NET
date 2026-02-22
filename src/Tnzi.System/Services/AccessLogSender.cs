namespace Tnzi.System.Services;

/// <summary>
/// 访问日志发送者和消费者实现
/// </summary>
public class AccessLogSender : IAccessLogSender, IAccessLogConsumer
{
    private readonly Channel<AccessLogDto> _channel;

    public AccessLogSender()
    {
        // 使用容量限制，防止内存溢出，满时丢弃最新（因为通常不是核心业务日志）
        _channel = Channel.CreateBounded<AccessLogDto>(new BoundedChannelOptions(5000)
        {
            FullMode = BoundedChannelFullMode.DropWrite
        });
    }

    public ChannelReader<AccessLogDto> Reader => _channel.Reader;

    public async Task SendAsync(AccessLogDto log)
    {
        Check.NotNull(log);
        await _channel.Writer.WriteAsync(log);
    }
}
