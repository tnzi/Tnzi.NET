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
}
