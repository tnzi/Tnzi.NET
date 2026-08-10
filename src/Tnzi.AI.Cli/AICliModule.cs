namespace Tnzi.AI.Cli;

/// <summary>
/// 外部 CLI agent 运行时子模块：把 Claude Code、Codex 以及所有说 ACP 的编码 CLI
/// 变成框架可调度的 agent 运行时。
/// </summary>
/// <remarks>
/// <para>
/// <b>与已删除的同名旧模块（≤ 0.1.26）名字相同、架构完全不同。</b>旧版把外部执行做成
/// <c>AgentExecutionMode</c> 的一个取值，于是它要和内建执行共用同一条中间件管线 ——
/// 结果是 15 个中间件里散落 28 处「跳过这个中间件」的补丁，最终整体删除
/// （归档 tag <c>archive/pre-ai-client-removal</c>）。
/// </para>
/// <para>本模块的三条红线，任何一条被打破都会重演那次失败：</para>
/// <list type="number">
/// <item><b>不进 <c>AiMiddlewareContext</c> 管线。</b>外部执行是与 <c>IAgentExecutor</c>
/// <b>平级</b>的独立执行域，不是它的一个模式。禁止新增 <c>AgentExecutionMode.ExternalCli</c>，
/// 禁止任何 <c>ShouldSkipMiddleware</c> 类开关。可复用的只有<b>结果侧</b>能力
/// （成本、预算、用量、审计）—— 请求侧的 RAG 注入、工具装配、guardrail 对外部 agent
/// 本就不适用，不该靠 skip 绕过。</item>
/// <item><b>路由分支只允许出现在 <see cref="IAgentDispatchFacade"/> 一处。</b>
/// 约定测试 <c>CliAgentRedLineTests</c> 守着它。</item>
/// <item><b>不复用 <c>ISandbox.ExecuteCommandAsync</c>（批量非交互，装不下 ACP）
/// 与 Channels 的 <c>IGateway</c>（语义是入站消息路由，不是任务下发）。</b></item>
/// </list>
/// <para>
/// <b>默认关闭</b>（<c>AI:Cli:Enabled=false</c>）：外部 agent 等于任意代码执行能力，
/// 必须显式 opt-in。
/// </para>
/// </remarks>
[DependsOn(typeof(AIModule))]
// ISkillService 的契约与 NoOp 回退都在 AIModule（已 [DependsOn]）；真实实现来自可选的
// AISkillsModule。审计只看到「注册者是 AISkillsModule」，但本模块并不依赖它 ——
// Skills 没加载时解析到的是 AIModule 的 NoOp 回退，这正是预期行为。
[SuppressDependencyAudit("Contract and NoOp fallback both live in AIModule; AISkillsModule is an optional provider", IgnoredServiceType = typeof(ISkillService))]
public class AICliModule : TnziApplicationModule
{
    /// <inheritdoc />
    /// <remarks>与 AI 核心共享前缀：拆的是程序集，不是 schema。</remarks>
    public override string? TableNamePrefix => "AI";

    /// <inheritdoc />
    /// <remarks>59：在 AI(50) 与全部既有 AI 子模块（51-58）之后。</remarks>
    public override int LoadOrder => 59;

    /// <inheritdoc />
    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        Check.NotNull(context);
        context.Services.AddTnziOptions<CliAgentOptions, CliAgentOptionsValidator>(context.Configuration);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        Check.NotNull(context);
        var services = context.Services;

        services.AddTransient<IPermissionDefinitionProvider, CliPermissions>();

        // provider 描述表：内置集合 + appsettings 合并。无状态，读 IOptionsMonitor。
        services.AddSingleton<ICliProviderRegistry, CliProviderRegistry>();

        // 适配器按会话创建（有状态、一次性），所以注入工厂而不是实例。
        services.AddSingleton<ICliProtocolAdapterFactory, CliProtocolAdapterFactory>();

        // 进程宿主。P4 的沙箱执行会在这里换一个实现，适配器与调度层一行不改。
        services.AddSingleton<ICliProcessHost, LocalProcessHost>();

        services.AddSingleton<ICliExecutableResolver, CliExecutableResolver>();
        services.AddSingleton<ICliBriefComposer, CliBriefComposer>();
        services.AddSingleton<ICliMcpConfigComposer, CliMcpConfigComposer>();

        // 回写凭据：本模块签发，Tnzi.AI.Mcp 经核心契约 IRunScopedCredentialValidator 校验。
        // 两个可选子模块因此互不引用，各自单独加载都成立。
        services.AddScoped<CliRunTokenService>();
        services.AddScoped<IRunScopedCredentialValidator>(sp => sp.GetRequiredService<CliRunTokenService>());
        services.AddSingleton<ICliWorkspacePreparer, FileSystemWorkspacePreparer>();

        // 信号中枢与取消登记处必须是 Singleton：订阅者（HTTP 请求作用域）与执行者
        // （后台服务作用域）分处不同 scope，只有单例才能让它们看到同一份状态。
        services.AddSingleton<CliRunSignalHub>();
        services.AddSingleton<CliRunCancellationRegistry>();

        // 核心契约的真实实现。它们在 Configure 阶段注册，先于 AIModule 在
        // PostConfigure 阶段的 NoOp TryAdd 回退，因此自动胜出。
        services.AddScoped<ICliAgentDispatcher, CliAgentDispatcher>();
        services.AddScoped<ICliAgentBindingService, CliAgentBindingService>();
        services.AddScoped<ICliRuntimeService, CliRuntimeService>();

        services.AddScoped<CliRunExecutor>();

        services.AddHostedService<CliRunQueueProcessor>();
        services.AddHostedService<CliRuntimeProbeService>();
        services.AddHostedService<CliWorkspaceGcService>();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        Check.NotNull(context);

        var logger = context.ServiceProvider.GetRequiredService<ILogger<AICliModule>>();
        var options = context.ServiceProvider.GetRequiredService<IOptions<CliAgentOptions>>().Value;

        if (!options.Enabled)
        {
            logger.LogInformation(
                "External CLI agent execution is loaded but disabled (AI:Cli:Enabled=false). "
                + "Bindings and runs will return 501 until it is switched on.");
            return Task.CompletedTask;
        }

        var registry = context.ServiceProvider.GetRequiredService<ICliProviderRegistry>();
        var factory = context.ServiceProvider.GetRequiredService<ICliProtocolAdapterFactory>();

        var unsupported = registry.GetEnabled()
            .Where(p => !factory.IsImplemented(p.Protocol))
            .Select(p => $"{p.Key} ({p.Protocol})")
            .ToList();

        if (unsupported.Count > 0)
        {
            // 启动时就说清楚，好过让管理员绑定之后在第一次运行时收到 501。
            logger.LogWarning(
                "These enabled providers have no protocol adapter in this version and cannot run: {Providers}",
                string.Join(", ", unsupported));
        }

        logger.LogInformation(
            "External CLI agent execution is enabled (workspaces={Root}, maxConcurrentRuns={Max})",
            options.WorkspacesRoot ?? CliWorkspaceLayout.DefaultWorkspacesRoot,
            options.MaxConcurrentRuns);

        return Task.CompletedTask;
    }
}
