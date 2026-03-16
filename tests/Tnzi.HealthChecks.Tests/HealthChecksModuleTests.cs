using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Tnzi.Modules;

namespace Tnzi.HealthChecks.Tests;

public class HealthChecksModuleTests
{
    [Fact]
    public async Task ConfigureServicesAsync_WithEnabledCacheCheck_RegistersHealthChecks()
    {
        var module = new HealthChecksModule();
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HealthChecks:Enabled"] = "true",
                ["HealthChecks:EnableCacheCheck"] = "true",
                ["HealthChecks:EnableDatabaseCheck"] = "false",
                ["HealthChecks:EnableRedisCheck"] = "false",
                ["HealthChecks:EnableEventBusCheck"] = "false",
            })
            .Build();

        var context = new ServiceConfigurationContext(services, configuration);
        await module.PreConfigureServicesAsync(context);
        await module.ConfigureServicesAsync(context);
        await module.PostConfigureServicesAsync(context);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;

        Assert.Equal(ModuleType.Framework, module.ModuleType);
        Assert.Equal(50, module.LoadOrder);
        Assert.Contains(options.Registrations, registration => registration.Name == "cache");
    }

    [Fact]
    public async Task ConfigureServicesAsync_WithDisabledHealthChecks_DoesNotRegisterChecks()
    {
        var module = new HealthChecksModule();
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HealthChecks:Enabled"] = "false",
            })
            .Build();

        var context = new ServiceConfigurationContext(services, configuration);
        await module.PreConfigureServicesAsync(context);
        await module.ConfigureServicesAsync(context);
        await module.PostConfigureServicesAsync(context);

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(HealthCheckService));
    }
}
