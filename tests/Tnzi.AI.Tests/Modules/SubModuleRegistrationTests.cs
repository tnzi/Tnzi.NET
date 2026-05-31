using Tnzi.AI.Channels;
using Tnzi.AI.Channels.Abstractions;
using Tnzi.AI.Channels.Adapters.Feishu;
using Tnzi.AI.Channels.Adapters.Telegram;
using Tnzi.AI.Channels.Options;
using Tnzi.AI.Mcp;
using Tnzi.AI.Mcp.Options;
using Tnzi.AI.Mcp.Server;
using Tnzi.AI.Mcp.Services;
using Tnzi.AI.Mcp.Services.Interfaces;
using Tnzi.AI.Sandbox;
using Tnzi.AI.Sandbox.Middleware;
using Tnzi.AI.Sandbox.Providers.Docker;
using Tnzi.AI.Sandbox.Providers.Kubernetes;
using Tnzi.AI.Sandbox.Tools;
using Tnzi.AI.Workflow;
using Tnzi.Modules;

namespace Tnzi.AI.Tests.Modules;

/// <summary>
/// AI 子模块 DI 注册覆盖测试
/// </summary>
public class SubModuleRegistrationTests
{
    // =========================================================================
    // AISkillsModule
    // =========================================================================

    [Fact]
    public void AISkillsModule_LoadOrder_Is51()
    {
        new AISkillsModule().LoadOrder.ShouldBe(51);
    }

    [Theory]
    [InlineData(typeof(ISkillLoadTracker), ServiceLifetime.Scoped)]
    [InlineData(typeof(ISkillService), ServiceLifetime.Scoped)]
    [InlineData(typeof(ISkillCategoryService), ServiceLifetime.Scoped)]
    [InlineData(typeof(FileSystemSkillStore), ServiceLifetime.Singleton)]
    [InlineData(typeof(DatabaseSkillStore), ServiceLifetime.Scoped)]
    [InlineData(typeof(SkillConstraintMiddleware), ServiceLifetime.Scoped)]
    public void AISkillsModule_RegistersService(Type serviceType, ServiceLifetime lifetime)
    {
        var services = ConfigureSkillsModule();
        var descriptor = services.FirstOrDefault(d => d.ServiceType == serviceType);

        descriptor.ShouldNotBeNull($"AISkillsModule should register {serviceType.Name}");
        descriptor.Lifetime.ShouldBe(lifetime);
    }

    [Theory]
    [InlineData(typeof(ISkillTemplateEngine))]
    [InlineData(typeof(ISkillConstraintEnforcer))]
    [InlineData(typeof(ISkillRequirementsValidator))]
    [InlineData(typeof(ISkillSearchService))]
    [InlineData(typeof(ISkillRegistry))]
    public void AISkillsModule_TryAddService(Type serviceType)
    {
        var services = ConfigureSkillsModule();
        services.Any(d => d.ServiceType == serviceType)
            .ShouldBeTrue($"AISkillsModule should TryAdd {serviceType.Name}");
    }

    [Fact]
    public void AISkillsModule_RegistersSkillConstraintMiddleware_AsIAiMiddleware()
    {
        var services = ConfigureSkillsModule();
        services.Any(d => d.ServiceType == typeof(IAiMiddleware)
            && d.ImplementationFactory != null)
            .ShouldBeTrue("SkillConstraintMiddleware should be forwarded as IAiMiddleware");
    }

    // =========================================================================
    // AIWorkflowModule
    // =========================================================================

    [Fact]
    public void AIWorkflowModule_LoadOrder_Is52()
    {
        new AIWorkflowModule().LoadOrder.ShouldBe(52);
    }

    [Fact]
    public void AIWorkflowModule_TableNamePrefix_IsAI()
    {
        new AIWorkflowModule().TableNamePrefix.ShouldBe("AI");
    }

    [Fact]
    public void AIWorkflowModule_RegistersWorkflowService()
    {
        var services = ConfigureWorkflowModule();
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IWorkflowService));

        descriptor.ShouldNotBeNull();
        // WorkflowService is registered as factory (sp => sp.GetRequiredService<WorkflowService>())
        // so ImplementationType is null; verify via ImplementationFactory instead
        descriptor.ImplementationFactory.ShouldNotBeNull();
        descriptor.Lifetime.ShouldBe(ServiceLifetime.Scoped);
    }

    [Fact]
    public void AIWorkflowModule_RegistersWorkflowEngine()
    {
        var services = ConfigureWorkflowModule();
        services.Any(d => d.ServiceType == typeof(WorkflowEngine))
            .ShouldBeTrue("WorkflowEngine should be registered");
    }

    [Fact]
    public void AIWorkflowModule_RegistersWorkflowNodeExecutor()
    {
        var services = ConfigureWorkflowModule();
        services.Any(d => d.ServiceType == typeof(WorkflowNodeExecutor))
            .ShouldBeTrue("WorkflowNodeExecutor should be registered");
    }

    [Fact]
    public void AIWorkflowModule_RegistersAll9NodeTypes()
    {
        var services = ConfigureWorkflowModule();
        var nodeRegistrations = services.Where(d => d.ServiceType == typeof(IWorkflowNode)).ToList();

        nodeRegistrations.Count.ShouldBe(9, "Should register exactly 9 workflow node types");

        var nodeTypes = nodeRegistrations.Select(d => d.ImplementationType!.Name).OrderBy(n => n).ToList();
        nodeTypes.ShouldContain("AgentNode");
        nodeTypes.ShouldContain("ReviewNode");
        nodeTypes.ShouldContain("ApprovalNode");
        nodeTypes.ShouldContain("RouterNode");
        nodeTypes.ShouldContain("ParallelNode");
        nodeTypes.ShouldContain("SynthesizeNode");
        nodeTypes.ShouldContain("DebateNode");
        nodeTypes.ShouldContain("ConditionalNode");
        nodeTypes.ShouldContain("TransformNode");
    }

    [Fact]
    public void AIWorkflowModule_TryAddsCheckpointStore()
    {
        var services = ConfigureWorkflowModule();
        services.Any(d => d.ServiceType == typeof(IWorkflowCheckpointStore))
            .ShouldBeTrue("IWorkflowCheckpointStore should be registered via TryAdd");
    }

    // =========================================================================
    // AIMcpModule
    // =========================================================================

    [Fact]
    public void AIMcpModule_LoadOrder_Is53()
    {
        new AIMcpModule().LoadOrder.ShouldBe(53);
    }

    [Fact]
    public void AIMcpModule_TableNamePrefix_IsAI()
    {
        new AIMcpModule().TableNamePrefix.ShouldBe("AI");
    }

    [Fact]
    public void AIMcpModule_RegistersCoreServices()
    {
        var services = ConfigureMcpModule();

        services.Any(d => d.ServiceType == typeof(IMcpToolAnalyticsService))
            .ShouldBeTrue("IMcpToolAnalyticsService should be registered");
        services.Any(d => d.ServiceType == typeof(IMcpOAuthTokenManager))
            .ShouldBeTrue("IMcpOAuthTokenManager should be registered");
        services.Any(d => d.ServiceType == typeof(McpServerSecurityMiddleware))
            .ShouldBeTrue("McpServerSecurityMiddleware should be registered");
    }

    [Fact]
    public void AIMcpModule_TryAddsMcpServerHost()
    {
        var services = ConfigureMcpModule();
        services.Any(d => d.ServiceType == typeof(IMcpServerHost))
            .ShouldBeTrue("IMcpServerHost should be registered via TryAdd");
    }

    // =========================================================================
    // SandboxModule
    // =========================================================================

    [Fact]
    public void SandboxModule_LoadOrder_Is57()
    {
        new SandboxModule().LoadOrder.ShouldBe(57);
    }

    [Fact]
    public void SandboxModule_RegistersVirtualPathTranslator()
    {
        var services = ConfigureSandboxModule();
        services.Any(d => d.ServiceType == typeof(IVirtualPathTranslator))
            .ShouldBeTrue("IVirtualPathTranslator should be registered");
    }

    [Fact]
    public void SandboxModule_RegistersLocalSandboxProvider_ByDefault()
    {
        var services = ConfigureSandboxModule();
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ISandboxProvider));

        descriptor.ShouldNotBeNull("ISandboxProvider should be registered");
        descriptor.ImplementationType.ShouldBe(typeof(LocalSandboxProvider));
    }

    [Fact]
    public void SandboxModule_RegistersDockerProvider_WhenConfigured()
    {
        var services = ConfigureSandboxModule("docker");
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ISandboxProvider));

        descriptor.ShouldNotBeNull();
        descriptor.ImplementationType.ShouldBe(typeof(DockerSandboxProvider));
    }

    [Fact]
    public void SandboxModule_RegistersKubernetesProvider_WhenConfigured()
    {
        var services = ConfigureSandboxModule("kubernetes");
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ISandboxProvider));

        descriptor.ShouldNotBeNull();
        descriptor.ImplementationType.ShouldBe(typeof(KubernetesSandboxProvider));
    }

    [Fact]
    public void SandboxModule_RegistersSandboxTools()
    {
        var services = ConfigureSandboxModule();
        services.Any(d => d.ServiceType == typeof(SandboxTools))
            .ShouldBeTrue("SandboxTools should be registered");
    }

    [Fact]
    public void SandboxModule_RegistersBothMiddlewares()
    {
        var services = ConfigureSandboxModule();
        var middlewares = services.Where(d => d.ServiceType == typeof(IAiMiddleware)).ToList();

        middlewares.ShouldContain(d => d.ImplementationType == typeof(ThreadDataMiddleware),
            "ThreadDataMiddleware should be registered as IAiMiddleware");
        middlewares.ShouldContain(d => d.ImplementationType == typeof(SandboxMiddleware),
            "SandboxMiddleware should be registered as IAiMiddleware");
    }

    // =========================================================================
    // ChannelsModule
    // =========================================================================

    [Fact]
    public void ChannelsModule_LoadOrder_Is58()
    {
        new ChannelsModule().LoadOrder.ShouldBe(58);
    }

    [Fact]
    public void ChannelsModule_TableNamePrefix_IsAI()
    {
        new ChannelsModule().TableNamePrefix.ShouldBe("AI");
    }

    [Fact]
    public void ChannelsModule_Disabled_RegistersNothing()
    {
        var services = ConfigureChannelsModule(enabled: false);

        services.Any(d => d.ServiceType == typeof(IChannelMessageBus)).ShouldBeFalse();
        services.Any(d => d.ServiceType == typeof(IChannelManager)).ShouldBeFalse();
    }

    [Fact]
    public void ChannelsModule_Enabled_RegistersCoreServices()
    {
        var services = ConfigureChannelsModule(enabled: true);

        services.Any(d => d.ServiceType == typeof(IChannelMessageBus)).ShouldBeTrue();
        services.Any(d => d.ServiceType == typeof(IChannelManager)).ShouldBeTrue();
        services.Any(d => d.ServiceType == typeof(IChannelThreadStore)).ShouldBeTrue();
    }

    [Fact]
    public void ChannelsModule_TelegramEnabled_RegistersAdapter()
    {
        var services = ConfigureChannelsModule(enabled: true, telegramEnabled: true);

        services.Any(d => d.ServiceType == typeof(IChannelAdapter)
            && d.ImplementationType == typeof(TelegramChannelAdapter))
            .ShouldBeTrue("TelegramChannelAdapter should be registered");
    }

    [Fact]
    public void ChannelsModule_FeishuEnabled_RegistersAdapter()
    {
        var services = ConfigureChannelsModule(enabled: true, feishuEnabled: true);

        // Webhook adapters register the concrete type as a singleton, then forward both
        // IChannelAdapter and IInboundWebhookAdapter to it via factory (ImplementationType is null).
        services.Any(d => d.ServiceType == typeof(FeishuChannelAdapter))
            .ShouldBeTrue("FeishuChannelAdapter concrete singleton should be registered");
        services.Any(d => d.ServiceType == typeof(IChannelAdapter)
            && d.ImplementationFactory != null)
            .ShouldBeTrue("FeishuChannelAdapter should be exposed as IChannelAdapter");
        services.Any(d => d.ServiceType == typeof(IInboundWebhookAdapter)
            && d.ImplementationFactory != null)
            .ShouldBeTrue("FeishuChannelAdapter should be exposed as IInboundWebhookAdapter");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static ServiceCollection ConfigureSkillsModule()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        services.AddOptions<AIOptions>().Bind(config.GetSection("AI"));
        var context = new ServiceConfigurationContext(services, config);
        new AISkillsModule().ConfigureServicesAsync(context).GetAwaiter().GetResult();
        return services;
    }

    private static ServiceCollection ConfigureWorkflowModule()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var context = new ServiceConfigurationContext(services, config);
        new AIWorkflowModule().ConfigureServicesAsync(context).GetAwaiter().GetResult();
        return services;
    }

    private static ServiceCollection ConfigureMcpModule()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:McpServer:Enabled"] = "false"
            })
            .Build();

        services.AddOptions<McpServerOptions>()
            .Bind(config.GetSection("AI:McpServer"));
        services.AddOptions<McpOAuthOptions>()
            .Bind(config.GetSection("AI:McpServer:OAuth"));

        var context = new ServiceConfigurationContext(services, config);
        new AIMcpModule().ConfigureServicesAsync(context).GetAwaiter().GetResult();
        return services;
    }

    private static ServiceCollection ConfigureSandboxModule(string provider = "local")
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:Sandbox:Provider"] = provider,
                ["AI:Sandbox:DataRoot"] = Path.GetTempPath()
            })
            .Build();

        services.AddOptions<SandboxModuleOptions>()
            .Bind(config.GetSection("AI:Sandbox"));

        var context = new ServiceConfigurationContext(services, config);
        new SandboxModule().ConfigureServicesAsync(context).GetAwaiter().GetResult();
        return services;
    }

    private static ServiceCollection ConfigureChannelsModule(
        bool enabled = false, bool telegramEnabled = false, bool feishuEnabled = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:Channels:Enabled"] = enabled.ToString(),
                ["AI:Channels:Telegram:Enabled"] = telegramEnabled.ToString(),
                ["AI:Channels:Telegram:BotToken"] = telegramEnabled ? "test-token" : null,
                ["AI:Channels:Feishu:Enabled"] = feishuEnabled.ToString(),
                ["AI:Channels:Feishu:AppId"] = feishuEnabled ? "test-app" : null,
                ["AI:Channels:Feishu:AppSecret"] = feishuEnabled ? "test-secret" : null,
                ["AI:Channels:ThreadStore"] = "File",
                ["AI:Channels:FileStorePath"] = Path.GetTempPath()
            })
            .Build();

        services.AddOptions<ChannelsModuleOptions>()
            .Bind(config.GetSection("AI:Channels"));

        var context = new ServiceConfigurationContext(services, config);
        new ChannelsModule().ConfigureServicesAsync(context).GetAwaiter().GetResult();
        return services;
    }
}
