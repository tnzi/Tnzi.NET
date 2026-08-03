
namespace Tnzi.AI.Tests;

/// <summary>
/// AgentService 执行配置映射测试
/// </summary>
public class AgentServiceExecutionConfigTests
{
    public AgentServiceExecutionConfigTests()
    {
        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);
    }

    /// <summary>
    /// A grant service mock whose <c>GetGrantsAsync</c> returns an empty projection (no grants),
    /// so the AgentService read-fallback surfaces the legacy JSON-column values and reconcile
    /// writes are accepted as no-ops. Mirrors agents seeded without explicit grants.
    /// </summary>
    private static IAgentGrantService EmptyGrantService()
    {
        var mock = new Mock<IAgentGrantService>();
        mock.Setup(s => s.GetGrantsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentGrantsProjection());
        return mock.Object;
    }

    [Fact]
    public async Task CreateAsync_StoresAndReturnsTypedExecutionConfig()
    {
        Agent? insertedAgent = null;

        var repository = new Mock<IRepository<Agent, Guid>>();
        repository.Setup(x => x.InsertAsync(It.IsAny<Agent>(), It.IsAny<CancellationToken>()))
            .Callback<Agent, CancellationToken>((agent, _) =>
            {
                insertedAgent = agent;
                agent.Id = Guid.NewGuid();
            })
            .Returns(Task.CompletedTask);

        var service = new AgentService(
            repository.Object,
            Mock.Of<IRepository<AgentVersion, Guid>>(),
            TestDispatchFacade.Wrap(Mock.Of<IAgentRuntime>()),
            EmptyGrantService(),
            new ServiceCollection()
                .AddLogging()
                .BuildServiceProvider());

        var targetAgentId = Guid.NewGuid();
        var input = new CreateAgentDto
        {
            Name = "Coordinator",
            Provider = "test",
            ExecutionMode = AgentExecutionMode.Handoff,
            ExecutionConfig = new AgentExecutionConfigDto
            {
                Handoff = new HandoffExecutionConfigDto
                {
                    Targets = new Dictionary<string, Guid> { ["billing"] = targetAgentId },
                    MaxHandoffs = 3
                }
            }
        };

        var result = await service.CreateAsync(input);

        result.Succeeded.ShouldBeTrue();
        insertedAgent.ShouldNotBeNull();
        insertedAgent!.Configuration.ShouldNotBeNullOrWhiteSpace();
        insertedAgent.Configuration.ShouldContain("billing");

        result.Data.ShouldNotBeNull();
        result.Data.ExecutionConfig.ShouldNotBeNull();
        result.Data.ExecutionConfig!.Handoff.ShouldNotBeNull();
        result.Data.ExecutionConfig.Handoff!.Targets["billing"].ShouldBe(targetAgentId);
        result.Data.ExecutionConfig.Handoff.MaxHandoffs.ShouldBe(3);
    }

    [Fact]
    public async Task UpdateAsync_WhenExecutionModeChangesWithoutConfig_ClearsStaleConfiguration()
    {
        var agentId = Guid.NewGuid();
        var existingAgent = new Agent
        {
            Id = agentId,
            Name = "Coordinator",
            Provider = "test",
            ExecutionMode = AgentExecutionMode.Router,
            Configuration = """{"router":{"targets":{"specialist":"11111111-1111-1111-1111-111111111111"}}}"""
        };

        var repository = new Mock<IRepository<Agent, Guid>>();
        repository.Setup(x => x.GetAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAgent);
        repository.Setup(x => x.UpdateAsync(It.IsAny<Agent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var versionRepository = new Mock<IRepository<AgentVersion, Guid>>();
        versionRepository.Setup(x => x.InsertAsync(It.IsAny<AgentVersion>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        versionRepository.Setup(x => x.AsQueryable(false))
            .Returns(new List<AgentVersion>().AsQueryable());
        versionRepository.As<IQueryable<AgentVersion>>().Setup(x => x.Provider).Returns(new List<AgentVersion>().AsQueryable().Provider);
        versionRepository.As<IQueryable<AgentVersion>>().Setup(x => x.Expression).Returns(new List<AgentVersion>().AsQueryable().Expression);
        versionRepository.As<IQueryable<AgentVersion>>().Setup(x => x.ElementType).Returns(new List<AgentVersion>().AsQueryable().ElementType);
        versionRepository.As<IQueryable<AgentVersion>>().Setup(x => x.GetEnumerator()).Returns(() => new List<AgentVersion>().GetEnumerator());

        var service = new AgentService(
            repository.Object,
            versionRepository.Object,
            TestDispatchFacade.Wrap(Mock.Of<IAgentRuntime>()),
            EmptyGrantService(),
            new ServiceCollection()
                .AddLogging()
                .BuildServiceProvider());

        var result = await service.UpdateAsync(agentId, new UpdateAgentDto
        {
            ExecutionMode = AgentExecutionMode.Single
        });

        result.Succeeded.ShouldBeTrue();
        existingAgent.ExecutionMode.ShouldBe(AgentExecutionMode.Single);
        existingAgent.Configuration.ShouldBeNull();
        result.Data.ShouldNotBeNull();
        result.Data!.ExecutionConfig.ShouldBeNull();
    }

    [Fact]
    public async Task CreateAsync_StoresAndReturnsRouterConfig()
    {
        Agent? insertedAgent = null;

        var repository = new Mock<IRepository<Agent, Guid>>();
        repository.Setup(x => x.InsertAsync(It.IsAny<Agent>(), It.IsAny<CancellationToken>()))
            .Callback<Agent, CancellationToken>((agent, _) =>
            {
                insertedAgent = agent;
                agent.Id = Guid.NewGuid();
            })
            .Returns(Task.CompletedTask);

        var service = new AgentService(
            repository.Object,
            Mock.Of<IRepository<AgentVersion, Guid>>(),
            TestDispatchFacade.Wrap(Mock.Of<IAgentRuntime>()),
            EmptyGrantService(),
            new ServiceCollection()
                .AddLogging()
                .BuildServiceProvider());

        var routerTargetId = Guid.NewGuid();
        var input = new CreateAgentDto
        {
            Name = "Router",
            Provider = "test",
            ExecutionMode = AgentExecutionMode.Router,
            ExecutionConfig = new AgentExecutionConfigDto
            {
                Router = new RouterExecutionConfigDto
                {
                    Targets = new Dictionary<string, Guid> { ["specialist"] = routerTargetId },
                    AllowDirectResponse = false
                }
            }
        };

        var result = await service.CreateAsync(input);

        result.Succeeded.ShouldBeTrue();
        insertedAgent.ShouldNotBeNull();
        insertedAgent!.Configuration.ShouldNotBeNull();
        insertedAgent.Configuration!.ShouldContain("router");

        result.Data.ShouldNotBeNull();
        result.Data!.ExecutionConfig.ShouldNotBeNull();
        result.Data.ExecutionConfig!.Router.ShouldNotBeNull();
        result.Data.ExecutionConfig.Router!.Targets["specialist"].ShouldBe(routerTargetId);
        result.Data.ExecutionConfig.Router.AllowDirectResponse.ShouldBe(false);
    }
}
