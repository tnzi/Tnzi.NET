using System.Reflection;

namespace Tnzi.AI.Tests.Modules;

/// <summary>
/// AIModule 可选子模块回退测试
/// </summary>
public class AIModuleOptionalDependencyTests
{
    [Fact]
    public void CoreModule_RegistersFallbackWorkflowService()
    {
        var services = CreateServiceCollection();
        var provider = services.BuildServiceProvider();

        var workflowService = provider.GetRequiredService<IWorkflowService>();

        workflowService.ShouldBeOfType<NoOpWorkflowService>();
    }

    [Fact]
    public void CoreModule_CanResolveRuntimeAndOptionsBuilder_WithoutWorkflowOrSkillsModules()
    {
        var services = CreateServiceCollection(new Dictionary<string, string?>
        {
            ["AI:ContextProviders:Enabled"] = "true",
            ["AI:ContextProviders:Skills:Enabled"] = "true"
        });

        services.AddScoped(_ => Mock.Of<IRepository<Agent, Guid>>());
        services.AddScoped<IMemoryStore>(_ => Mock.Of<IMemoryStore>());
        services.AddScoped<IEntityMemoryStore>(_ => Mock.Of<IEntityMemoryStore>());
        services.AddScoped<IAgentResolver>(_ => Mock.Of<IAgentResolver>());
        services.AddScoped<IAgentFactory>(_ => Mock.Of<IAgentFactory>());
        services.AddScoped<IRunStore>(_ => Mock.Of<IRunStore>());
        services.AddScoped<ITraceStore>(_ => Mock.Of<ITraceStore>());

        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });

        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IAgentRuntime>().ShouldNotBeNull();

        var builder = scope.ServiceProvider.GetRequiredService<AgentExecutorOptionsBuilder>();
        var options = builder.Build(null, "TestAgent", "hello", null, null, null);

        options.ShouldNotBeNull();
    }

    /// <summary>
    /// 当只加载 AIModule（无任何子模块）时，全部 INoOpService 回退实现都应能从 DI 解析，
    /// 且解析结果确实是 INoOpService（即真正命中了 NoOp 回退，而非真实实现或 null）。
    /// </summary>
    [Theory]
    [InlineData(typeof(IWorkflowService))]
    [InlineData(typeof(IWorkflowExecutionControlService))]
    [InlineData(typeof(IWorkflowExecutionQueryService))]
    [InlineData(typeof(IWorkflowExecutionMailbox))]
    [InlineData(typeof(IAgentStreamForwarder))]
    [InlineData(typeof(IVectorStore))]
    [InlineData(typeof(ISkillLoadTracker))]
    [InlineData(typeof(ISkillService))]
    [InlineData(typeof(ISkillStore))]
    [InlineData(typeof(ISkillTemplateEngine))]
    [InlineData(typeof(ISkillConstraintEnforcer))]
    [InlineData(typeof(ISkillSearchService))]
    [InlineData(typeof(ISkillRequirementsValidator))]
    public void CoreModule_AllNoOpFallbacks_ResolveAsNoOpService(Type serviceType)
    {
        var services = CreateServiceCollection();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var resolved = scope.ServiceProvider.GetService(serviceType);

        resolved.ShouldNotBeNull($"{serviceType.Name} should resolve to a NoOp fallback");
        resolved.ShouldBeAssignableTo<INoOpService>(
            $"{serviceType.Name} should resolve to an INoOpService fallback when no sub-module is loaded");
    }

    /// <summary>
    /// 完整性守卫：枚举 Tnzi.AI 程序集中全部 INoOpService 实现，
    /// 确保每一个（除有条件硬错误的 ITextSearchService 外）解析出来都是 INoOpService。
    /// 新增 NoOp 回退而忘记在 AIModule.Validation 的探测表中登记时，此测试会失败。
    /// </summary>
    [Fact]
    public void CoreModule_EveryNoOpImplementation_ResolvesAsNoOpFallback()
    {
        var services = CreateServiceCollection();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // ITextSearchService 是有条件硬错误（feature 开启时抛出），不走信息级探测
        var coveredServiceTypes = new HashSet<Type>
        {
            typeof(IWorkflowService), typeof(IWorkflowExecutionControlService),
            typeof(IWorkflowExecutionQueryService), typeof(IWorkflowExecutionMailbox),
            typeof(IAgentStreamForwarder), typeof(IVectorStore),
            typeof(ISkillLoadTracker), typeof(ISkillService), typeof(ISkillStore),
            typeof(ISkillTemplateEngine), typeof(ISkillConstraintEnforcer),
            typeof(ISkillSearchService), typeof(ISkillRequirementsValidator),
            typeof(ITextSearchService)
        };

        var noOpImpls = typeof(AIModule).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                && typeof(INoOpService).IsAssignableFrom(t))
            .ToList();

        noOpImpls.ShouldNotBeEmpty();

        foreach (var impl in noOpImpls)
        {
            // 找到该 NoOp 实现对应的 INoOpService 之外的服务接口
            var serviceIface = impl.GetInterfaces()
                .FirstOrDefault(i => i != typeof(INoOpService)
                    && coveredServiceTypes.Contains(i));

            serviceIface.ShouldNotBeNull(
                $"{impl.Name} is an INoOpService implementation but its service interface is not covered by the NoOp probe table");

            var resolved = scope.ServiceProvider.GetService(serviceIface);
            resolved.ShouldBeAssignableTo<INoOpService>(
                $"{serviceIface.Name} should resolve to a NoOp fallback when no sub-module is loaded");
        }
    }

    /// <summary>
    /// 无子模块、且未启用 TextSearch/ChatHistoryMemory 时，启动校验不应抛出。
    /// </summary>
    [Fact]
    public void ValidateRuntimeConfiguration_NoSubModules_DoesNotThrow()
    {
        var services = CreateServiceCollection();
        using var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("test");

        Should.NotThrow(() => InvokeValidateRuntimeConfiguration(provider, logger));
    }

    /// <summary>
    /// 启用 TextSearch 但 ITextSearchService 仍是 NoOp 回退时，启动校验必须抛出硬错误。
    /// </summary>
    [Fact]
    public void ValidateRuntimeConfiguration_TextSearchEnabledWithNoOp_Throws()
    {
        var services = CreateServiceCollection(new Dictionary<string, string?>
        {
            ["AI:ContextProviders:Enabled"] = "true",
            ["AI:ContextProviders:TextSearch:Enabled"] = "true"
        });
        using var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("test");

        var ex = Should.Throw<TargetInvocationException>(
            () => InvokeValidateRuntimeConfiguration(provider, logger));
        ex.InnerException.ShouldBeOfType<InvalidOperationException>();
    }

    private static void InvokeValidateRuntimeConfiguration(IServiceProvider provider, ILogger logger)
    {
        var method = typeof(AIModule).GetMethod(
            "ValidateNoOpFallbacks",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.ShouldNotBeNull();
        method.Invoke(null, [provider, logger]);
    }

    [Fact]
    public async Task OptionsBuilder_BuildsSuccessfullyWithEmptyContributors()
    {
        var aiOptions = new AIOptions();
        aiOptions.ContextProviders.Enabled = true;

        var builder = new AgentExecutorOptionsBuilder(
            new StaticOptionsMonitor<AIOptions>(aiOptions),
            LoggerFactory.Create(_ => { }),
            Mock.Of<IChatClientFactory>(),
            [],
            new HeuristicTokenEstimator(),
            Mock.Of<ILogger<AgentExecutorOptionsBuilder>>());

        var options = builder.Build(null, "TestAgent", "hello", null, null, null);

        options.ShouldNotBeNull();
        options.Name.ShouldBe("TestAgent");
        await Task.CompletedTask;
    }

    private static ServiceCollection CreateServiceCollection(Dictionary<string, string?>? extraConfig = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configValues = new Dictionary<string, string?>
        {
            ["AI:Providers:openai:Enabled"] = "true",
            ["AI:Providers:openai:ApiKey"] = "test-key",
            ["AI:Providers:openai:DefaultModel"] = "gpt-4o",
            ["AI:DefaultProvider"] = "openai"
        };

        if (extraConfig != null)
        {
            foreach (var (key, value) in extraConfig)
            {
                configValues[key] = value;
            }
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        services.AddOptions<AIOptions>().Bind(configuration.GetSection("AI"));
        services.AddOptions<AiUtilityOptions>().Bind(configuration.GetSection("AI:Utility"));
        services.AddOptions<ThreadOptions>().Bind(configuration.GetSection("AI:Thread"));
        services.AddOptions<LoopDetectionOptions>().Bind(configuration.GetSection("AI:LoopDetection"));
        services.AddOptions<SubAgentOptions>().Bind(configuration.GetSection("AI:SubAgent"));
        services.AddOptions<SuggestionOptions>().Bind(configuration.GetSection("AI:Suggestions"));
        services.AddOptions<TodoOptions>().Bind(configuration.GetSection("AI:Todo"));
        services.AddOptions<PortAllocatorOptions>().Bind(configuration.GetSection("AI:PortAllocator"));

        // AIModule registers the engine impls in Configure and the NoOp fallbacks in PostConfigure.
        // Bootstrap through the real phase semantics so the engine is resolvable while the OPTIONAL
        // sub-modules (Workflow/Skills/Rag) remain at their NoOp fallbacks — exactly the
        // "core loaded, sub-modules absent" scenario these tests assert.
        TestHelpers.ConfigureModules(services, configuration, new AIModule());
        return services;
    }
}
