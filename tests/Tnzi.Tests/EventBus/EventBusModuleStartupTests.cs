namespace Tnzi.Tests.EventBus;

/// <summary>
/// <see cref="EventBusModule"/> 的启动自证与 <see cref="EventBusOptionsValidator"/> 的连接来源规则。
/// </summary>
/// <remarks>
/// 配了 <c>EventBus:Type=RabbitMQ</c> 却漏了传输模块时，集成事件经 <c>PublishEventAsync</c> 落到本地总线：
/// 启动干净、发布成功、本服务的处理器照常执行，只是别的服务永远收不到，且没有任何告警。
/// 与缓存、对象存储同口径：配了却没加载就抛异常并指名要加载的包，绝不回退。
/// </remarks>
public class EventBusModuleStartupTests
{
    [Theory]
    [InlineData("RabbitMQ", "Tnzi.RabbitMQ", "RabbitMQEventBusModule")]
    [InlineData("rabbitmq", "Tnzi.RabbitMQ", "RabbitMQEventBusModule")]
    [InlineData("Kafka", "Tnzi.Kafka", "KafkaEventBusModule")]
    public async Task TransportConfiguredButNotLoaded_FailsStartupNamingThePackage(string type, string package, string module)
    {
        using var provider = BuildProvider(type, distributedBusRegistered: false);

        var ex = await Assert.ThrowsAsync<ConfigurationException>(() =>
            new EventBusModule().OnApplicationInitializationAsync(new ApplicationInitializationContext(provider)));

        Assert.Contains("EventBus:Type", ex.Message);
        Assert.Contains(package, ex.Message);
        Assert.Contains(module, ex.Message);
    }

    [Theory]
    [InlineData("RabbitMQ")]
    [InlineData("Kafka")]
    public async Task TransportConfiguredAndDistributedBusRegistered_Starts(string type)
    {
        // 只看「有没有分布式总线」，不看具体实现类型：自定义传输是正当做法
        using var provider = BuildProvider(type, distributedBusRegistered: true);

        await new EventBusModule().OnApplicationInitializationAsync(new ApplicationInitializationContext(provider));
    }

    [Theory]
    [InlineData("Local")]
    [InlineData("local")]
    public async Task LocalConfigured_StartsWithoutADistributedBus(string type)
    {
        using var provider = BuildProvider(type, distributedBusRegistered: false);

        await new EventBusModule().OnApplicationInitializationAsync(new ApplicationInitializationContext(provider));
    }

    [Fact]
    public async Task CheckDoesNotConstructTheDistributedBus()
    {
        // 启动自证只问「注册了没有」：构造分布式总线会在代理不可达时让启动失败，这不是这道检查该管的事
        var services = new ServiceCollection();
        services.AddOptions<EventBusOptions>().Configure(o => o.Type = "RabbitMQ");
        var constructed = false;
        services.AddSingleton<IDistributedEventBus>(_ =>
        {
            constructed = true;
            return new Mock<IDistributedEventBus>().Object;
        });
        using var provider = services.BuildServiceProvider();

        await new EventBusModule().OnApplicationInitializationAsync(new ApplicationInitializationContext(provider));

        Assert.False(constructed);
    }

    [Theory]
    [InlineData("RabbitMQ")]
    [InlineData("Kafka")]
    public void Validator_DoesNotRequireTheConnectionKeyItCanSee(string type)
    {
        // 传输模块还认 ConnectionStrings:RabbitMQ / ConnectionStrings:Kafka / Kafka:BootstrapServers，
        // 校验器只看得见 EventBus 节里的那一个。在这里硬性要求它，会让按文档只配备选来源的部署启动即失败；
        // 来源全缺由传输模块在解析处报，错误消息列出它认的全部键。
        var result = new EventBusOptionsValidator().Validate(null, new EventBusOptions { Type = type });

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Fact]
    public void Validator_StillRejectsAnUnknownType()
    {
        var result = new EventBusOptionsValidator().Validate(null, new EventBusOptions { Type = "Nats" });

        Assert.False(result.Succeeded);
        Assert.Contains("EventBus.Type", result.FailureMessage);
    }

    private static ServiceProvider BuildProvider(string type, bool distributedBusRegistered)
    {
        var services = new ServiceCollection();
        services.AddOptions<EventBusOptions>().Configure(o => o.Type = type);

        if (distributedBusRegistered)
        {
            services.AddSingleton(new Mock<IDistributedEventBus>().Object);
        }

        return services.BuildServiceProvider();
    }
}
