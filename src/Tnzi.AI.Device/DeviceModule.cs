using Tnzi.AI.Device.Options;
using Tnzi.AI.Device.Services;
using Tnzi.AI.Device.Tools;
using Tnzi.AI.Device.Transport;

namespace Tnzi.AI.Device;

/// <summary>
/// Device module — provides device node abstraction for AI agents to invoke device capabilities
/// </summary>
[DependsOn(typeof(AIModule))]
public class DeviceModule : TnziApplicationModule
{
    /// <summary>
    /// Load order (after AI sub-modules: Skills=51, Workflow=52, Mcp=53, Coder=54, Rag=55, Cli=56, Sandbox=57, Channels=58)
    /// </summary>
    public override int LoadOrder => 60;

    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.AddOptions<DeviceOptions>()
            .Bind(context.Configuration.GetSection("AI:Device"))
            .ValidateWith<DeviceOptions, DeviceOptionsValidator>();

        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 始终注册服务（保持 DI 容器完整性），运行时行为由 Options.Enabled 控制
        var services = context.Services;
        services.AddSingleton<IDeviceRegistry, DeviceRegistry>();
        services.AddSingleton<IDevicePairingService, DevicePairingService>();
        services.AddSingleton<DeviceTools>();

        return Task.CompletedTask;
    }

    public override Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        var options = context.ServiceProvider.GetRequiredService<IOptions<DeviceOptions>>().Value;
        if (!options.Enabled)
        {
            return Task.CompletedTask;
        }

        // Register local device node if enabled
        if (options.LocalNodeEnabled)
        {
            var registry = context.ServiceProvider.GetRequiredService<IDeviceRegistry>();
            registry.Register(new LocalDeviceNode());
        }

        // Scan and register AI tools (only when enabled)
        var logger = context.ServiceProvider.GetRequiredService<ILogger<DeviceModule>>();
        AIToolRegistration.ScanAndRegisterAITools(context.ServiceProvider, typeof(DeviceModule).Assembly, logger);

        return Task.CompletedTask;
    }
}
