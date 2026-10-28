using Microsoft.Data.Sqlite;

namespace Tnzi.AI.Tests.Infrastructure;

/// <summary>
/// 管理端登记在库里的提供商（Agent 编辑器的下拉框把它们列在最前面）必须能被 Agent 真正用上。
/// </summary>
/// <remarks>
/// <see cref="ChatClientFactory"/> 一直按「库里的启用行 > 配置」解析，但 <see cref="AgentFactory"/> 在调用它之前
/// 先拿 <c>AI:Providers</c> 预检：只在库里的名字抛 "is not configured"，库里重新启用一个配置里 Enabled=false 的
/// 同名条目抛 "is disabled"。每一条 Agent 路径都经过这个工厂，绑定这类提供商的 Agent 一次对话也跑不起来。
/// 真实 SQLite + EFCoreRepository + 真实 ChatClientFactory，不 mock 被测对象。
/// </remarks>
public class DatabaseProviderResolutionTests : IDisposable
{
    private const string DatabaseOnly = "acme-llm";
    private const string ReEnabled = "Dormant";

    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _serviceProvider;
    private readonly AIOptions _options;

    public DatabaseProviderResolutionTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(u => u.Id).Returns((Guid?)null);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => new AgentRunFkDbContext(
            new DbContextOptionsBuilder<AgentRunFkDbContext>().UseSqlite(_connection).Options, currentUser.Object));
        services.AddScoped<IRepository<Provider, Guid>, EFCoreRepository<AgentRunFkDbContext, Provider, Guid>>();
        _serviceProvider = services.BuildServiceProvider();

        using (var scope = _serviceProvider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AgentRunFkDbContext>();
            context.Database.EnsureCreated();
            context.Set<Provider>().AddRange(
                new Provider { Name = DatabaseOnly, ProviderType = "OpenAI", IsEnabled = true, Endpoint = "https://llm.acme.example/v1", DefaultModel = "acme-1" },
                new Provider { Name = ReEnabled, ProviderType = "OpenAI", IsEnabled = true, DefaultModel = "dormant-2" });
            context.SaveChangesAsync().GetAwaiter().GetResult();
        }

        _options = new AIOptions
        {
            DefaultProvider = "OpenAI",
            Providers = new Dictionary<string, ProviderOptions>
            {
                ["OpenAI"] = new() { Name = "OpenAI", Enabled = true, ApiKey = "config-key", DefaultModel = "gpt-4o" },
                [ReEnabled] = new() { Name = ReEnabled, Enabled = false, ApiKey = "config-dormant", DefaultModel = "dormant-1" }
            }
        };
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        _connection.Close();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private ChatClientFactory CreateChatClientFactory(AIOptions? options = null)
        => new(
            new StaticOptionsMonitor<AIOptions>(options ?? _options),
            [new StubChatClientProvider()],
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ChatClientFactory>.Instance);

    private AgentFactory CreateAgentFactory(IChatClientFactory chatClientFactory)
    {
        var opts = new StaticOptionsMonitor<AIOptions>(_options);
        var toolResolver = new Mock<IToolResolver>();
        toolResolver.Setup(r => r.ResolveToolsAsync(
                It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var optionsBuilder = new AgentExecutorOptionsBuilder(
            opts,
            _serviceProvider.GetRequiredService<ILoggerFactory>(),
            chatClientFactory,
            middlewares: [],
            tokenEstimator: new HeuristicTokenEstimator(),
            logger: NullLogger<AgentExecutorOptionsBuilder>.Instance);

        return new AgentFactory(chatClientFactory, opts, toolResolver.Object, optionsBuilder, Mock.Of<IToolRegistry>(), NullLogger<AgentFactory>.Instance);
    }

    [Fact]
    public async Task CreateAgent_ProviderRegisteredOnlyInTheDatabase_Succeeds()
    {
        var factory = CreateAgentFactory(CreateChatClientFactory());

        var executor = await factory.CreateAgentAsync(providerName: DatabaseOnly, name: "Billing");

        executor.ShouldNotBeNull();
    }

    [Fact]
    public async Task CreateAgent_DatabaseRowReEnablesADisabledConfigEntry_Succeeds()
    {
        var factory = CreateAgentFactory(CreateChatClientFactory());

        var executor = await factory.CreateAgentAsync(providerName: ReEnabled, name: "Billing");

        executor.ShouldNotBeNull();
    }

    [Fact]
    public async Task CreateAgent_ProviderUnknownEverywhere_StillFailsWithTheName()
    {
        var factory = CreateAgentFactory(CreateChatClientFactory());

        var ex = await Should.ThrowAsync<InvalidOperationException>(() => factory.CreateAgentAsync(providerName: "nowhere"));

        ex.Message.ShouldContain("nowhere");
    }

    /// <summary>
    /// 省略 provider 的 YAML Agent 定义在库里存空串（列非空），它的意思是「用默认提供商」。
    /// 此前空串一路传到工厂，每次运行都以「没有指定提供商」失败。
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAgent_BlankProvider_UsesTheDefaultProvider(string blank)
    {
        var factory = CreateAgentFactory(CreateChatClientFactory());

        var executor = await factory.CreateAgentAsync(providerName: blank, name: "Support");

        executor.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ChatClientFactory_BlankProviderName_AnswersForTheDefaultProvider(string blank)
    {
        IChatClientFactory factory = CreateChatClientFactory();

        factory.IsProviderAvailable(blank).ShouldBeTrue();
        factory.GetDefaultModel(blank).ShouldBe("gpt-4o");
        Should.NotThrow(() => factory.GetChatClient(blank));
    }

    [Fact]
    public void ChatClientFactory_AnswersByNameAndDefaultModelAsGetChatClientWould()
    {
        IChatClientFactory factory = CreateChatClientFactory();

        factory.IsProviderAvailable(DatabaseOnly).ShouldBeTrue();
        factory.IsProviderAvailable(ReEnabled).ShouldBeTrue();
        factory.IsProviderAvailable("OpenAI").ShouldBeTrue();
        factory.IsProviderAvailable("nowhere").ShouldBeFalse();

        factory.GetDefaultModel(DatabaseOnly).ShouldBe("acme-1");
        factory.GetDefaultModel(ReEnabled).ShouldBe("dormant-2");
        factory.GetDefaultModel("nowhere").ShouldBeNull();
    }

    /// <summary>
    /// 只在管理端登记提供商的部署是受支持的形态；开了 Summarize 也不能因为配置里一个提供商都没有就起不来。
    /// </summary>
    [Fact]
    public void StartupValidation_SummarizeWithOnlyDatabaseProviders_Starts()
    {
        var sp = BuildStartupServices(withDatabase: true);

        Should.NotThrow(() => AIModule.ValidateProviderRuntimeConfiguration(sp, NullLogger.Instance));
    }

    [Fact]
    public void StartupValidation_SummarizeWithNoProviderAnywhere_StillFailsFast()
    {
        var sp = BuildStartupServices(withDatabase: false);

        var ex = Should.Throw<InvalidOperationException>(() => AIModule.ValidateProviderRuntimeConfiguration(sp, NullLogger.Instance));
        ex.Message.ShouldContain("Summarize");
    }

    private IServiceProvider BuildStartupServices(bool withDatabase)
    {
        var options = new AIOptions();
        options.History.Reduction.Mode = HistoryReductionMode.Summarize;

        var services = new ServiceCollection();
        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(options));
        if (withDatabase)
        {
            services.AddScoped(_ => _serviceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope()
                .ServiceProvider.GetRequiredService<IRepository<Provider, Guid>>());
        }

        return services.BuildServiceProvider();
    }

    private sealed class StubChatClientProvider : IChatClientProvider
    {
        public string ProviderName => "OpenAI";

        public IChatClient CreateChatClient(ProviderOptions options, string model) => Mock.Of<IChatClient>();

        public IEmbeddingGenerator<string, Embedding<float>>? CreateEmbeddingGenerator(ProviderOptions options, string model) => null;
    }
}
