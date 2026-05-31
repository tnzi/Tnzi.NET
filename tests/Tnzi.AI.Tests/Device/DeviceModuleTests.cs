using Tnzi.AI.Device;
using Tnzi.AI.Device.Options;
using Tnzi.AI.Device.Services;
using Tnzi.AI.Device.Tools;
using Tnzi.Modules;

namespace Tnzi.AI.Tests.Device;

public class DeviceModuleTests
{
    [Fact]
    public void Module_HasCorrectLoadOrder()
    {
        var module = new DeviceModule();

        module.LoadOrder.ShouldBe(60);
    }

    [Fact]
    public void Module_DependsOnAIModule()
    {
        var moduleType = typeof(DeviceModule);
        var dependsOnAttrs = moduleType.GetCustomAttributes(typeof(DependsOnAttribute), true);

        dependsOnAttrs.ShouldNotBeEmpty();
        var attr = (DependsOnAttribute)dependsOnAttrs[0];
        attr.DependedModuleTypes.ShouldContain(typeof(AIModule));
    }

    [Fact]
    public void ConfigureServicesAsync_RegistersServices()
    {
        var services = CreateServiceCollection();

        services.Any(d => d.ServiceType == typeof(IDeviceRegistry)).ShouldBeTrue();
        services.Any(d => d.ServiceType == typeof(IDevicePairingService)).ShouldBeTrue();
        services.Any(d => d.ServiceType == typeof(DeviceTools)).ShouldBeTrue();
    }

    [Fact]
    public void ConfigureServicesAsync_Disabled_StillRegistersServices()
    {
        // 始终注册服务（保持 DI 容器完整性），运行时行为由 Options.Enabled 控制
        var services = new ServiceCollection();
        services.AddLogging();

        var module = new DeviceModule();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:Device:Enabled"] = "false"
            })
            .Build();

        services.AddOptions<DeviceOptions>()
            .Bind(configuration.GetSection("AI:Device"));

        var context = new ServiceConfigurationContext(services, configuration);
        module.ConfigureServicesAsync(context).GetAwaiter().GetResult();

        services.Any(d => d.ServiceType == typeof(IDeviceRegistry)).ShouldBeTrue();
    }

    [Fact]
    public void ConfigureServicesAsync_ServicesAreSingleton()
    {
        var services = CreateServiceCollection();

        var registryDesc = services.First(d => d.ServiceType == typeof(IDeviceRegistry));
        registryDesc.Lifetime.ShouldBe(ServiceLifetime.Singleton);

        var pairingDesc = services.First(d => d.ServiceType == typeof(IDevicePairingService));
        pairingDesc.Lifetime.ShouldBe(ServiceLifetime.Singleton);

        var toolsDesc = services.First(d => d.ServiceType == typeof(DeviceTools));
        toolsDesc.Lifetime.ShouldBe(ServiceLifetime.Singleton);
    }

    [Fact]
    public void DeviceOptions_Default_LocalNodeEnabled_IsFalse()
    {
        var options = new DeviceOptions();

        options.LocalNodeEnabled.ShouldBeFalse();
    }

    private static ServiceCollection CreateServiceCollection()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var module = new DeviceModule();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:Device:Enabled"] = "true"
            })
            .Build();

        services.AddOptions<DeviceOptions>()
            .Bind(configuration.GetSection("AI:Device"));

        var context = new ServiceConfigurationContext(services, configuration);
        module.ConfigureServicesAsync(context).GetAwaiter().GetResult();
        return services;
    }
}
