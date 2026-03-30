using Tnzi.AI;
using Tnzi.AI.Sandbox.Tools;

namespace Tnzi.AI.Sandbox;

/// <summary>
/// 沙箱执行环境模块 — 提供 Local/Docker/Kubernetes 三层沙箱
/// </summary>
[DependsOn(typeof(AIModule))]
public class SandboxModule : TnziCustomModule
{
    public override int LoadOrder => 57;

    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.AddOptions<SandboxModuleOptions>()
            .Bind(context.Configuration.GetSection("AI:Sandbox"))
            .ValidateWith<SandboxModuleOptions, SandboxModuleOptionsValidator>();
        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // Virtual path translator — resolve options via IOptions at DI resolution time
        context.Services.AddSingleton<IVirtualPathTranslator>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<SandboxModuleOptions>>().Value;
            return new VirtualPathTranslator(opts.DataRoot);
        });

        // Sandbox provider — determine at registration time from config
        var providerName = context.Configuration.GetSection("AI:Sandbox:Provider").Value?.ToLowerInvariant() ?? "local";
        switch (providerName)
        {
            case "docker":
                context.Services.AddSingleton<ISandboxProvider, DockerSandboxProvider>();
                break;
            case "kubernetes":
                context.Services.AddSingleton<ISandboxProvider, KubernetesSandboxProvider>();
                break;
            default:
                context.Services.AddSingleton<ISandboxProvider, LocalSandboxProvider>();
                break;
        }

        // Tools
        context.Services.AddScoped<SandboxTools>();

        // Middlewares
        context.Services.AddScoped<IAiMiddleware, ThreadDataMiddleware>();
        context.Services.AddScoped<IAiMiddleware, SandboxMiddleware>();

        return Task.CompletedTask;
    }
}
