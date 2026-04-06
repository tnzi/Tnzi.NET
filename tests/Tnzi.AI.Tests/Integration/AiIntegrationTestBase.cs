using Tnzi.AI.Infrastructure.Memory;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.AI.Tests.Integration;

/// <summary>
/// AI 集成测试 DbContext — 注册所有 AI 核心实体
/// </summary>
public class AiIntegrationDbContext : TnziDbContext<AiIntegrationDbContext>
{
    public AiIntegrationDbContext(
        DbContextOptions<AiIntegrationDbContext> options,
        Tnzi.Security.Claims.ICurrentUser currentUser)
        : base(options, currentUser)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 注册所有 AI 核心实体
        modelBuilder.ApplyConfiguration(new Tnzi.AI.Entities.Configs.AgentConfiguration());
        modelBuilder.ApplyConfiguration(new Tnzi.AI.Entities.Configs.AgentThreadConfiguration());
        modelBuilder.ApplyConfiguration(new Tnzi.AI.Entities.Configs.AgentThreadMessageConfiguration());
        modelBuilder.ApplyConfiguration(new Tnzi.AI.Entities.Configs.AgentRunConfiguration());
        modelBuilder.ApplyConfiguration(new Tnzi.AI.Entities.Configs.AgentRunNodeConfiguration());
        modelBuilder.ApplyConfiguration(new Tnzi.AI.Entities.Configs.AgentRunTraceConfiguration());
        modelBuilder.ApplyConfiguration(new Tnzi.AI.Entities.Configs.AgentVersionConfiguration());
        modelBuilder.ApplyConfiguration(new Tnzi.AI.Entities.Configs.UsageLogConfiguration());
        modelBuilder.ApplyConfiguration(new Tnzi.AI.Entities.Configs.UserQuotaConfiguration());
        modelBuilder.ApplyConfiguration(new Tnzi.AI.Entities.Configs.MemoryEntryConfiguration());
        modelBuilder.ApplyConfiguration(new Tnzi.AI.Entities.Configs.EntityMemoryConfiguration());
        modelBuilder.ApplyConfiguration(new Tnzi.AI.Entities.Configs.AgentPersonaConfiguration());
        modelBuilder.ApplyConfiguration(new Tnzi.AI.Entities.Configs.UserProfileConfiguration());
        modelBuilder.ApplyConfiguration(new Tnzi.AI.Entities.Configs.AgentArtifactConfiguration());
        modelBuilder.ApplyConfiguration(new Tnzi.AI.Entities.Configs.EvaluationRunConfiguration());

        base.OnModelCreating(modelBuilder);
        TestHelper.ApplySqliteUtcDateTimeConverter(modelBuilder, Database.ProviderName);
    }
}

/// <summary>
/// AI 集成测试基类 — 提供可编程 Mock AI 提供商、真实 AgentRuntime、SQLite 内存数据库
/// </summary>
/// <remarks>
/// <para>设计策略：</para>
/// <list type="bullet">
///   <item>MockChatClientProvider — 可编程的 AI 响应队列</item>
///   <item>真实 AgentRuntime 和中间件管道 — 测试真正的请求处理流程</item>
///   <item>SQLite 内存数据库 — 真实的 EF Core 持久化和查询</item>
///   <item>Mock 外部依赖 — IWorkflowService、ISkillRegistry 等复杂基础设施</item>
/// </list>
/// </remarks>
public abstract class AiIntegrationTestBase : IntegratedTestBase<AiIntegrationDbContext>
{
    /// <summary>
    /// 可编程的 Mock AI 提供商
    /// </summary>
    protected MockChatClientProvider MockProvider { get; } = new();

    /// <summary>
    /// AI 运行时入口（真实实现，驱动中间件管道 + 执行策略）
    /// </summary>
    protected IAgentRuntime Runtime => ServiceProvider.GetRequiredService<IAgentRuntime>();

    /// <summary>
    /// 默认 AIOptions（子类可覆盖 ConfigureAiOptions 定制）
    /// </summary>
    protected AIOptions AiOptions { get; } = CreateDefaultAiOptions();

    protected override void ConfigureServices(IServiceCollection services)
    {
        // 1. Mapster 映射基础设施
        var mapperConfig = new TypeAdapterConfig();
        var mapper = new Mapper(mapperConfig);
        MapperExtensions.SetMapper(mapper);

        // 2. AI 配置选项
        ConfigureAiOptions(AiOptions);
        services.AddSingleton(MsOptions.Create(AiOptions));
        services.AddSingleton<IOptionsMonitor<AIOptions>>(sp =>
        {
            var monitor = new Mock<IOptionsMonitor<AIOptions>>();
            monitor.Setup(m => m.CurrentValue).Returns(AiOptions);
            return monitor.Object;
        });

        // 3. 注册 Mock ChatClient Provider 和 Factory
        var mockChatClient = MockProvider.CreateChatClient(
            new ProviderOptions { Enabled = true, DefaultModel = "mock-model" }, "mock-model");
        var chatClientFactoryMock = new Mock<IChatClientFactory>();
        chatClientFactoryMock
            .Setup(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(mockChatClient);
        chatClientFactoryMock
            .Setup(f => f.GetAvailableProviders())
            .Returns(new List<string> { "MockProvider" });
        chatClientFactoryMock
            .Setup(f => f.GetDefaultModel(It.IsAny<string?>()))
            .Returns("mock-model");
        services.AddSingleton(chatClientFactoryMock.Object);

        // 4. 仓储层
        services.AddScoped<IRepository<Agent, Guid>,
            EFCoreRepository<AiIntegrationDbContext, Agent, Guid>>();
        services.AddScoped<IRepository<AgentThread, Guid>,
            EFCoreRepository<AiIntegrationDbContext, AgentThread, Guid>>();
        services.AddScoped<IRepository<AgentThreadMessage, Guid>,
            EFCoreRepository<AiIntegrationDbContext, AgentThreadMessage, Guid>>();
        services.AddScoped<IRepository<AgentRun, Guid>,
            EFCoreRepository<AiIntegrationDbContext, AgentRun, Guid>>();
        services.AddScoped<IRepository<AgentRunNode, Guid>,
            EFCoreRepository<AiIntegrationDbContext, AgentRunNode, Guid>>();
        services.AddScoped<IRepository<AgentRunTrace, Guid>,
            EFCoreRepository<AiIntegrationDbContext, AgentRunTrace, Guid>>();
        services.AddScoped<IRepository<UsageLog, Guid>,
            EFCoreRepository<AiIntegrationDbContext, UsageLog, Guid>>();
        services.AddScoped<IRepository<UserQuota, Guid>,
            EFCoreRepository<AiIntegrationDbContext, UserQuota, Guid>>();
        services.AddScoped<IRepository<MemoryEntry, Guid>,
            EFCoreRepository<AiIntegrationDbContext, MemoryEntry, Guid>>();
        services.AddScoped<IRepository<EntityMemory, Guid>,
            EFCoreRepository<AiIntegrationDbContext, EntityMemory, Guid>>();

        // 5. 基础设施 Mock
        services.AddSingleton<IAgentExecutionContextAccessor, AgentExecutionContextAccessor>();

        // ToolRegistry / ToolScanner — 空实现即可
        services.AddSingleton<IToolScanner, ToolScanner>();
        services.AddSingleton<IToolRegistry, ToolRegistry>();

        // ToolResolver — 返回空工具列表
        var toolResolverMock = new Mock<IToolResolver>();
        toolResolverMock.Setup(r => r.ResolveToolsAsync(
                It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IList<AITool>?)null);
        services.AddScoped(_ => toolResolverMock.Object);

        // AgentFactory — 使用 Mock 直接返回简单 AgentExecutor
        services.AddScoped<IAgentFactory>(sp =>
        {
            var factoryMock = new Mock<IAgentFactory>();
            factoryMock.Setup(f => f.CreateAgentAsync(
                    It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<string?>(), It.IsAny<IEnumerable<string>?>(),
                    It.IsAny<double?>(), It.IsAny<int?>(), It.IsAny<AgentExecutorOptions?>(),
                    It.IsAny<IEnumerable<string>?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string? provider, string? model, string? instructions, string? name,
                    IEnumerable<string>? toolGroups, double? temperature, int? maxTokens,
                    AgentExecutorOptions? options, IEnumerable<string>? perms, Guid? agentId, CancellationToken ct) =>
                {
                    var client = MockProvider.CreateChatClient(
                        new ProviderOptions { Enabled = true }, model ?? "mock-model");
                    return new AgentExecutor(client, new AgentExecutorOptions
                    {
                        Name = name ?? "TestAgent",
                        Instructions = instructions,
                        Temperature = (float?)temperature,
                        MaxOutputTokens = maxTokens
                    });
                });
            return factoryMock.Object;
        });

        // AgentResolver — 使用 Mock 返回简单的 AgentResolution
        services.AddScoped<IAgentResolver>(sp =>
        {
            var resolverMock = new Mock<IAgentResolver>();
            resolverMock.Setup(r => r.ResolveAgentAsync(
                    It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid? agentId, string? provider, string? model,
                    List<string>? toolGroups, CancellationToken ct) =>
                {
                    var client = MockProvider.CreateChatClient(
                        new ProviderOptions { Enabled = true }, model ?? "mock-model");
                    var executor = new AgentExecutor(client, new AgentExecutorOptions
                    {
                        Name = "TestAgent",
                    });
                    return AgentResolution.Success(executor, provider ?? "MockProvider", model, agentId);
                });
            resolverMock.Setup(r => r.BuildChatMessageAsync(
                    It.IsAny<string?>(), It.IsAny<List<ContentPartDto>?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string? msg, List<ContentPartDto>? parts, CancellationToken ct) =>
                    new ChatMessage(ChatRole.User, msg ?? string.Empty));
            return resolverMock.Object;
        });

        // RunStore / TraceStore — 真实实现需要 DB
        services.AddScoped<IRunStore, RunStore>();
        services.AddScoped<ITraceStore, TraceStore>();

        // WorkflowService — Mock（工作流测试有独立 fixture）
        services.AddScoped(_ => Mock.Of<IWorkflowService>());

        // IServiceScopeFactory — AgentRuntime 需要
        services.AddSingleton<IServiceScopeFactory>(sp =>
            sp.GetRequiredService<IServiceScopeFactory>());

        // 中间件 — 默认不注册中间件，子类可通过 ConfigureMiddlewares 添加
        ConfigureMiddlewares(services);

        // 注册 AgentRuntime
        services.AddScoped<IAgentRuntime, AgentRuntime>();

        // 6. 子类可扩展的服务注册点
        ConfigureAdditionalServices(services);
    }

    /// <summary>
    /// 创建默认 AIOptions（禁用所有不需要的功能）
    /// </summary>
    private static AIOptions CreateDefaultAiOptions()
    {
        return new AIOptions
        {
            DefaultProvider = "MockProvider",
            Providers = new Dictionary<string, ProviderOptions>
            {
                ["MockProvider"] = new()
                {
                    Enabled = true,
                    DefaultModel = "mock-model",
                    ApiKey = "test-api-key"
                }
            },
            // 禁用所有需要外部依赖的功能
            ContextProviders = new ContextProvidersOptions { Enabled = false },
            Guardrails = new GuardrailsOptions { Enabled = false },
            BuiltInTools = new BuiltInToolsOptions { Enabled = false },
            Mcp = new McpOptions { Enabled = false },
            History = new HistoryOptions
            {
                Reduction = new HistoryReductionOptions { Mode = HistoryReductionMode.None }
            }
        };
    }

    /// <summary>
    /// 子类可覆盖以自定义 AIOptions（在 DI 注册前调用）
    /// </summary>
    protected virtual void ConfigureAiOptions(AIOptions options) { }

    /// <summary>
    /// 子类可覆盖以注册中间件到管道
    /// </summary>
    protected virtual void ConfigureMiddlewares(IServiceCollection services) { }

    /// <summary>
    /// 子类可覆盖以注册额外的服务
    /// </summary>
    protected virtual void ConfigureAdditionalServices(IServiceCollection services) { }

    /// <summary>
    /// 创建 Agent 实体并持久化到数据库
    /// </summary>
    protected async Task<Agent> CreateAgentAsync(string name, string? instructions = null)
    {
        var agent = new Agent
        {
            Name = name,
            Instructions = instructions ?? $"You are {name}.",
            Provider = "MockProvider",
            Model = "mock-model",
            IsEnabled = true,
            ExecutionMode = AgentExecutionMode.Single
        };

        DbContext.Set<Agent>().Add(agent);
        await DbContext.SaveChangesAsync();
        return agent;
    }

    /// <summary>
    /// 创建对话线程并持久化到数据库
    /// </summary>
    protected async Task<AgentThread> CreateThreadAsync(Guid? agentId = null)
    {
        var thread = new AgentThread
        {
            Title = "Test Thread",
            AgentId = agentId
        };

        DbContext.Set<AgentThread>().Add(thread);
        await DbContext.SaveChangesAsync();
        return thread;
    }

    /// <summary>
    /// 创建简单的 AgentRunRequest
    /// </summary>
    protected static AgentRunRequest CreateRequest(
        string userMessage,
        Guid? agentId = null,
        Guid? threadId = null)
    {
        return new AgentRunRequest
        {
            AgentId = agentId,
            ThreadId = threadId,
            UserMessage = userMessage,
            Provider = agentId.HasValue ? null : "MockProvider",
            Model = agentId.HasValue ? null : "mock-model"
        };
    }
}
