namespace Tnzi.Kafka;

/// <summary>
/// Kafka 连通性探针：借生产者已有的句柄问一次集群元数据。
/// </summary>
/// <remarks>
/// <para>
/// Kafka 没有「连接开着没有」这种一问就答的状态（librdkafka 在后台自己维护 broker 连接），
/// 因此这里用元数据查询作为可达性判据：拿得到元数据就说明至少有一个 broker 应答。
/// 用 <see cref="DependentAdminClientBuilder"/> 复用生产者句柄，不再单开一条连接 ——
/// 健康检查每几秒跑一次，每次都建新客户端是自己给集群加压。
/// </para>
/// <para>
/// <c>GetMetadata</c> 是阻塞 API，因此加了明确的超时；超时即视为不可达，
/// 这正是就绪探针要回答的问题。
/// </para>
/// </remarks>
public class KafkaHealthProbe : IDistributedEventBusHealthProbe
{
    private static readonly TimeSpan MetadataTimeout = TimeSpan.FromSeconds(5);

    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// 初始化一个 <see cref="KafkaHealthProbe"/> 类型的新实例。
    /// </summary>
    /// <remarks>
    /// 惰性取生产者：构造注入会让「解析健康检查」本身依赖生产者能被建出来。
    /// </remarks>
    public KafkaHealthProbe(IServiceProvider serviceProvider)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
    }

    /// <inheritdoc />
    public string TransportName => "Kafka";

    /// <inheritdoc />
    public async Task<DistributedEventBusHealth> CheckAsync(CancellationToken cancellationToken = default)
    {
        var producer = _serviceProvider.GetService<IProducer<string, string>>();

        if (producer == null)
        {
            return new DistributedEventBusHealth(false, "no Kafka producer is registered");
        }

        try
        {
            return await Task.Run(() =>
            {
                using var adminClient = new DependentAdminClientBuilder(producer.Handle).Build();
                var metadata = adminClient.GetMetadata(MetadataTimeout);

                return metadata.Brokers.Count > 0
                    ? new DistributedEventBusHealth(true, $"{metadata.Brokers.Count} broker(s) reachable")
                    : new DistributedEventBusHealth(false, "cluster metadata lists no broker");
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            // 异常消息可能带 bootstrap 地址与凭据线索，只报类型名 —— 端点是匿名可访问的
            return new DistributedEventBusHealth(false, $"fetching Kafka metadata failed ({ex.GetType().Name})");
        }
    }
}
