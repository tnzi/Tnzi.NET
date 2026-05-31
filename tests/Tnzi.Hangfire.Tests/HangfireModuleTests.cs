using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tnzi.BackgroundJobs;
using Tnzi.Modules;
using Tnzi.MultiTenancy;

namespace Tnzi.Hangfire.Tests;

public class HangfireModuleTests
{
    [Fact]
    public async Task ConfigureServicesAsync_WithMemoryStorage_RegistersBackgroundJobManager()
    {
        var module = new HangfireModule();
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Hangfire:Enabled"] = "true",
                ["Hangfire:StorageType"] = "Memory",
                ["Hangfire:Server:WorkerCount"] = "1",
                ["Hangfire:Dashboard:Enabled"] = "false",
            })
            .Build();

        var context = new ServiceConfigurationContext(services, configuration);
        await module.PreConfigureServicesAsync(context);
        await module.ConfigureServicesAsync(context);

        Assert.Equal(ModuleType.Infrastructure, module.ModuleType);
        Assert.Equal(50, module.LoadOrder);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IBackgroundJobManager));
    }

    [Fact]
    public void HangfireBackgroundJobManager_AsSingleton_PassesScopeValidation()
    {
        // Regression: HangfireBackgroundJobManager 注册为 Singleton 时不能持有
        // Scoped 的 ICurrentTenant，否则在 ValidateScopes=true 下会抛
        // InvalidOperationException ("Cannot consume scoped service ... from singleton ...")。
        var services = new ServiceCollection();
        services.AddScoped<ICurrentTenant, CurrentTenant>();
        services.AddSingleton<IBackgroundJobManager, HangfireBackgroundJobManager>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

        var manager = provider.GetRequiredService<IBackgroundJobManager>();
        Assert.IsType<HangfireBackgroundJobManager>(manager);
    }

    [Fact]
    public async Task ConfigureServicesAsync_WithDisabledHangfire_SkipsBackgroundJobManager()
    {
        var module = new HangfireModule();
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Hangfire:Enabled"] = "false",
            })
            .Build();

        var context = new ServiceConfigurationContext(services, configuration);
        await module.PreConfigureServicesAsync(context);
        await module.ConfigureServicesAsync(context);

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IBackgroundJobManager));
    }
}
