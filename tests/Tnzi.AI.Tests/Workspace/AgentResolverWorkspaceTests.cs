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
    private readonly Mock<IWorkspaceAgentProvider> _workspaceProvider = new();
    private readonly Mock<ILogger<AgentResolver>> _logger = new();

    private AgentResolver CreateResolver(AIOptions? options = null)
    {
        var opts = Microsoft.Extensions.Options.Options.Create(options ?? new AIOptions());
        return new AgentResolver(
            _agentFactory.Object,
            opts,
            _agentRepository.Object,
            _toolRegistry.Object,
            _templateEngine.Object,
            _versionRouter.Object,
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
                It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<Guid?>(),
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
                It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<Guid?>(),
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
                It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(executor);

        var resolver = CreateResolver();

        var result = await resolver.ResolveAgentAsync(agentId, null, null, null, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.PersonaId.ShouldBe(personaId);
        result.PersonaContent.ShouldBeNull();
    }

    /// <summary>
    /// ExternalCli mode bypasses ChatClient creation but the PersonaId still flows
    /// through to AgentResolution so downstream telemetry/inspection can see it.
    /// </summary>
    [Fact]
    public async Task ResolveAgentAsync_ExternalCliAgentWithPersonaId_PropagatesPersonaId()
    {
        var agentId = Guid.NewGuid();
        var personaId = Guid.NewGuid();
        var entity = new Agent
        {
            Id = agentId,
            Name = "cli-agent",
            Provider = "claude-code",
            IsEnabled = true,
            ExecutionMode = AgentExecutionMode.ExternalCli,
            PersonaId = personaId
        };

        _agentRepository.Setup(r => r.GetAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        _versionRouter.Setup(v => v.RouteAsync(entity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentVersionRouteResult.Passthrough(entity));
        _templateEngine.Setup(t => t.Render(It.IsAny<string>(), It.IsAny<IDictionary<string, string>>()))
            .Returns(string.Empty);

        var resolver = CreateResolver();

        var result = await resolver.ResolveAgentAsync(agentId, null, null, null, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.PersonaId.ShouldBe(personaId);
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
                It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<Guid?>(),
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
                It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<Guid?>(),
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
                It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentExecutor(new Mock<IChatClient>().Object, new AgentExecutorOptions()));

        var resolver = CreateResolver();

        var result = await resolver.ResolveAgentAsync(agentId, null, null, null, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.ExecutionMode.ShouldBe(AgentExecutionMode.Single);
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
                It.IsAny<AgentExecutorOptions?>(), It.IsAny<IEnumerable<string>?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(executor);

        var resolver = CreateResolver();

        var result = await resolver.ResolveAgentAsync(agentId, null, null, null, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.PersonaContent.ShouldBe(personaBody);
        result.PersonaId.ShouldBeNull();
    }
}
