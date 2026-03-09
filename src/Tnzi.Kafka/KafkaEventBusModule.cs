
namespace Tnzi.Kafka;

/// <summary>
/// Kafka事件总线模块
/// 配置路径：Kafka（可选）、EventBus（必需）
/// 注意：此模块仅在 EventBus.Type = "Kafka" 时才会注册 Kafka 相关服务
/// </summary>
[DependsOn(typeof(EventBusModule))]
public class KafkaEventBusModule : TnziInfrastructureModule
{
    /// <summary>
    /// 在 EventBusModule 之后加载
    /// </summary>
    public override int LoadOrder => 11;

    /// <summary>
    /// 预配置服务
    /// </summary>
    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 注册配置选项并启用启动时验证
        context.Services.AddOptions<KafkaOptions>()
            .Bind(context.Configuration.GetSection("Kafka"))
            .ValidateWith<KafkaOptions, KafkaOptionsValidator>();

        return Task.CompletedTask;
    }

    /// <summary>
    /// 配置服务
    /// </summary>
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var services = context.Services;
        var configuration = context.Configuration;

        // 检查 EventBus.Type 是否为 Kafka
        var eventBusOptions = configuration.GetSection("EventBus").Get<EventBusOptions>() ?? new EventBusOptions();

        if (!string.Equals(eventBusOptions.Type, "Kafka", StringComparison.OrdinalIgnoreCase))
        {
            // 如果类型不是 Kafka，不注册 Kafka 相关服务
            return Task.CompletedTask;
        }

        // 从配置中手动绑定 KafkaOptions（此时 ServiceProvider 尚未构建）
        var kafkaOptions = new KafkaOptions();
        configuration.GetSection("Kafka").Bind(kafkaOptions);

        // 获取 BootstrapServers（优先级：EventBusOptions.KafkaBootstrapServers > ConnectionStrings:Kafka > KafkaOptions），并展开 ${VAR} 占位符
        var bootstrapServers = eventBusOptions.KafkaBootstrapServers
            ?? configuration.GetConnectionString("Kafka");

        if (string.IsNullOrWhiteSpace(bootstrapServers))
        {
            // 回退到 KafkaOptions 中的配置（可能是默认值 localhost:9092）
            bootstrapServers = kafkaOptions.BootstrapServers;
        }

        bootstrapServers = ConnectionStringExpander.Expand(bootstrapServers, configuration);

        // 注册Kafka生产者
        services.AddSingleton<IProducer<string, string>>(provider =>
        {
            var options = provider.GetService<IOptions<KafkaOptions>>()?.Value ?? new KafkaOptions();

            var producerConfig = new ProducerConfig
            {
                BootstrapServers = bootstrapServers,
                Acks = options.Producer.Acks,
                MessageSendMaxRetries = options.Producer.RetryCount,
                RetryBackoffMs = options.Producer.RetryBackoffMs,
                MessageTimeoutMs = options.Producer.MessageTimeoutMs,
                EnableIdempotence = options.Producer.EnableIdempotence
            };

            return new ProducerBuilder<string, string>(producerConfig).Build();
        });

        // 注册KafkaEventBus实例（作为Singleton注册，DI容器会在关闭时自动调用DisposeAsync）
        services.AddSingleton<KafkaEventBus>(provider =>
        {
            var producer = provider.GetRequiredService<IProducer<string, string>>();
            var logger = provider.GetRequiredService<ILogger<KafkaEventBus>>();
            var options = provider.GetService<IOptions<KafkaOptions>>()?.Value ?? new KafkaOptions();

            return new KafkaEventBus(producer, logger, provider, options, bootstrapServers);
        });

        // 注册IEventBus和IIntegrationEventBus接口（替换本地事件总线），指向同一实例
        services.RemoveAll<IEventBus>();
        services.AddSingleton<IEventBus>(provider => provider.GetRequiredService<KafkaEventBus>());
        services.AddSingleton<IIntegrationEventBus>(provider => provider.GetRequiredService<KafkaEventBus>());

        return Task.CompletedTask;
    }
}
