
namespace Tnzi.Audit.Services;

/// <summary>
/// 审计日志消费者接口
/// </summary>
public interface IAuditConsumer
{
    /// <summary>
    /// 获取通道读取器
    /// </summary>
    ChannelReader<AuditOperation> Reader { get; }
}
