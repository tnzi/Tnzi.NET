
namespace Tnzi.Kafka.Exceptions;

/// <summary>
/// Kafka 异常
/// </summary>
public class KafkaException : InfrastructureException
{
    /// <summary>
    /// 主题名称
    /// </summary>
    public string? Topic { get; }
    
    /// <summary>
    /// 初始化一个<see cref="KafkaException"/>类型的新实例
    /// </summary>
    /// <param name="message">异常消息</param>
    /// <param name="topic">主题名称</param>
    /// <param name="innerException">内部异常</param>
    public KafkaException(string message, string? topic = null, Exception? innerException = null)
        : base("Kafka", message, true, innerException)
    {
        Topic = topic;
        if (topic != null)
        {
            this.WithData("Topic", topic);
        }
    }
}

/// <summary>
/// Kafka 连接异常
/// </summary>
public class KafkaConnectionException : KafkaException
{
    /// <summary>
    /// 连接端点
    /// </summary>
    public string? Endpoint { get; }
    
    /// <summary>
    /// 初始化一个<see cref="KafkaConnectionException"/>类型的新实例
    /// </summary>
    /// <param name="message">异常消息</param>
    /// <param name="endpoint">连接端点</param>
    /// <param name="innerException">内部异常</param>
    public KafkaConnectionException(string message, string? endpoint = null, Exception? innerException = null)
        : base(message, null, innerException)
    {
        Endpoint = endpoint;
        if (endpoint != null)
        {
            this.WithData("Endpoint", endpoint);
        }
    }
}