using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.Modules;

namespace Tnzi.SignalR.Tests;

/// <summary>
/// 模块真的把服务注册进去了。
///
/// ★ 单独断言实现类的行为看不见"忘了注册这一行"：契约测得再全，缺了 DI 那一行的话
/// 运行时解析不到，而调用点（Hub 里是可选解析、控制器里是可选参数）会安静地退化成
/// 什么都不做。所以过一遍模块自己的 <c>ConfigureServicesAsync</c>。
/// </summary>
public class SignalRModuleRegistrationTests
{
    private static ServiceProvider BuildServices(Dictionary<string, string?>? settings = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings ?? [])
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        // CachingModule 提供的依赖，本测试只关心 SignalR 自己注册了什么
        services.AddSingleton(Mock.Of<ICache>());

        var module = new SignalRModule();
        module.PreConfigureServicesAsync(new ServiceConfigurationContext(services, configuration))
            .GetAwaiter().GetResult();
        module.ConfigureServicesAsync(new ServiceConfigurationContext(services, configuration))
            .GetAwaiter().GetResult();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void ConnectionManagerIsRegistered()
    {
        using var provider = BuildServices();

        provider.GetService<IConnectionManager>().ShouldBeOfType<ConnectionManager>();
    }

    /// <summary>
    /// 中断表缺席时强制下线会退回"只清登记"，而那正是它要修的行为 —— 且不报错。
    /// </summary>
    [Fact]
    public void ConnectionAborterIsRegistered()
    {
        using var provider = BuildServices();

        provider.GetService<IHubConnectionAborter>().ShouldBeOfType<HubConnectionAborter>();
    }

    [Fact]
    public void ConnectionAborterIsASingleton_SoEveryHubSharesOneRegistry()
    {
        using var provider = BuildServices();

        provider.GetRequiredService<IHubConnectionAborter>()
            .ShouldBeSameAs(provider.GetRequiredService<IHubConnectionAborter>());
    }

    [Fact]
    public void RateLimitServiceIsRegisteredOnlyWhenRateLimitingIsEnabled()
    {
        using var off = BuildServices();
        off.GetService<IRateLimitService>().ShouldBeNull();

        using var on = BuildServices(new Dictionary<string, string?>
        {
            ["SignalR:RateLimit:Enabled"] = "true",
        });
        on.GetService<IRateLimitService>().ShouldNotBeNull();
    }
}
