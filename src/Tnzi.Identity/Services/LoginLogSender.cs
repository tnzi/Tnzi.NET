namespace Tnzi.Identity.Services;

/// <summary>
/// 登录日志消费者接口
/// </summary>
public interface ILoginLogConsumer
{
    ChannelReader<LoginLog> Reader { get; }
}

/// <summary>
/// 登录日志发送者和消费者实现
/// </summary>
public class LoginLogSender : ILoginLogSender, ILoginLogConsumer
{
    private readonly Channel<LoginLog> _channel;

    public LoginLogSender()
    {
        _channel = Channel.CreateBounded<LoginLog>(new BoundedChannelOptions(2000)
        {
            FullMode = BoundedChannelFullMode.DropWrite
        });
    }

    public ChannelReader<LoginLog> Reader => _channel.Reader;

    public async Task SendAsync(LoginLog log)
    {
        if (log == null) return;
        await _channel.Writer.WriteAsync(log);
    }
}
