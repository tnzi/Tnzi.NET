using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tnzi.EventBus;
using Tnzi.Modules;

namespace Tnzi.Kafka.Tests;

/// <summary>
/// 引导服务器的来源顺序：<c>EventBus:KafkaBootstrapServers</c> &gt; <c>ConnectionStrings:Kafka</c> &gt; <c>Kafka:BootstrapServers</c>。
/// </summary>
/// <remarks>
/// 配置样例里 <c>EventBus:KafkaBootstrapServers</c> 写成空串。空串不是「没配」也不是一个可用的地址：
/// 若只在 null 时才看下一个来源，按文档改配 <c>ConnectionStrings:Kafka</c> 的部署会越过它，
/// 安静地连到 <c>Kafka:BootstrapServers</c> 的默认值 <c>localhost:9092</c>。
/// </remarks>
public class BootstrapServerSourceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ConnectionStringIsUsed_WhenTheEventBusKeyIsMissingOrBlank(string? eventBusValue)
    {
        await using var provider = await BuildProviderAsync(new Dictionary<string, string?>
        {
            ["EventBus:KafkaBootstrapServers"] = eventBusValue,
            ["ConnectionStrings:Kafka"] = "broker-a:9092",
        });

        BootstrapServersOf(provider).ShouldBe("broker-a:9092");
    }

    [Fact]
    public async Task EventBusKey_WinsOverTheConnectionString()
    {
        await using var provider = await BuildProviderAsync(new Dictionary<string, string?>
        {
            ["EventBus:KafkaBootstrapServers"] = "broker-b:9092",
            ["ConnectionStrings:Kafka"] = "broker-a:9092",
        });

        BootstrapServersOf(provider).ShouldBe("broker-b:9092");
    }

    [Fact]
    public async Task KafkaSection_IsTheLastResort()
    {
        await using var provider = await BuildProviderAsync(new Dictionary<string, string?>
        {
            ["EventBus:KafkaBootstrapServers"] = "",
            ["Kafka:BootstrapServers"] = "broker-c:9092",
        });

        BootstrapServersOf(provider).ShouldBe("broker-c:9092");
    }

    private static string BootstrapServersOf(IServiceProvider provider)
    {
        var bus = provider.GetRequiredService<KafkaEventBus>();
        return bus.BuildConsumerConfig(bus.ConsumerIdentity.Describe(typeof(ProbeIntegrationEvent))).BootstrapServers;
    }

    private static async Task<ServiceProvider> BuildProviderAsync(Dictionary<string, string?> settings)
    {
        settings["EventBus:Type"] = "Kafka";
        settings["EventBus:ConsumerGroup"] = "tests-app";

        var module = new KafkaEventBusModule();
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var context = new ServiceConfigurationContext(services, configuration);
        await module.PreConfigureServicesAsync(context);
        await module.ConfigureServicesAsync(context);

        // 不建真实生产者：那会起一个连不存在代理的 librdkafka 实例
        services.RemoveAll<IProducer<string, string>>();
        services.AddSingleton(new Mock<IProducer<string, string>>().Object);

        return services.BuildServiceProvider();
    }

    public class ProbeIntegrationEvent : EventBase, IIntegrationEvent
    {
        public string SourceService { get; set; } = "tests";
    }
}
