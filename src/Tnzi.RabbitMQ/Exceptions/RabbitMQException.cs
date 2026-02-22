
namespace Tnzi.RabbitMQ.Exceptions;

/// <summary>
/// 消息队列异常基类
/// </summary>
public class MessageQueueException : InfrastructureException
{
    /// <summary>
    /// 队列名称
    /// </summary>
    public string? QueueName { get; }
    
    /// <summary>
    /// 初始化一个<see cref="MessageQueueException"/>类型的新实例
    /// </summary>
    /// <param name="componentName">组件名称</param>
    /// <param name="message">异常消息</param>
    /// <param name="queueName">队列名称</param>
    /// <param name="isRetryable">是否可重试</param>
    /// <param name="innerException">内部异常</param>
    public MessageQueueException(
        string componentName,
        string message, 
        string? queueName = null,
        bool isRetryable = true,
        Exception? innerException = null)
        : base(componentName, message, isRetryable, innerException)
    {
        QueueName = queueName;
        if (queueName != null)
        {
            this.WithData("QueueName", queueName);
        }
    }
}

/// <summary>
/// RabbitMQ 异常
/// </summary>
public class RabbitMQException : MessageQueueException
{
    /// <summary>
    /// 初始化一个<see cref="RabbitMQException"/>类型的新实例
    /// </summary>
    /// <param name="message">异常消息</param>
    /// <param name="queueName">队列名称</param>
    /// <param name="innerException">内部异常</param>
    public RabbitMQException(string message, string? queueName = null, Exception? innerException = null)
        : base("RabbitMQ", message, queueName, true, innerException)
    {
    }
}

/// <summary>
/// RabbitMQ 连接异常
/// </summary>
public class RabbitMQConnectionException : RabbitMQException
{
    /// <summary>
    /// 连接端点
    /// </summary>
    public string? Endpoint { get; }
    
    /// <summary>
    /// 初始化一个<see cref="RabbitMQConnectionException"/>类型的新实例
    /// </summary>
    /// <param name="message">异常消息</param>
    /// <param name="endpoint">连接端点</param>
    /// <param name="innerException">内部异常</param>
    public RabbitMQConnectionException(string message, string? endpoint = null, Exception? innerException = null)
        : base(message, null, innerException)
    {
        Endpoint = endpoint;
        if (endpoint != null)
        {
            this.WithData("Endpoint", endpoint);
        }
    }
}