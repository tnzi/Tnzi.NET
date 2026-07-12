using Tnzi.AI;
using Tnzi.AI.Workspace;

namespace Tnzi.AI.Tests.Workspace;

/// <summary>
/// AgentResolver workspace fallback path tests
/// </summary>
public class AgentResolverWorkspaceTests
{
    private readonly Mock<IAgentFactory> _agentFactory = new();
    private readonly Mock<IRepository<Agent, Guid>> _agentRepository = new();
    private readonly Mock<IToolRegistry> _toolRegistry = new();
    private readonly Mock<IPromptTemplateEngine> _templateEngine = new();
    private readonly Mock<IAgentVersionRouter> _versionRouter = new();
    private readonly Mock<IAgentGrantService> _grantService = new();
    private readonly Mock<IWorkspaceAgentProvider> _workspaceProvider = new();
    private readonly Mock<ILogger<AgentResolver>> _logger = new();

    public AgentResolverWorkspaceTests()
    {
        // No grants seeded by default → empty projection → resolver read-fallback to entity JSON columns.
        _grantService.Setup(s => s.GetGrantsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentGrantsProjection());
    }

    private AgentResolver CreateResolver(AIOptions? options = null)
    {
        var opts = new StaticOptionsMonitor<AIOptions>(options ?? new AIOptions());
        return new AgentResolver(
            _agentFactory.Object,
            opts,
            _agentRepository.Object,
            _toolRegistry.Object,
            _templateEngine.Object,
            _versionRouter.Object,
            _grantService.Object,
            _logger.Object,
            permissionChecker: null,
            workspaceAgentProvider: _workspaceProvider.Object);
    }

    [Fact]
    public async Task ResolveAgentAsync_AgentNotInDb_FallsBackToWorkspace()
    {
        // Arrange
        var agentId = Guid.NewGuid();

        _agentRepository.Setup(r => r.GetAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Agent?)null);

        var wsDefinition = new WorkspaceAgentDefinition
        {
            AgentId = agentId.ToString(),
            Name = "workspace-agent",
            Provider = "OpenAI",
            Model = "gpt-4o",
            Instructions = "You are a helpful assistant.",
            ToolGroups = new List<string> { "general" }
        };

        _workspaceProvider.Setup(w => w.LoadAsync(It.IsAny<string>(), agentId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(wsDefinition);

        var executor = new AgentExecutor(new Mock<IChatClient>().Object, new AgentExecutorOptions());
        _agentFactory.Setup(f => f.CreateAgentAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<string>?>(), It.IsAny<double?>(), It.IsAny<int?>(),
                It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(executor);

        var resolver = CreateResolver();

        // Act
        var result = await resolver.ResolveAgentAsync(agentId, null, null, null, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        result.Agent.ShouldBe(executor);
        result.Provider.ShouldBe("OpenAI");
        result.Model.ShouldBe("gpt-4o");

        _workspaceProvider.Verify(w => w.LoadAsync(It.IsAny<string>(), agentId.ToString(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResolveAgentAsync_AgentInDb_DoesNotCallWorkspace()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var entity = new Agent
        {
            Id = agentId,
            Name = "db-agent",
            Provider = "OpenAI",
            IsEnabled = true,
            ExecutionMode = AgentExecutionMode.Single
        };

        _agentRepository.Setup(r => r.GetAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        _versionRouter.Setup(v => v.RouteAsync(entity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentVersionRouteResult.Passthrough(entity));

        _templateEngine.Setup(t => t.Render(It.IsAny<string>(), It.IsAny<IDictionary<string, string>>()))
            .Returns(string.Empty);

        var executor = new AgentExecutor(new Mock<IChatClient>().Object, new AgentExecutorOptions());
        _agentFactory.Setup(f => f.CreateAgentAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<string>?>(), It.IsAny<double?>(), It.IsAny<int?>(),
                It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(executor);

        var resolver = CreateResolver();

        // Act
        var result = await resolver.ResolveAgentAsync(agentId, null, null, null, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        _workspaceProvider.Verify(w => w.LoadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveAgentAsync_WorkspaceDisabled_SkipsWorkspace()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var options = new AIOptions { Workspace = new WorkspaceOptions { Enabled = false } };

        _agentRepository.Setup(r => r.GetAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Agent?)null);

        var resolver = CreateResolver(options);

        // Act
        var result = await resolver.ResolveAgentAsync(agentId, null, null, null, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(ErrorCodes.AgentNotFound);
        _workspaceProvider.Verify(w => w.LoadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveAgentAsync_WorkspaceReturnsNull_ReturnsFailure()
    {
        // Arrange
        var agentId = Guid.NewGuid();

        _agentRepository.Setup(r => r.GetAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Agent?)null);

        _workspaceProvider.Setup(w => w.LoadAsync(It.IsAny<string>(), agentId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkspaceAgentDefinition?)null);

        var resolver = CreateResolver();

        // Act
        var result = await resolver.ResolveAgentAsync(agentId, null, null, null, CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeFalse();
        result.ErrorCode.ShouldBe(ErrorCodes.AgentNotFound);
    }

    /// <summary>
    /// DB Agent 解析必须把 Agent.PersonaId 透传到 AgentResolution.PersonaId。
    /// 这是 Persona 注入链路的关键一环 — 历史 bug：AgentService 把 PersonaId 写到
    /// entity.PersonaId 独立列，但 AgentResolver 只把 entity.Configuration JSON 传递出去，
    /// 导致 ContextInjectionMiddleware 永远拿不到 PersonaId，&lt;soul&gt; 块永不注入。
    /// </summary>
    [Fact]
    public async Task ResolveAgentAsync_DbAgentWithPersonaId_PropagatesPersonaIdToResolution()
    {
        var agentId = Guid.NewGuid();
        var personaId = Guid.NewGuid();
        var entity = new Agent
        {
            Id = agentId,
            Name = "db-agent-with-persona",
            Provider = "OpenAI",
            Model = "gpt-4o",
            IsEnabled = true,
            ExecutionMode = AgentExecutionMode.Single,
            PersonaId = personaId
        };

        _agentRepository.Setup(r => r.GetAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        _versionRouter.Setup(v => v.RouteAsync(entity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentVersionRouteResult.Passthrough(entity));
        _templateEngine.Setup(t => t.Render(It.IsAny<string>(), It.IsAny<IDictionary<string, string>>()))
            .Returns(string.Empty);
        var executor = new AgentExecutor(new Mock<IChatClient>().Object, new AgentExecutorOptions());
        _agentFactory.Setup(f => f.CreateAgentAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<string>?>(), It.IsAny<double?>(), It.IsAny<int?>(),
                It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(executor);

        var resolver = CreateResolver();

        var result = await resolver.ResolveAgentAsync(agentId, null, null, null, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.PersonaId.ShouldBe(personaId);
        result.PersonaContent.ShouldBeNull();
    }

    /// <summary>
    /// Workspace AGENT.md frontmatter `executionMode: Handoff` must reach AgentResolution.ExecutionMode.
    /// Historical bug: AgentResolver workspace branch dropped ExecutionMode silently to Single, so a
    /// user declaring multi-agent orchestration in markdown got single-agent behaviour at runtime.
    /// </summary>
    [Fact]
    public async Task ResolveAgentAsync_WorkspaceAgentExecutionMode_PropagatesToResolution()
    {
        var agentId = Guid.NewGuid();

        _agentRepository.Setup(r => r.GetAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Agent?)null);

        var wsDefinition = new WorkspaceAgentDefinition
        {
            AgentId = agentId.ToString(),
            Name = "ws-handoff-agent",
            Provider = "OpenAI",
            Model = "gpt-4o",
            Instructions = "Route to sub-agents",
            ExecutionMode = "Handoff" // case-insensitive frontmatter value
        };
        _workspaceProvider.Setup(w => w.LoadAsync(It.IsAny<string>(), agentId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(wsDefinition);

        var executor = new AgentExecutor(new Mock<IChatClient>().Object, new AgentExecutorOptions());
        _agentFactory.Setup(f => f.CreateAgentAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<string>?>(), It.IsAny<double?>(), It.IsAny<int?>(),
                It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(executor);

        var resolver = CreateResolver();

        var result = await resolver.ResolveAgentAsync(agentId, null, null, null, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.ExecutionMode.ShouldBe(AgentExecutionMode.Handoff);
    }

    /// <summary>
    /// Workspace path must populate CreationParameters so SkillConstraintMiddleware can
    /// rebuild the executor when a skill triggers a model/provider override.
    /// Without this, workspace agents skip skill-driven model swaps silently.
    /// </summary>
    [Fact]
    public async Task ResolveAgentAsync_WorkspaceAgent_PopulatesCreationParameters()
    {
        var agentId = Guid.NewGuid();
        var toolGroups = new List<string> { "general", "code" };

        _agentRepository.Setup(r => r.GetAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Agent?)null);

        var wsDefinition = new WorkspaceAgentDefinition
        {
            AgentId = agentId.ToString(),
            Name = "ws-with-params",
            Provider = "OpenAI",
            Model = "gpt-4o",
            Instructions = "Be terse.",
            Temperature = 0.5f, // 0.5 is exact in float → no widening precision loss
            MaxTokens = 4096,
            ToolGroups = toolGroups
        };
        _workspaceProvider.Setup(w => w.LoadAsync(It.IsAny<string>(), agentId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(wsDefinition);

        var executor = new AgentExecutor(new Mock<IChatClient>().Object, new AgentExecutorOptions());
        _agentFactory.Setup(f => f.CreateAgentAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<string>?>(), It.IsAny<double?>(), It.IsAny<int?>(),
                It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(executor);

        var resolver = CreateResolver();

        var result = await resolver.ResolveAgentAsync(agentId, null, null, null, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.CreationParameters.ShouldNotBeNull();
        result.CreationParameters.Instructions.ShouldBe("Be terse.");
        result.CreationParameters.Name.ShouldBe("ws-with-params");
        result.CreationParameters.Temperature.ShouldBe(0.5);
        result.CreationParameters.MaxTokens.ShouldBe(4096);
        result.CreationParameters.ToolGroups.ShouldBe(toolGroups);
    }

    [Fact]
    public async Task ResolveAgentAsync_WorkspaceExecutionModeUnknown_FallsBackToSingle()
    {
        var agentId = Guid.NewGuid();

        _agentRepository.Setup(r => r.GetAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Agent?)null);

        var wsDefinition = new WorkspaceAgentDefinition
        {
            AgentId = agentId.ToString(),
            Name = "ws-bad-mode",
            Provider = "OpenAI",
            Model = "gpt-4o",
            ExecutionMode = "GalaxyBrain" // unknown enum value
        };
        _workspaceProvider.Setup(w => w.LoadAsync(It.IsAny<string>(), agentId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(wsDefinition);

        _agentFactory.Setup(f => f.CreateAgentAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<string>?>(), It.IsAny<double?>(), It.IsAny<int?>(),
                It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentExecutor(new Mock<IChatClient>().Object, new AgentExecutorOptions()));

        var resolver = CreateResolver();

        var result = await resolver.ResolveAgentAsync(agentId, null, null, null, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.ExecutionMode.ShouldBe(AgentExecutionMode.Single);
    }

    /// <summary>
    /// CRITICAL (A/B routing honors versioned grants): when the version router A/B-routes to a
    /// variant, the variant's resource grants come from the route result's SnapshotGrants — NOT the
    /// agent's CURRENT live grants. Seed DIFFERENT live grants to prove the SNAPSHOT wins: the
    /// resolution's KnowledgeBaseIds/SkillSlugs and the factory's toolGroups/toolNames must reflect
    /// the snapshot, and GetGrantsAsync must NOT be consulted for the resource lists.
    /// </summary>
    [Fact]
    public async Task ResolveAgentAsync_AbRoutedWithSnapshotGrants_UsesSnapshotNotLive()
    {
        var agentId = Guid.NewGuid();
        var snapshotKbId = Guid.NewGuid();
        var liveKbId = Guid.NewGuid();

        var entity = new Agent
        {
            Id = agentId,
            Name = "ab-agent",
            Provider = "OpenAI",
            Model = "gpt-4o",
            IsEnabled = true,
            ExecutionMode = AgentExecutionMode.Single
        };

        _agentRepository.Setup(r => r.GetAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        // Router A/B-routes to a variant and exposes the SNAPSHOT's grants.
        _versionRouter.Setup(v => v.RouteAsync(entity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentVersionRouteResult
            {
                Agent = entity,
                IsAbTestRouted = true,
                SelectedVersion = 2,
                SelectedVariant = "B",
                SnapshotGrants = new AgentGrantsProjection
                {
                    ToolGroups = new[] { "snapshot_group" },
                    ToolNames = new[] { "snapshot_tool" },
                    SkillSlugs = new[] { "snapshot_skill" },
                    KnowledgeBaseIds = new[] { snapshotKbId }
                }
            });

        // LIVE grants are DIFFERENT — if the resolver wrongly reads live, the asserts below fail.
        _grantService.Setup(s => s.GetGrantsAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentGrantsProjection
            {
                ToolGroups = new[] { "live_group" },
                ToolNames = new[] { "live_tool" },
                SkillSlugs = new[] { "live_skill" },
                KnowledgeBaseIds = new[] { liveKbId }
            });

        _templateEngine.Setup(t => t.Render(It.IsAny<string>(), It.IsAny<IDictionary<string, string>>()))
            .Returns(string.Empty);

        IEnumerable<string>? capturedToolGroups = null;
        IEnumerable<string>? capturedToolNames = null;
        var executor = new AgentExecutor(new Mock<IChatClient>().Object, new AgentExecutorOptions());
        _agentFactory.Setup(f => f.CreateAgentAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<string>?>(), It.IsAny<double?>(), It.IsAny<int?>(),
                It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback((string? _, string? _, string? _, string? _, IEnumerable<string>? toolGroups, double? _, int? _,
                AgentExecutorOptions? _, IEnumerable<string>? _, IEnumerable<string>? toolNames, Guid? _, CancellationToken _) =>
            {
                capturedToolGroups = toolGroups;
                capturedToolNames = toolNames;
            })
            .ReturnsAsync(executor);

        var resolver = CreateResolver();

        var result = await resolver.ResolveAgentAsync(agentId, null, null, null, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();

        // Resolution-level resource lists reflect the SNAPSHOT, not live.
        result.KnowledgeBaseIds.ShouldBe(new[] { snapshotKbId });
        result.SkillSlugs.ShouldBe(new[] { "snapshot_skill" });

        // Tool grants flow through the factory call → also from the snapshot.
        capturedToolGroups.ShouldBe(new[] { "snapshot_group" });
        capturedToolNames.ShouldBe(new[] { "snapshot_tool" });

        // The live junction must NOT be consulted when SnapshotGrants is present.
        _grantService.Verify(s => s.GetGrantsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Passthrough (SnapshotGrants == null) keeps the existing behavior: the resolver reads the
    /// LIVE grants via GetGrantsAsync.
    /// </summary>
    [Fact]
    public async Task ResolveAgentAsync_PassthroughNullSnapshotGrants_UsesLiveGrants()
    {
        var agentId = Guid.NewGuid();
        var liveKbId = Guid.NewGuid();
        var entity = new Agent
        {
            Id = agentId,
            Name = "passthrough-agent",
            Provider = "OpenAI",
            Model = "gpt-4o",
            IsEnabled = true,
            ExecutionMode = AgentExecutionMode.Single
        };

        _agentRepository.Setup(r => r.GetAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        _versionRouter.Setup(v => v.RouteAsync(entity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentVersionRouteResult.Passthrough(entity)); // SnapshotGrants == null

        _grantService.Setup(s => s.GetGrantsAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentGrantsProjection
            {
                SkillSlugs = new[] { "live_skill" },
                KnowledgeBaseIds = new[] { liveKbId }
            });

        _templateEngine.Setup(t => t.Render(It.IsAny<string>(), It.IsAny<IDictionary<string, string>>()))
            .Returns(string.Empty);
        var executor = new AgentExecutor(new Mock<IChatClient>().Object, new AgentExecutorOptions());
        _agentFactory.Setup(f => f.CreateAgentAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<string>?>(), It.IsAny<double?>(), It.IsAny<int?>(),
                It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(executor);

        var resolver = CreateResolver();

        var result = await resolver.ResolveAgentAsync(agentId, null, null, null, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.SkillSlugs.ShouldBe(new[] { "live_skill" });
        result.KnowledgeBaseIds.ShouldBe(new[] { liveKbId });

        // Passthrough → live grants ARE consulted.
        _grantService.Verify(s => s.GetGrantsAsync(agentId, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Workspace path: PERSONA.md content must reach AgentResolution.PersonaContent
    /// so the middleware injects it without any DB lookup.
    /// </summary>
    [Fact]
    public async Task ResolveAgentAsync_WorkspaceAgentWithPersonaContent_PropagatesPersonaContent()
    {
        var agentId = Guid.NewGuid();
        var personaBody = "I am a witty assistant who loves analogies.";

        _agentRepository.Setup(r => r.GetAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Agent?)null);

        var wsDefinition = new WorkspaceAgentDefinition
        {
            AgentId = agentId.ToString(),
            Name = "workspace-with-persona",
            Provider = "OpenAI",
            Model = "gpt-4o",
            Instructions = "Base instructions",
            PersonaContent = personaBody
        };
        _workspaceProvider.Setup(w => w.LoadAsync(It.IsAny<string>(), agentId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(wsDefinition);

        var executor = new AgentExecutor(new Mock<IChatClient>().Object, new AgentExecutorOptions());
        _agentFactory.Setup(f => f.CreateAgentAsync(
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<string>?>(), It.IsAny<double?>(), It.IsAny<int?>(),
                It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(executor);

        var resolver = CreateResolver();

        var result = await resolver.ResolveAgentAsync(agentId, null, null, null, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.PersonaContent.ShouldBe(personaBody);
        result.PersonaId.ShouldBeNull();
    }
}
