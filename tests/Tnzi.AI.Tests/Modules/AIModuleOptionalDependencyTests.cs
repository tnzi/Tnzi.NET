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

    [Fact]
    public async Task OptionsBuilder_BuildsSuccessfullyWithEmptyContributors()
    {
        var aiOptions = new AIOptions();
        aiOptions.ContextProviders.Enabled = true;

        var builder = new AgentExecutorOptionsBuilder(
            Microsoft.Extensions.Options.Options.Create(aiOptions),
            LoggerFactory.Create(_ => { }),
            Mock.Of<IChatClientFactory>(),
            [],
            Enumerable.Empty<IContextProviderContributor>(),
            new HeuristicTokenEstimator(),
            Mock.Of<IAgentExecutionContextAccessor>(),
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

        var context = new Tnzi.Modules.ServiceConfigurationContext(services, configuration);
        new AIModule().ConfigureServicesAsync(context).GetAwaiter().GetResult();
        return services;
    }
}
