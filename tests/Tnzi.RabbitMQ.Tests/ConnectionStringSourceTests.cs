using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using Tnzi.Modules;
using Tnzi.RabbitMQ.Exceptions;

namespace Tnzi.RabbitMQ.Tests;

/// <summary>
/// 连接串的来源顺序：<c>EventBus:RabbitMqConnectionString</c> &gt; <c>ConnectionStrings:RabbitMQ</c>。
/// </summary>
/// <remarks>
/// 配置样例里 <c>EventBus:RabbitMqConnectionString</c> 写成空串。若只在 null 时才看下一个来源，
/// 按文档改配 <c>ConnectionStrings:RabbitMQ</c>（容器与编排平台经环境变量注入的常见做法）的部署会在空串上
/// 报「connection string cannot be empty」启动失败，而它要的连接串明明配好了。
/// </remarks>
public class ConnectionStringSourceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ConnectionStringsEntryIsUsed_WhenTheEventBusKeyIsMissingOrBlank(string? eventBusValue)
    {
        await using var provider = await BuildProviderAsync(new Dictionary<string, string?>
        {
            ["EventBus:RabbitMqConnectionString"] = eventBusValue,
            ["ConnectionStrings:RabbitMQ"] = "amqp://guest:guest@broker-a:5672/",
        });

        HostOf(provider).ShouldBe("broker-a");
    }

    [Fact]
    public async Task EventBusKey_WinsOverTheConnectionStringsEntry()
    {
        await using var provider = await BuildProviderAsync(new Dictionary<string, string?>
        {
            ["EventBus:RabbitMqConnectionString"] = "amqp://guest:guest@broker-b:5672/",
            ["ConnectionStrings:RabbitMQ"] = "amqp://guest:guest@broker-a:5672/",
        });

        HostOf(provider).ShouldBe("broker-b");
    }

    [Fact]
    public async Task NoSourceAtAll_FailsNamingBothKeys()
    {
        var ex = await Should.ThrowAsync<RabbitMQConnectionException>(() => BuildProviderAsync(new Dictionary<string, string?>
        {
            ["EventBus:RabbitMqConnectionString"] = "",
        }));

        ex.Message.ShouldContain("EventBus.RabbitMqConnectionString");
        ex.Message.ShouldContain("ConnectionStrings:RabbitMQ");
    }

    private static string HostOf(IServiceProvider provider)
        => ((ConnectionFactory)provider.GetRequiredService<IConnectionFactory>()).HostName;

    private static async Task<ServiceProvider> BuildProviderAsync(Dictionary<string, string?> settings)
    {
        settings["EventBus:Type"] = "RabbitMQ";
        settings["EventBus:ConsumerGroup"] = "tests-app";

        var module = new RabbitMQEventBusModule();
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var context = new ServiceConfigurationContext(services, configuration);
        await module.PreConfigureServicesAsync(context);
        await module.ConfigureServicesAsync(context);

        return services.BuildServiceProvider();
    }
}
