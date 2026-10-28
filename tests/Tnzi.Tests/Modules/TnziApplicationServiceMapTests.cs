using Microsoft.Extensions.Configuration;

namespace Tnzi.Tests.Modules;

/// <summary>
/// 每个模块在 Configure / PostConfigure 阶段注册了哪些描述符（模块 Manifest、运行时设置消费审计、
/// 模块依赖审计的唯一输入）。模块里先 <c>RemoveAll</c> 掉别的模块的注册再 <c>Add</c> 是常见写法：
/// 按「调用前的数量」取下标之后的描述符会因为列表左移漏掉本模块最早的那几条注册。
/// </summary>
public class TnziApplicationServiceMapTests
{
    [Fact]
    public async Task ConfigureServices_RecordsEveryRegistration_WhenAModuleRemovesAnEarlierOne()
    {
        var app = await ConfigureAsync(typeof(ReplacingConsumerModule));

        var consumer = Manifest<ReplacingConsumerModule>(app);
        Assert.Contains(consumer.Services, s => s.InterfaceType == typeof(IServiceMapBar) && s.ImplementationType == typeof(ServiceMapBar));
        Assert.Contains(consumer.Services, s => s.InterfaceType == typeof(IServiceMapFoo) && s.ImplementationType == typeof(ServiceMapFooReplacement));
    }

    [Fact]
    public async Task ConfigureServices_KeepsTheOriginalProvider_OfAReplacedRegistration()
    {
        var app = await ConfigureAsync(typeof(ReplacingConsumerModule));

        // 被后来的模块替换掉的注册仍记在原模块名下：依赖审计把全部注册模块都算作候选，
        // 可替换实现的原始提供者因此仍然算数（否则用到该服务的模块都会被报成依赖了替换方）
        var provider = Manifest<ServiceMapProviderModule>(app);
        Assert.Contains(provider.Services, s => s.InterfaceType == typeof(IServiceMapFoo) && s.ImplementationType == typeof(ServiceMapFoo));
        Assert.Contains(provider.Services, s => s.InterfaceType == typeof(IServiceMapBaz));
    }

    [Fact]
    public async Task PostConfigureServices_RecordsEveryRegistration_WhenAModuleRemovesAnEarlierOne()
    {
        var app = await ConfigureAsync(typeof(PostReplacingConsumerModule));

        var consumer = Manifest<PostReplacingConsumerModule>(app);
        Assert.Contains(consumer.Services, s => s.InterfaceType == typeof(IServiceMapBar) && s.ImplementationType == typeof(ServiceMapBar));
        Assert.Contains(consumer.Services, s => s.InterfaceType == typeof(IServiceMapFoo) && s.ImplementationType == typeof(ServiceMapFooReplacement));
    }

    private static async Task<TnziApplication> ConfigureAsync(Type startupModule)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder().Build();

        var app = new TnziApplication(startupModule, services, configuration);
        await app.ConfigureServicesAsync();
        await app.InitializeAsync(services.BuildServiceProvider());
        return app;
    }

    private static ModuleManifest Manifest<TModule>(TnziApplication app)
        => app.Modules.Single(m => m.Type == typeof(TModule)).Manifest;
}

public interface IServiceMapFoo { }

public interface IServiceMapBar { }

public interface IServiceMapBaz { }

public sealed class ServiceMapFoo : IServiceMapFoo { }

public sealed class ServiceMapFooReplacement : IServiceMapFoo { }

public sealed class ServiceMapBar : IServiceMapBar { }

public sealed class ServiceMapBaz : IServiceMapBaz { }

public class ServiceMapProviderModule : TnziCustomModule
{
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.AddSingleton<IServiceMapFoo, ServiceMapFoo>();
        context.Services.AddSingleton<IServiceMapBaz, ServiceMapBaz>();
        return Task.CompletedTask;
    }
}

[DependsOn(typeof(ServiceMapProviderModule))]
public class ReplacingConsumerModule : TnziCustomModule
{
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.AddSingleton<IServiceMapBar, ServiceMapBar>();
        context.Services.RemoveAll<IServiceMapFoo>();
        context.Services.AddSingleton<IServiceMapFoo, ServiceMapFooReplacement>();
        return Task.CompletedTask;
    }
}

[DependsOn(typeof(ServiceMapProviderModule))]
public class PostReplacingConsumerModule : TnziCustomModule
{
    public override Task PostConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.AddSingleton<IServiceMapBar, ServiceMapBar>();
        context.Services.RemoveAll<IServiceMapFoo>();
        context.Services.AddSingleton<IServiceMapFoo, ServiceMapFooReplacement>();
        return Task.CompletedTask;
    }
}
