using Tnzi.AI.Tools.Models;

namespace Tnzi.AI.Tests.Engine;

/// <summary>
/// AgentFactory - verifies that ToolDefinitions is populated from IToolRegistry
/// so parallel-tool-execution and GracefulShutdown interrupt actually work.
/// </summary>
public class AgentFactoryToolDefinitionsTests
{
    /// <summary>
    /// When an agent has tool groups containing concurrency-safe / GracefulShutdown tools,
    /// AgentExecutorOptions.ToolDefinitions must reflect those flags after CreateAgentAsync.
    /// </summary>
    [Fact]
    public async Task CreateAgentAsync_ToolGroupsWithConcurrencySafeTool_PopulatesToolDefinitions()
    {
        // Arrange
        var toolDef = new ToolDefinition
        {
            Name = "read_file",
            GroupName = "fs",
            IsConcurrencySafe = true,
            IsDestructive = false,
            InterruptBehavior = ToolInterruptBehavior.GracefulShutdown,
            ProviderType = typeof(object),
            MethodInfo = typeof(object).GetMethod("ToString")!
        };

        var toolRegistry = new Mock<IToolRegistry>();
        toolRegistry
            .Setup(r => r.GetToolsByGroupsWithPermissions(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IEnumerable<string>?>()))
            .Returns([toolDef]);

        var aiTool = AIFunctionFactory.Create(() => "result", "read_file", "Read a file");
        var toolResolver = new Mock<IToolResolver>();
        toolResolver
            .Setup(r => r.ResolveToolsAsync(
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([aiTool]);

        var factory = BuildFactory(toolRegistry.Object, toolResolver.Object);

        // Act
        var executor = await factory.CreateAgentAsync(
            toolGroups: ["fs"],
            ct: CancellationToken.None);

        // Assert - executor's options must carry the populated ToolDefinitions dict
        executor.ShouldNotBeNull();

        // Verify via CanExecuteConcurrently - the executor must see ToolDefinitions
        // for two concurrent calls to be allowed (both tools same name = 1 call, so
        // we verify the behavior indirectly by checking CanExecuteConcurrently logic)
        var defs = GetToolDefinitions(executor);
        defs.ShouldNotBeNull("ToolDefinitions should be populated from IToolRegistry");
        defs!.ContainsKey("read_file").ShouldBeTrue();
        defs["read_file"].IsConcurrencySafe.ShouldBeTrue();
        defs["read_file"].InterruptBehavior.ShouldBe(ToolInterruptBehavior.GracefulShutdown);
    }

    /// <summary>
    /// When toolGroups is null (no C# tools), ToolDefinitions must remain null.
    /// </summary>
    [Fact]
    public async Task CreateAgentAsync_NullToolGroups_ToolDefinitionsRemainsNull()
    {
        var toolRegistry = new Mock<IToolRegistry>();
        var toolResolver = new Mock<IToolResolver>();
        toolResolver
            .Setup(r => r.ResolveToolsAsync(
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IList<AITool>?)null);

        var factory = BuildFactory(toolRegistry.Object, toolResolver.Object);

        // Act - no toolGroups specified
        var executor = await factory.CreateAgentAsync(
            toolGroups: null,
            ct: CancellationToken.None);

        executor.ShouldNotBeNull();
        GetToolDefinitions(executor).ShouldBeNull("No tool groups → no definitions to populate");
        toolRegistry.Verify(
            r => r.GetToolsByGroupsWithPermissions(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IEnumerable<string>?>()),
            Times.Never,
            "Registry should not be queried when toolGroups is null");
    }

    /// <summary>
    /// When the registry returns an empty list (unknown group), ToolDefinitions must also be null.
    /// </summary>
    [Fact]
    public async Task CreateAgentAsync_RegistryReturnsEmpty_ToolDefinitionsRemainsNull()
    {
        var toolRegistry = new Mock<IToolRegistry>();
        toolRegistry
            .Setup(r => r.GetToolsByGroupsWithPermissions(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IEnumerable<string>?>()))
            .Returns([]);

        var toolResolver = new Mock<IToolResolver>();
        toolResolver
            .Setup(r => r.ResolveToolsAsync(
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IList<AITool>?)null);

        var factory = BuildFactory(toolRegistry.Object, toolResolver.Object);

        var executor = await factory.CreateAgentAsync(
            toolGroups: ["unknown-group"],
            ct: CancellationToken.None);

        executor.ShouldNotBeNull();
        GetToolDefinitions(executor).ShouldBeNull(
            "Empty registry result → no definitions dict (stays null for safe sequential fallback)");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static AgentFactory BuildFactory(IToolRegistry toolRegistry, IToolResolver toolResolver)
    {
        var options = new AIOptions
        {
            DefaultProvider = "test",
            Providers = new Dictionary<string, ProviderOptions>
            {
                ["test"] = new() { Enabled = true, DefaultModel = "m1" }
            }
        };
        var opts = new StaticOptionsMonitor<AIOptions>(options);

        var chatClient = new Mock<IChatClient>();
        chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        var chatClientFactory = new Mock<IChatClientFactory>();
        chatClientFactory
            .Setup(f => f.GetChatClient(It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(chatClient.Object);

        // Build a minimal AgentExecutorOptionsBuilder
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddSingleton(opts);
        services.AddSingleton(chatClientFactory.Object);
        services.AddSingleton<ITokenEstimator>(new HeuristicTokenEstimator());
        services.AddSingleton<IAgentExecutionContextAccessor>(new AgentExecutionContextAccessor());
        var sp = services.BuildServiceProvider();

        var optionsBuilder = new AgentExecutorOptionsBuilder(
            opts,
            sp.GetRequiredService<ILoggerFactory>(),
            chatClientFactory.Object,
            middlewares: [],
            tokenEstimator: new HeuristicTokenEstimator(),
            logger: NullLogger<AgentExecutorOptionsBuilder>.Instance);

        return new AgentFactory(
            chatClientFactory.Object,
            opts,
            toolResolver,
            optionsBuilder,
            toolRegistry: toolRegistry,
            logger: NullLogger<AgentFactory>.Instance);
    }

    /// <summary>
    /// Reads ToolDefinitions from an AgentExecutor via the internal options.
    /// Uses reflection because AgentExecutorOptions.ToolDefinitions is internal to the executor.
    /// </summary>
    private static IReadOnlyDictionary<string, ToolDefinition>? GetToolDefinitions(IAgentExecutor executor)
    {
        var optionsField = typeof(AgentExecutor)
            .GetField("_options", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var options = optionsField?.GetValue(executor) as AgentExecutorOptions;
        return options?.ToolDefinitions;
    }
}
