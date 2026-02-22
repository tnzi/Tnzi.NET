namespace Tnzi.System.Services;

/// <summary>
/// 访问日志消费者接口
/// </summary>
public interface IAccessLogConsumer
{
    /// <summary>
    /// 获取通道读取器
    /// </summary>
    ChannelReader<AccessLogDto> Reader { get; }
}
