using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.AspNetCore.Extensions;
using Tnzi.Modules;

namespace Tnzi.AspNetCore.Tests.Mvc;

/// <summary>
/// <c>AddControllerAssembly&lt;T&gt;()</c> 此前在服务集合里找 <c>IMvcBuilder</c>，而它从来不是服务：
/// 每次调用都静默返回，插件程序集里的控制器全部 404 且没有任何日志。
/// </summary>
public class AddControllerAssemblyTests
{
    private static ServiceConfigurationContext NewContext(IServiceCollection services)
        => new(services, new ConfigurationBuilder().Build());

    private static ApplicationPartManager PartManagerOf(IServiceCollection services)
        => (ApplicationPartManager)services.Last(s => s.ServiceType == typeof(ApplicationPartManager)).ImplementationInstance!;

    [Fact]
    public void AfterMvcIsRegistered_TheAssemblyBecomesAnApplicationPart()
    {
        var services = new ServiceCollection();
        services.AddControllers();
        var pluginAssembly = typeof(Mock).Assembly;
        Assert.DoesNotContain(PartManagerOf(services).ApplicationParts.OfType<AssemblyPart>(), p => p.Assembly == pluginAssembly);

        NewContext(services).AddControllerAssembly<Mock>();

        Assert.Contains(PartManagerOf(services).ApplicationParts.OfType<AssemblyPart>(), p => p.Assembly == pluginAssembly);
    }

    [Fact]
    public void CallingItTwice_RegistersTheAssemblyOnce()
    {
        var services = new ServiceCollection();
        services.AddControllers();
        var context = NewContext(services);

        context.AddControllerAssembly<Mock>().AddControllerAssembly<Mock>();

        Assert.Single(PartManagerOf(services).ApplicationParts.OfType<AssemblyPart>(), p => p.Assembly == typeof(Mock).Assembly);
    }

    [Fact]
    public void BeforeMvcIsRegistered_ItFailsLoudly()
    {
        var context = NewContext(new ServiceCollection());

        var error = Assert.Throws<InvalidOperationException>(() => context.AddControllerAssembly<Mock>());

        Assert.Contains("MVC is not registered", error.Message);
    }
}
