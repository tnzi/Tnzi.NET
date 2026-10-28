namespace Tnzi.AI.Tests.Engine;

/// <summary>
/// 解析一个 Agent 定义时，主路径之外的入口（工作区 Agent、多 Agent 策略里的子 / 目标 Agent、
/// 省略 provider 的 YAML 定义）必须得到与主路径相同的结果。
/// </summary>
public class AgentDefinitionResolutionTests
{
    private const string DefaultProvider = "OpenAI";

    // ---------------------------------------------------------------- 空白 provider

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DbAgentWithBlankProvider_RunsOnTheDefaultProviderAndReportsIt(string blank)
    {
        // 省略 provider 的 YAML 定义在库里存空串（列非空）。原样传下去每次运行都失败，用量与配额还记在 "" 名下
        var agentId = Guid.NewGuid();
        var repository = new Mock<IRepository<Agent, Guid>>();
        repository.Setup(r => r.GetAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Agent { Id = agentId, Name = "support", Provider = blank, IsEnabled = true });

        var (factory, captured) = CapturingFactory();
        var resolver = CreateResolver(factory.Object, repository.Object);

        var resolution = await resolver.ResolveAgentAsync(agentId, null, null, null, CancellationToken.None);

        resolution.IsSuccess.ShouldBeTrue();
        resolution.Provider.ShouldBe(DefaultProvider);
        captured.Provider.ShouldBe(DefaultProvider);
    }

    // ---------------------------------------------------------------- 指令模板渲染

    [Fact]
    public async Task WorkspaceAgent_InstructionsAreRenderedLikeDatabaseAgents()
    {
        var agentId = Guid.NewGuid();
        var repository = new Mock<IRepository<Agent, Guid>>();
        repository.Setup(r => r.GetAsync(agentId, It.IsAny<CancellationToken>())).ReturnsAsync((Agent?)null);

        var workspace = new Mock<IWorkspaceAgentProvider>();
        workspace.Setup(w => w.LoadAsync(It.IsAny<string>(), agentId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkspaceAgentDefinition
            {
                AgentId = agentId.ToString(),
                Name = "reviewer",
                Provider = DefaultProvider,
                Instructions = "You are {{agent.name}}. Today is {{date}}."
            });

        var (factory, captured) = CapturingFactory();
        var options = new AIOptions { DefaultProvider = DefaultProvider };
        options.Workspace.Enabled = true;
        var resolver = CreateResolver(factory.Object, repository.Object, options, workspace.Object);

        var resolution = await resolver.ResolveAgentAsync(agentId, null, null, null, CancellationToken.None);

        resolution.IsSuccess.ShouldBeTrue();
        captured.Instructions.ShouldBe($"You are reviewer. Today is {DateTime.UtcNow:yyyy-MM-dd}.");
        resolution.CreationParameters!.Instructions.ShouldBe(captured.Instructions);
    }

    [Fact]
    public async Task StrategyChildAgent_InstructionsAreRenderedLikeThePrimaryAgent()
    {
        // Handoff / Router / AgentAsTools 的目标与子 Agent 走 ExecutionStrategyAgentLoader，不渲染时模型读到字面量 {{date}}
        var childId = Guid.NewGuid();
        var repository = new Mock<IRepository<Agent, Guid>>();
        repository.Setup(r => r.GetAsync(childId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Agent { Id = childId, Name = "researcher", Provider = DefaultProvider, IsEnabled = true, Instructions = "You are {{agent.name}}. Today is {{date}}." });

        var (factory, captured) = CapturingFactory();
        var services = new ServiceCollection();
        var grants = new Mock<IAgentGrantService>();
        grants.Setup(s => s.GetGrantsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentGrantsProjection());
        services.AddSingleton(grants.Object);
        services.AddSingleton<IUserToolPermissionResolver>(new UserToolPermissionResolver(
            new ToolRegistry(Mock.Of<ILogger<ToolRegistry>>()), Mock.Of<ILogger<UserToolPermissionResolver>>()));
        services.AddSingleton<IPromptTemplateEngine>(new SimplePromptTemplateEngine());

        var context = new ExecutionStrategyContext
        {
            AgentFactory = factory.Object,
            AgentRepository = repository.Object,
            ServiceProvider = services.BuildServiceProvider(),
            ExecutionContextAccessor = new AgentExecutionContextAccessor(),
            Logger = Mock.Of<ILogger>()
        };

        var child = await ExecutionStrategyAgentLoader.ResolveAgentAsync(childId, context, CancellationToken.None);

        child.ShouldNotBeNull();
        captured.Instructions.ShouldBe($"You are researcher. Today is {DateTime.UtcNow:yyyy-MM-dd}.");
    }

    // ---------------------------------------------------------------- AgentResolution.WithExecutor

    [Fact]
    public void WithExecutor_CopiesEveryOtherField()
    {
        // 解析之后换模型（think 别名）用它产出副本；新加到 AgentResolution 上的字段漏抄 = 那次运行悄悄丢掉它
        var original = new AgentResolution
        {
            Agent = new AgentExecutor(Mock.Of<IChatClient>(), new AgentExecutorOptions { Name = "a" }),
            Provider = "p",
            Model = "m",
            AgentId = Guid.NewGuid(),
            ExecutionMode = AgentExecutionMode.Router,
            AgentConfiguration = "{}",
            PersonaContent = "soul",
            KnowledgeBaseIds = [Guid.NewGuid()],
            SkillSlugs = ["s"],
            ErrorCode = "E",
            CreationParameters = new AgentCreationParameters("i", "n", null, null, null, null)
        };
        var replacement = new AgentExecutor(Mock.Of<IChatClient>(), new AgentExecutorOptions { Name = "b" });

        var copy = original.WithExecutor(replacement, "think");

        copy.Agent.ShouldBeSameAs(replacement);
        copy.Model.ShouldBe("think");
        foreach (var property in typeof(AgentResolution).GetProperties().Where(p => p.CanWrite))
        {
            if (property.Name is nameof(AgentResolution.Agent) or nameof(AgentResolution.Model))
                continue;
            property.GetValue(copy).ShouldBe(property.GetValue(original), $"WithExecutor must copy {property.Name}");
        }
    }

    // ---------------------------------------------------------------- helpers

    private sealed class Captured
    {
        public string? Provider { get; set; }
        public string? Instructions { get; set; }
    }

    private static (Mock<IAgentFactory> Factory, Captured Captured) CapturingFactory()
    {
        var captured = new Captured();
        var factory = new Mock<IAgentFactory>();
        factory.Setup(f => f.CreateAgentAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<string>?>(), It.IsAny<double?>(), It.IsAny<int?>(),
                It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<string?, string?, string?, string?, IEnumerable<string>?, double?, int?, AgentExecutorOptions?, IEnumerable<string>?, IEnumerable<string>?, Guid?, CancellationToken>(
                (provider, _, instructions, _, _, _, _, _, _, _, _, _) =>
                {
                    captured.Provider = provider;
                    captured.Instructions = instructions;
                })
            .ReturnsAsync(new AgentExecutor(Mock.Of<IChatClient>(), new AgentExecutorOptions { Name = "test" }));
        return (factory, captured);
    }

    private static AgentResolver CreateResolver(IAgentFactory factory, IRepository<Agent, Guid> repository, AIOptions? options = null, IWorkspaceAgentProvider? workspace = null)
    {
        var versionRouter = new Mock<IAgentVersionRouter>();
        versionRouter.Setup(r => r.RouteAsync(It.IsAny<Agent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Agent a, CancellationToken _) => AgentVersionRouteResult.Passthrough(a));

        var grants = new Mock<IAgentGrantService>();
        grants.Setup(s => s.GetGrantsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentGrantsProjection());

        return new AgentResolver(
            factory,
            new StaticOptionsMonitor<AIOptions>(options ?? new AIOptions { DefaultProvider = DefaultProvider }),
            repository,
            new UserToolPermissionResolver(new ToolRegistry(Mock.Of<ILogger<ToolRegistry>>()), Mock.Of<ILogger<UserToolPermissionResolver>>()),
            new SimplePromptTemplateEngine(),
            versionRouter.Object,
            grants.Object,
            Mock.Of<ILogger<AgentResolver>>(),
            workspaceAgentProvider: workspace);
    }
}
