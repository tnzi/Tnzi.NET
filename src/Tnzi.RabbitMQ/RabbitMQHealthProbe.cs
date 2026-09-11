namespace Tnzi.RabbitMQ;

/// <summary>
/// RabbitMQ 连通性探针：直接问那条被复用的连接开着没有。
/// </summary>
/// <remarks>
/// <para>
/// 刻意<b>不</b>去发一条探测消息或建一个临时 Channel：健康检查会被 K8s 每几秒调一次，
/// 每次都在共享连接上做副作用只会自己造出问题。<c>IConnection.IsOpen</c> 由客户端的
/// 心跳与自动恢复维护，代理不可达时它会翻成 false —— 这正是要报出去的那件事。
/// </para>
/// <para>
/// 连接是懒创建的（DI 工厂第一次被解析时才连），所以这个探针本身可能触发首次连接。
/// 那是对的：<c>/health/ready</c> 的问题就是「这个实例现在能不能收发消息」。
/// </para>
/// </remarks>
public class RabbitMQHealthProbe : IDistributedEventBusHealthProbe
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// 初始化一个 <see cref="RabbitMQHealthProbe"/> 类型的新实例。
    /// </summary>
    /// <remarks>
    /// 经 <see cref="IServiceProvider"/> 惰性取 <c>IConnection</c> 而不是构造注入：
    /// 构造注入会让「解析健康检查」变成「必须先连上代理」，代理宕机时整个
    /// <c>/health</c> 端点会 500 —— 那正是它该报告的情形，却变成了它自己不可用。
    /// </remarks>
    public RabbitMQHealthProbe(IServiceProvider serviceProvider)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
    }

    /// <inheritdoc />
    public string TransportName => "RabbitMQ";

    /// <inheritdoc />
    public Task<DistributedEventBusHealth> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = _serviceProvider.GetService<IConnection>();

            if (connection == null)
            {
                return Task.FromResult(new DistributedEventBusHealth(
                    false, "no RabbitMQ connection is registered"));
            }

            return Task.FromResult(connection.IsOpen
                ? new DistributedEventBusHealth(true)
                : new DistributedEventBusHealth(false, "the AMQP connection is closed"));
        }
        catch (Exception ex)
        {
            // 连接工厂在解析时就会尝试连接，连不上会抛。异常消息可能带连接串，
            // 因此只报类型名 —— 探针端点是匿名可访问的。
            return Task.FromResult(new DistributedEventBusHealth(
                false, $"connecting to RabbitMQ failed ({ex.GetType().Name})"));
        }
    }
}
