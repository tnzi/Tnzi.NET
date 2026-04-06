using Tnzi.AI;
using Tnzi.AI.Sandbox.Tools;

namespace Tnzi.AI.Sandbox;

/// <summary>
/// 沙箱执行环境模块 — 提供 Local/Docker/Kubernetes 三层沙箱
/// </summary>
[DependsOn(typeof(AIModule))]
public class SandboxModule : TnziApplicationModule
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
                RegisterDockerHttpClient(context);
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

    private static void RegisterDockerHttpClient(ServiceConfigurationContext context)
    {
        var dockerHost = context.Configuration.GetSection("AI:Sandbox:Docker:DockerHost").Value;
        if (string.IsNullOrWhiteSpace(dockerHost))
        {
            dockerHost = OperatingSystem.IsWindows()
                ? "npipe:////./pipe/docker_engine"
                : "unix:///var/run/docker.sock";
        }

        context.Services.AddHttpClient(DockerSandboxProvider.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new DockerSocketHandler(dockerHost))
            .ConfigureHttpClient(client =>
            {
                // Docker API 需要一个 base address，但实际连接通过 socket handler
                client.BaseAddress = new Uri("http://localhost/v1.45");
                client.Timeout = TimeSpan.FromMinutes(5);
            });
    }
}
