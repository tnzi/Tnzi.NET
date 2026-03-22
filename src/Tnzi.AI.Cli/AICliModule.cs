namespace Tnzi.AI.Cli;

/// <summary>
/// AI CLI 模块 — 提供 ExternalCli 执行模式，将 Agent 请求委托给外部 CLI 工具（Claude Code, Codex 等）
/// </summary>
[DependsOn(typeof(AIModule))]
public class AICliModule : TnziCustomModule
{
    public override int LoadOrder => 56;

    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        context.Services.AddOptions<CliOptions>()
            .Bind(context.Configuration.GetSection("AI:Cli"))
            .ValidateWith<CliOptions, CliOptionsValidator>();

        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // CLI 提供者（Singleton，无状态）
        context.Services.AddSingleton<ICliAgentProvider, ClaudeCodeCliProvider>();

        // 进程管理器（Singleton，仅依赖 ILogger）
        context.Services.AddSingleton<CliProcessManager>();

        // 工作目录验证器（Scoped，依赖 IOptions）
        context.Services.AddScoped<WorkingDirectoryValidator>();

        // 执行策略 + IExternalCliExecutor 接口注册
        context.Services.AddScoped<ExternalCliExecutionStrategy>();
        context.Services.AddScoped<IExternalCliExecutor>(sp =>
            sp.GetRequiredService<ExternalCliExecutionStrategy>());

        return Task.CompletedTask;
    }
}
