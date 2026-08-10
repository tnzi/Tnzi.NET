using Tnzi.AI.Infrastructure.Mcp;
using Tnzi.AI.Infrastructure.Network;
using Tnzi.AI.Infrastructure.Readability;

namespace Tnzi.AI.Tests.Modules;

/// <summary>
/// AIModule DI 注册覆盖测试 - 验证所有核心服务、中间件、工具、Guardrails 都正确注册
/// </summary>
public class AIModuleRegistrationTests
{
    #region 模块元数据

    [Fact]
    public void LoadOrder_Is50()
    {
        var module = new AIModule();
        module.LoadOrder.ShouldBe(50);
    }

    [Fact]
    public void TableNamePrefix_IsAI()
    {
        var module = new AIModule();
        module.TableNamePrefix.ShouldBe("AI");
    }

    #endregion

    #region 核心服务注册

    [Theory]
    [InlineData(typeof(IChatClientFactory), typeof(ChatClientFactory), ServiceLifetime.Singleton)]
    [InlineData(typeof(ICostCalculator), typeof(CostCalculator), ServiceLifetime.Singleton)]
    [InlineData(typeof(ISubAgentRegistry), typeof(SubAgentRegistry), ServiceLifetime.Singleton)]
    [InlineData(typeof(IToolPermissionEvaluator), null, ServiceLifetime.Singleton)]
    [InlineData(typeof(IShellCommandAnalyzer), typeof(ShellCommandAnalyzer), ServiceLifetime.Singleton)]
    [InlineData(typeof(IReadabilityExtractor), typeof(SmartReaderExtractor), ServiceLifetime.Singleton)]
    [InlineData(typeof(IPortAllocator), typeof(PortAllocator), ServiceLifetime.Singleton)]
    [InlineData(typeof(IMcpClientFactory), typeof(McpClientFactory), ServiceLifetime.Singleton)]
    [InlineData(typeof(IMcpToolProvider), typeof(McpToolProvider), ServiceLifetime.Singleton)]
    [InlineData(typeof(OpenApiToolGenerator), null, ServiceLifetime.Singleton)]
    public void RegistersSingletonService(Type serviceType, Type? implType, ServiceLifetime lifetime)
    {
        var services = CreateServiceCollection();
        var descriptor = services.FirstOrDefault(d => d.ServiceType == serviceType);

        descriptor.ShouldNotBeNull($"{serviceType.Name} should be registered");
        descriptor.Lifetime.ShouldBe(lifetime);
        if (implType != null)
            descriptor.ImplementationType.ShouldBe(implType);
    }

    [Theory]
    [InlineData(typeof(IChatService), typeof(ChatService))]
    [InlineData(typeof(IEmbeddingService), typeof(EmbeddingService))]
    [InlineData(typeof(IStructuredOutputService), typeof(StructuredOutputService))]
    [InlineData(typeof(IAgentService), typeof(AgentService))]
    [InlineData(typeof(IAgentRunService), typeof(AgentRunService))]
    [InlineData(typeof(IAgentRunSignalDispatcher), typeof(AgentRunSignalDispatcher))]
    [InlineData(typeof(IAgentRuntimeControlService), typeof(AgentRuntimeControlService))]
    [InlineData(typeof(ISubAgentExecutionService), typeof(SubAgentExecutionService))]
    [InlineData(typeof(IAgentTraceService), typeof(AgentTraceService))]
    [InlineData(typeof(IAgentRuntime), typeof(AgentRuntime))]
    [InlineData(typeof(IUsageLogService), typeof(UsageLogService))]
    [InlineData(typeof(IUsageAnalyticsService), typeof(UsageAnalyticsService))]
    [InlineData(typeof(IAgentValidationService), typeof(AgentValidationService))]
    [InlineData(typeof(IEvaluationService), typeof(EvaluationService))]
    [InlineData(typeof(IUserProfileService), typeof(UserProfileService))]
    [InlineData(typeof(IAgentArtifactService), typeof(AgentArtifactService))]
    [InlineData(typeof(IMessageFeedbackService), typeof(MessageFeedbackService))]
    [InlineData(typeof(IRunStore), typeof(RunStore))]
    [InlineData(typeof(ITraceStore), typeof(TraceStore))]
    [InlineData(typeof(IAgentResolver), typeof(AgentResolver))]
    [InlineData(typeof(IToolResolver), typeof(ToolResolver))]
    [InlineData(typeof(IAgentFactory), typeof(AgentFactory))]
    [InlineData(typeof(GuardrailRunner), null)]
    public void RegistersScopedService(Type serviceType, Type? implType)
    {
        var services = CreateServiceCollection();
        var descriptor = services.FirstOrDefault(d => d.ServiceType == serviceType);

        descriptor.ShouldNotBeNull($"{serviceType.Name} should be registered");
        descriptor.Lifetime.ShouldBe(ServiceLifetime.Scoped);
        if (implType != null)
            descriptor.ImplementationType.ShouldBe(implType);
    }

    #endregion

    #region TryAdd 服务（允许用户覆盖）

    [Theory]
    [InlineData(typeof(IConversationStore))]
    [InlineData(typeof(IMemoryStore))]
    [InlineData(typeof(IMemoryConsolidator))]
    [InlineData(typeof(IEntityMemoryStore))]
    [InlineData(typeof(ITextSearchService))]
    [InlineData(typeof(IToolApprovalHandler))]
    [InlineData(typeof(IPromptTemplateEngine))]
    [InlineData(typeof(ISuggestionService))]
    [InlineData(typeof(IAiUtility))]
    [InlineData(typeof(ITnziAiClient))]
    [InlineData(typeof(ITokenEstimator))]
    [InlineData(typeof(IConfigChangeDetector))]
    [InlineData(typeof(IA2AClient))]
    [InlineData(typeof(IAgentEvaluator))]
    [InlineData(typeof(IToolRegistry))]
    [InlineData(typeof(IToolScanner))]
    [InlineData(typeof(IWorkflowService))]
    [InlineData(typeof(ISkillLoadTracker))]
    public void TryAddService_IsRegistered(Type serviceType)
    {
        var services = CreateServiceCollection();
        services.Any(d => d.ServiceType == serviceType)
            .ShouldBeTrue($"{serviceType.Name} should be registered via TryAdd");
    }

    [Fact]
    public void TryAdd_DoesNotOverrideExisting()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // 预注册自定义 IConversationStore
        var mock = Mock.Of<IConversationStore>();
        services.AddScoped(_ => mock);

        ConfigureModule(services);

        // 第一个注册应为自定义实现
        var descriptor = services.First(d => d.ServiceType == typeof(IConversationStore));
        descriptor.ImplementationFactory.ShouldNotBeNull();
    }

    #endregion

    #region ChatClient Provider 注册

    [Fact]
    public void RegistersBothChatClientProviders()
    {
        var services = CreateServiceCollection();
        var providers = services.Where(d => d.ServiceType == typeof(IChatClientProvider)).ToList();

        providers.Count.ShouldBeGreaterThanOrEqualTo(2, "Should register OpenAI and Anthropic providers");
        providers.ShouldContain(d => d.ImplementationType == typeof(OpenAIChatClientProvider));
        providers.ShouldContain(d => d.ImplementationType == typeof(AnthropicChatClientProvider));
    }

    #endregion

    #region ChatMessageProcessor 注册

    [Fact]
    public void RegistersAllChatMessageProcessors()
    {
        var services = CreateServiceCollection();
        var processors = services.Where(d => d.ServiceType == typeof(IChatMessageProcessor)).ToList();

        processors.Count.ShouldBeGreaterThanOrEqualTo(3);
        processors.ShouldContain(d => d.ImplementationType == typeof(DeepSeekChatMessageProcessor));
        processors.ShouldContain(d => d.ImplementationType == typeof(GeminiChatMessageProcessor));
        processors.ShouldContain(d => d.ImplementationType == typeof(MiniMaxChatMessageProcessor));
    }

    #endregion

    #region 中间件注册

    [Theory]
    [InlineData(typeof(RetryMiddleware))]
    [InlineData(typeof(ThinkingMiddleware))]
    [InlineData(typeof(PromptCachingMiddleware))]
    [InlineData(typeof(QuotaMiddleware))]
    [InlineData(typeof(InputGuardrailMiddleware))]
    [InlineData(typeof(HistoryMiddleware))]
    [InlineData(typeof(ContextInjectionMiddleware))]
    [InlineData(typeof(UsageLoggingMiddleware))]
    [InlineData(typeof(OutputGuardrailMiddleware))]
    [InlineData(typeof(ToolGuardrailMiddleware))]
    [InlineData(typeof(LoopDetectionMiddleware))]
    [InlineData(typeof(ToolErrorRecoveryMiddleware))]
    [InlineData(typeof(SubAgentLimitMiddleware))]
    [InlineData(typeof(SummarizationMiddleware))]
    [InlineData(typeof(FileUploadMiddleware))]
    [InlineData(typeof(ViewImageMiddleware))]
    [InlineData(typeof(TodoMiddleware))]
    [InlineData(typeof(ClarificationMiddleware))]
    public void RegistersMiddleware_BothConcreteAndInterface(Type middlewareType)
    {
        var services = CreateServiceCollection();

        // 具体类型应被注册
        services.Any(d => d.ServiceType == middlewareType)
            .ShouldBeTrue($"{middlewareType.Name} concrete type should be registered");

        // 同时应有 IAiMiddleware 转发注册
        var middlewareForwards = services.Where(d => d.ServiceType == typeof(IAiMiddleware)).ToList();
        middlewareForwards.ShouldNotBeEmpty("IAiMiddleware forward registrations should exist");
    }

    [Fact]
    public void RegistersAllMiddlewares_AsIAiMiddleware()
    {
        var services = CreateServiceCollection();
        var middlewareCount = services.Count(d => d.ServiceType == typeof(IAiMiddleware));

        // AIModule 注册了 18 个中间件（每个都有 IAiMiddleware 转发）
        middlewareCount.ShouldBeGreaterThanOrEqualTo(18, "Should register at least 18 IAiMiddleware forwards");
    }

    [Fact]
    public void RegistersToolGuardrail_AsToolExecutionMiddleware()
    {
        var services = CreateServiceCollection();

        services.ShouldContain(d =>
            d.ServiceType == typeof(IToolExecutionMiddleware) &&
            d.ImplementationFactory != null,
            "ToolGuardrailMiddleware should also participate in the tool execution pipeline");
    }

    [Fact]
    public void RegistersToolErrorRecovery_AsToolExecutionMiddleware()
    {
        var services = CreateServiceCollection();

        services.ShouldContain(d =>
            d.ServiceType == typeof(IToolExecutionMiddleware) &&
            d.ImplementationFactory != null,
            "ToolErrorRecoveryMiddleware should also participate in the tool execution pipeline");
    }

    #endregion

    #region 内置工具注册

    [Theory]
    [InlineData(typeof(DateTimeTools))]
    [InlineData(typeof(TextTools))]
    [InlineData(typeof(WebSearchTools))]
    [InlineData(typeof(Tnzi.AI.Tools.BuiltIn.MemoryTools))]
    [InlineData(typeof(A2ATools))]
    [InlineData(typeof(ClarificationTools))]
    [InlineData(typeof(TodoTools))]
    [InlineData(typeof(ArtifactTools))]
    [InlineData(typeof(AgentRunControlTools))]
    public void RegistersBuiltInTool(Type toolType)
    {
        var services = CreateServiceCollection();
        services.Any(d => d.ServiceType == toolType)
            .ShouldBeTrue($"{toolType.Name} should be registered");
    }

    #endregion

    #region Guardrail 注册

    [Theory]
    [InlineData(typeof(MaxLengthGuardrail))]
    [InlineData(typeof(PromptInjectionGuardrail))]
    [InlineData(typeof(PiiDetectionGuardrail))]
    [InlineData(typeof(ContentFilterGuardrail))]
    [InlineData(typeof(LlmJudgeGuardrail))]
    [InlineData(typeof(AllowlistGuardrailProvider))]
    public void RegistersGuardrail(Type guardrailType)
    {
        var services = CreateServiceCollection();
        services.Any(d => d.ServiceType == guardrailType)
            .ShouldBeTrue($"{guardrailType.Name} should be registered");
    }

    [Fact]
    public void RegistersInputGuardrails()
    {
        var services = CreateServiceCollection();
        var inputGuardrails = services.Count(d => d.ServiceType == typeof(IInputGuardrail));
        inputGuardrails.ShouldBeGreaterThanOrEqualTo(4, "Should have MaxLength + PromptInjection + PII + LlmJudge");
    }

    [Fact]
    public void RegistersOutputGuardrails()
    {
        var services = CreateServiceCollection();
        var outputGuardrails = services.Count(d => d.ServiceType == typeof(IOutputGuardrail));
        outputGuardrails.ShouldBeGreaterThanOrEqualTo(2, "Should have ContentFilter + LlmJudge");
    }

    [Fact]
    public void RegistersGuardrailProviders()
    {
        var services = CreateServiceCollection();
        var providers = services.Count(d => d.ServiceType == typeof(IGuardrailProvider));
        providers.ShouldBeGreaterThanOrEqualTo(6, "MaxLength + PromptInjection + PII + ContentFilter + LlmJudge + Allowlist");
    }

    #endregion

    #region 事件处理器

    [Fact]
    public void RegistersEventHandlers()
    {
        var services = CreateServiceCollection();

        // ThreadFirstReplyCompletedEvent handler
        services.Any(d => d.ServiceType.IsGenericType
            && d.ServiceType.GetGenericTypeDefinition().Name.Contains("IEventHandler")
            && d.ServiceType.GetGenericArguments().Any(a => a == typeof(ThreadFirstReplyCompletedEvent)))
            .ShouldBeTrue("ThreadFirstReplyCompletedEvent handler should be registered");
    }

    #endregion

    #region Thread 服务双接口注册

    [Fact]
    public void AgentThreadService_RegisteredAsBothInterfaces()
    {
        var services = CreateServiceCollection();

        services.Any(d => d.ServiceType == typeof(IAgentThreadService))
            .ShouldBeTrue("IAgentThreadService should be registered");
        services.Any(d => d.ServiceType == typeof(IAgentThreadInternalService))
            .ShouldBeTrue("IAgentThreadInternalService should be registered");
    }

    [Fact]
    public void QuotaService_RegisteredAsBothInterfaces()
    {
        var services = CreateServiceCollection();

        services.Any(d => d.ServiceType == typeof(IQuotaService))
            .ShouldBeTrue("IQuotaService should be registered");
        services.Any(d => d.ServiceType == typeof(IQuotaProvider))
            .ShouldBeTrue("IQuotaProvider should be registered");
    }

    [Fact]
    public void ResolvedPermissionEvaluator_UsesConfiguredRules()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        ConfigureModule(services, new Dictionary<string, string?>
        {
            ["AI:Providers:openai:ApiKey"] = "test-key",
            ["AI:Providers:openai:DefaultModel"] = "gpt-4o",
            ["AI:Permissions:Enabled"] = "true",
            ["AI:Permissions:SystemRules:0:ToolPattern"] = "bash",
            ["AI:Permissions:SystemRules:0:Behavior"] = "Deny",
            ["AI:Permissions:SystemRules:0:Reason"] = "Blocked by config"
        });

        using var provider = services.BuildServiceProvider();
        var evaluator = provider.GetRequiredService<IToolPermissionEvaluator>();

        var decision = evaluator.Evaluate(new ToolPermissionContext
        {
            ToolName = "bash"
        });

        decision.Behavior.ShouldBe(PermissionBehavior.Deny);
        decision.Reason.ShouldBe("Blocked by config");
    }

    #endregion

    #region Helpers

    private static ServiceCollection CreateServiceCollection()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        ConfigureModule(services);
        return services;
    }

    private static void ConfigureModule(IServiceCollection services)
        => ConfigureModule(services, null);

    private static void ConfigureModule(IServiceCollection services, IDictionary<string, string?>? overrides)
    {
        var module = new AIModule();
        var configValues = new Dictionary<string, string?>
        {
            ["AI:Providers:openai:ApiKey"] = "test-key",
            ["AI:Providers:openai:DefaultModel"] = "gpt-4o"
        };
        if (overrides != null)
        {
            foreach (var (key, value) in overrides)
            {
                configValues[key] = value;
            }
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        services.AddOptions<AIOptions>()
            .Bind(configuration.GetSection("AI"));
        services.AddOptions<AiUtilityOptions>()
            .Bind(configuration.GetSection("AI:Utility"));
        services.AddOptions<ThreadOptions>()
            .Bind(configuration.GetSection("AI:Thread"));
        services.AddOptions<LoopDetectionOptions>()
            .Bind(configuration.GetSection("AI:LoopDetection"));
        services.AddOptions<SubAgentOptions>()
            .Bind(configuration.GetSection("AI:SubAgent"));
        services.AddOptions<SuggestionOptions>()
            .Bind(configuration.GetSection("AI:Suggestions"));
        services.AddOptions<TodoOptions>()
            .Bind(configuration.GetSection("AI:Todo"));
        services.AddOptions<PortAllocatorOptions>()
            .Bind(configuration.GetSection("AI:PortAllocator"));

        // AIModule registers the engine implementations in Configure and the NoOp fallbacks in
        // PostConfigure. Bootstrap through the real phase semantics (Configure all → PostConfigure all).
        TestHelpers.ConfigureModules(services, configuration, module);
    }

    #endregion
}
