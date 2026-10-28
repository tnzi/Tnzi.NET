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

        // Thread directory provisioning (directories + per-thread skill copies). Runs
        // inside the sandbox environment on first use, never at middleware entry.
        context.Services.AddScoped<IThreadDataProvisioner, ThreadDataProvisioner>();

        // Middlewares
        context.Services.AddScoped<IAiMiddleware, ThreadDataMiddleware>();
        context.Services.AddScoped<IAiMiddleware, SandboxMiddleware>();

        // Events - sandbox command execution audit (uses optional IAuditSender)
        context.Services.AddEventHandler<SandboxCommandExecutedEvent, SandboxCommandAuditHandler>();

        // Events - a deleted thread's sandbox directory and quota counters go with it
        context.Services.AddEventHandler<ThreadDeletedEvent, SandboxThreadCleanupHandler>();

        return Task.CompletedTask;
    }

    public override Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
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

        return Task.CompletedTask;
    }

    private static void RegisterDockerHttpClient(ServiceConfigurationContext context)
    {
        context.Services.AddHttpClient(DockerSandboxProvider.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(sp => new System.Net.Http.SocketsHttpHandler
            {
                // .NET handles HTTP/1.1 framing; we only supply the underlying transport
                // (unix socket / Windows named pipe / TCP) via ConnectCallback.
                // 地址取绑定后的选项（配置与代码方式的 Configure 都算），而不是只读原始配置键。
                ConnectCallback = DockerSocketConnector.CreateConnectCallback(
                    sp.GetRequiredService<IOptions<SandboxModuleOptions>>().Value.Docker.ResolveDockerHost()),
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            })
            .ConfigureHttpClient(client =>
            {
                // 实际连接经 ConnectCallback，基址只是 HttpClient 拼地址用的。请求一律不带 API 版本前缀，
                // daemon 按它自己的当前版本应答：钉一个版本，比它旧的 daemon 全部拒绝（版本太新），
                // 而新版 daemon 又会把过旧的版本拒之门外。★ 此前基址写成 /v1.45，看起来钉了版本，
                // 但所有请求都用 / 开头的路径，按 URI 规则整段替换掉基址路径，版本前缀从未发出过。
                client.BaseAddress = new Uri("http://localhost/");
                client.Timeout = TimeSpan.FromMinutes(5);
            });
    }
}
