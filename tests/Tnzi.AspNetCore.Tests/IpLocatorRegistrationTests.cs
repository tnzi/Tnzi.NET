using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.Modules;
using Tnzi.Services;

namespace Tnzi.AspNetCore.Tests;

/// <summary>
/// 框架不得默认把访问者 IP 交给第三方定位服务。
/// </summary>
/// <remarks>
/// 自带的 <see cref="IpLocatorService"/> 以明文 HTTP 把 IP 发给一个免费的公共接口。它曾在没有别的实现时被默认注册，
/// 于是任何 Web 宿主一打开访问日志，每个公网来访 IP 就被交给第三方，而文档只写着「补 IP 归属地」。
/// 现在它只是一个消费方可以显式注册的实现；没注册时访问日志的归属地列留空。
/// </remarks>
public class IpLocatorRegistrationTests
{
    private static async Task<IServiceCollection> ConfigureAsync(Action<IServiceCollection>? before = null)
    {
        var services = new ServiceCollection();
        before?.Invoke(services);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var context = new ServiceConfigurationContext(services, configuration);

        var module = new AspNetCoreModule();
        await module.PreConfigureServicesAsync(context);
        await module.ConfigureServicesAsync(context);
        return services;
    }

    [Fact]
    public async Task WithoutAnExplicitRegistration_NoIpLocatorIsRegistered()
    {
        var services = await ConfigureAsync();

        Assert.DoesNotContain(services, s => s.ServiceType == typeof(IIpLocatorService));
    }

    [Fact]
    public async Task AnExplicitRegistration_IsKept()
    {
        var services = await ConfigureAsync(s => s.AddScoped<IIpLocatorService, IpLocatorService>());

        var descriptor = Assert.Single(services, s => s.ServiceType == typeof(IIpLocatorService));
        Assert.Equal(typeof(IpLocatorService), descriptor.ImplementationType);
    }

    [Fact]
    public async Task TheUserAgentParser_IsStillRegisteredByDefault()
    {
        var services = await ConfigureAsync();

        Assert.Contains(services, s => s.ServiceType == typeof(IUserAgentParserService));
    }
}
