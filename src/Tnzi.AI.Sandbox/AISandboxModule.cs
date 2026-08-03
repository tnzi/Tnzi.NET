using Tnzi.AI.Sandbox.Events.Handlers;
using Tnzi.AI.Sandbox.Tools;
using Tnzi.Audit;

namespace Tnzi.AI.Sandbox;

/// <summary>
/// 沙箱执行环境模块 - 提供 Local/Docker/Kubernetes 三层沙箱
/// </summary>
/// <remarks>
/// <c>[OptionalDependsOn(AuditModule)]</c> ensures audit infrastructure loads
/// first when present, so the <c>SandboxCommandAuditHandler</c> can resolve
/// <c>IAuditSender</c>; if the audit module is not loaded the handler degrades
/// gracefully (no-op) because the dependency is constructor-optional.
/// </remarks>
[DependsOn(typeof(AIModule))]
[OptionalDependsOn(typeof(AuditModule))]
public class AISandboxModule : TnziApplicationModule
{
    public override int LoadOrder => 57;

    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.AddTnziOptions<SandboxModuleOptions, SandboxModuleOptionsValidator>(context.Configuration, "AI:Sandbox");
        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // Code-declared permissions for this module's admin surfaces - the
        // Authorization module's PermissionDbSeeder picks every registered
        // provider up on startup (no-op when Authorization is not loaded).
        context.Services.AddTransient<IPermissionDefinitionProvider, SandboxPermissions>();

        // Virtual path translator - resolve options via IOptions at DI resolution time
        context.Services.AddSingleton<IVirtualPathTranslator>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<SandboxModuleOptions>>().Value;
            return new VirtualPathTranslator(opts.DataRoot);
        });

        // Sandbox provider - determine at registration time from config
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

        // Thread-scoped resource quota (cache-backed, no DB persistence)
        context.Services.AddSingleton<IThreadResourceQuota, ThreadResourceQuotaService>();

        // Tools
        context.Services.AddScoped<SandboxTools>();
        // Explicit accessor for the active sandbox (F8) - lets consumers inject
        // ISandboxAccessor instead of reaching into the AsyncLocal channel.
        // Stateless (reads the singleton AsyncLocal accessor live) → Singleton.
        context.Services.AddSingleton<ISandboxAccessor, SandboxAccessor>();

        // Middlewares
        context.Services.AddScoped<IAiMiddleware, ThreadDataMiddleware>();
        context.Services.AddScoped<IAiMiddleware, SandboxMiddleware>();

        // Events - sandbox command execution audit (uses optional IAuditSender)
        context.Services.AddEventHandler<SandboxCommandExecutedEvent, SandboxCommandAuditHandler>();

        return Task.CompletedTask;
    }

    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        var logger = context.ServiceProvider.GetRequiredService<ILogger<AISandboxModule>>();
        var options = context.ServiceProvider.GetRequiredService<IOptions<SandboxModuleOptions>>().Value;

        // Register sandbox tools only when the sandbox is enabled - a disabled
        // sandbox must not surface tools that can never obtain an environment.
        if (options.Enabled)
        {
            AIToolRegistration.ScanAndRegisterAITools(context.ServiceProvider, typeof(AISandboxModule).Assembly, logger);
        }
        else
        {
            logger.LogInformation("Sandbox is disabled (AI:Sandbox:Enabled=false); sandbox tools were not registered");
        }

        // Populate the shared skill resource root once at startup so that
        // ThreadDataMiddleware can symlink each thread to it rather than
        // copying every skill's files for every new thread.
        var skillStore = context.ServiceProvider.GetService<ISkillStore>();
        if (skillStore != null && options.Enabled && !string.IsNullOrWhiteSpace(options.DataRoot))
        {
            await SharedSkillExtractor.ExtractAsync(skillStore, options.DataRoot, logger);
        }
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
            .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
            {
                // .NET handles HTTP/1.1 framing; we only supply the underlying transport
                // (unix socket / Windows named pipe / TCP) via ConnectCallback.
                ConnectCallback = DockerSocketConnector.CreateConnectCallback(dockerHost),
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            })
            .ConfigureHttpClient(client =>
            {
                // Docker API 需要一个 base address，但实际连接通过 SocketsHttpHandler.ConnectCallback
                client.BaseAddress = new Uri("http://localhost/v1.45");
                client.Timeout = TimeSpan.FromMinutes(5);
            });
    }
}
