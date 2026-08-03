
namespace Tnzi.AI.Tests;

/// <summary>
/// 验证 per-tool 授权 (GrantType=Tool) 与 per-request <see cref="AgentRunRequest.ToolNames"/>
/// 经 <see cref="AgentResolver"/> 流入 <see cref="IAgentFactory.CreateAgentAsync"/> 的 toolNames 参数。
/// 这关闭了"单工具只能整组授权"的缺口 (A4.T5)。
/// </summary>
public class AgentToolGrantResolutionTests
{
    private sealed class StubTools : IAIToolProvider;

    private static (Mock<IAgentFactory> factory, List<string>? capturedToolNames, Func<List<string>?> getCaptured) CreateCapturingFactory()
    {
        List<string>? captured = null;
        var factory = new Mock<IAgentFactory>();
        factory.Setup(f => f.CreateAgentAsync(
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<double?>(),
                It.IsAny<int?>(),
                It.IsAny<AgentExecutorOptions?>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string?, string?, string?, string?, IEnumerable<string>?, double?, int?, AgentExecutorOptions?, IEnumerable<string>?, IEnumerable<string>?, Guid?, CancellationToken>(
                (_, _, _, _, _, _, _, _, _, toolNames, _, _) =>
                {
                    captured = toolNames?.ToList();
                })
            .ReturnsAsync(new AgentExecutor(Mock.Of<IChatClient>(), new AgentExecutorOptions { Name = "test" }));

        return (factory, captured, () => captured);
    }

    private static IOptionsMonitor<AIOptions> CreateOptions() => new StaticOptionsMonitor<AIOptions>(new AIOptions
    {
        DefaultProvider = "OpenAI",
        Providers = new Dictionary<string, ProviderOptions>
        {
            ["OpenAI"] = new() { Enabled = true, ApiKey = "sk-test-12345678901234567890", DefaultModel = "gpt-4o" }
        }
    });

    private static AgentResolver CreateResolver(
        IAgentFactory factory,
        IRepository<Agent, Guid> agentRepository,
        IAgentGrantService grantService,
        IToolRegistry? toolRegistry = null)
    {
        var versionRouter = new Mock<IAgentVersionRouter>();
        versionRouter.Setup(r => r.RouteAsync(It.IsAny<Agent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Agent a, CancellationToken _) => AgentVersionRouteResult.Passthrough(a));

        return new AgentResolver(
            factory,
            CreateOptions(),
            agentRepository,
            toolRegistry ?? new ToolRegistry(Mock.Of<ILogger<ToolRegistry>>()),
            new SimplePromptTemplateEngine(),
            versionRouter.Object,
            grantService,
            Mock.Of<ILogger<AgentResolver>>());
    }

    [Fact]
    public async Task ResolveAgentAsync_DbAgentWithToolGrant_FlowsToolNamesToFactory()
    {
        var agentId = Guid.NewGuid();

        var agentRepository = new Mock<IRepository<Agent, Guid>>();
        agentRepository.Setup(r => r.GetAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Agent { Id = agentId, Name = "agent", Provider = "OpenAI", Model = "gpt-4o", IsEnabled = true });

        // The agent has a single per-tool grant ("specific_tool") - no group grants the tool.
        var grantService = new Mock<IAgentGrantService>();
        grantService.Setup(s => s.GetGrantsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentGrantsProjection { ToolNames = ["specific_tool"] });

        var (factory, _, getCaptured) = CreateCapturingFactory();
        var resolver = CreateResolver(factory.Object, agentRepository.Object, grantService.Object);

        var resolution = await resolver.ResolveAgentAsync(agentId, null, null, null, CancellationToken.None);

        resolution.IsSuccess.ShouldBeTrue();
        getCaptured().ShouldBe(["specific_tool"]);
    }

    [Fact]
    public async Task ResolveAgentAsync_AdHocRequestToolNames_FlowsToFactory()
    {
        // No AgentId, no ToolGroups - purely ad-hoc per-tool override via request.ToolNames.
        var grantService = new Mock<IAgentGrantService>();
        var agentRepository = new Mock<IRepository<Agent, Guid>>();

        var (factory, _, getCaptured) = CreateCapturingFactory();
        var resolver = CreateResolver(factory.Object, agentRepository.Object, grantService.Object);

        var resolution = await resolver.ResolveAgentAsync(
            agentId: null, provider: null, model: null,
            toolGroups: null, ct: CancellationToken.None, toolNames: ["ad_hoc_tool"]);

        resolution.IsSuccess.ShouldBeTrue();
        getCaptured().ShouldBe(["ad_hoc_tool"]);
    }

    [Fact]
    public async Task ResolveAgentAsync_DbAgent_PersistsToolNamesInCreationParameters()
    {
        // CreationParameters snapshot must carry ToolNames so SkillConstraintMiddleware
        // rebuilds preserve per-tool grants.
        var agentId = Guid.NewGuid();

        var agentRepository = new Mock<IRepository<Agent, Guid>>();
        agentRepository.Setup(r => r.GetAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Agent { Id = agentId, Name = "agent", Provider = "OpenAI", Model = "gpt-4o", IsEnabled = true });

        var grantService = new Mock<IAgentGrantService>();
        grantService.Setup(s => s.GetGrantsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentGrantsProjection { ToolNames = ["specific_tool"] });

        var (factory, _, _) = CreateCapturingFactory();
        var resolver = CreateResolver(factory.Object, agentRepository.Object, grantService.Object);

        var resolution = await resolver.ResolveAgentAsync(agentId, null, null, null, CancellationToken.None);

        resolution.CreationParameters.ShouldNotBeNull();
        resolution.CreationParameters!.ToolNames.ShouldBe(["specific_tool"]);
    }
}
