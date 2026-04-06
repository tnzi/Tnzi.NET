using Microsoft.Extensions.DependencyInjection;

namespace Tnzi.AI.Tests;

public class SubAgentExecutionServiceTests
{
    [Fact]
    public async Task SpawnAsync_CreatesRunAndInvokesRuntimeWithExistingRunId()
    {
        var agentId = Guid.NewGuid();
        var createdRunId = Guid.NewGuid();
        var runtimeCalled = new TaskCompletionSource<AgentRunRequest>(TaskCreationOptions.RunContinuationsAsynchronously);

        var resolver = new Mock<IAgentResolver>();
        resolver.Setup(x => x.ResolveAgentAsync(
                agentId,
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentResolution.SuccessWithoutExecutor("openai", "gpt-5.4", agentId, null, AgentExecutionMode.Single));

        var runStore = new Mock<IRunStore>();
        runStore.Setup(x => x.CreateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRun run, CancellationToken _) =>
            {
                run.Id = createdRunId;
                return run;
            });

        var subAgentRegistry = new Mock<ISubAgentRegistry>();
        var executionContext = new AgentExecutionContextAccessor();

        var runtime = new Mock<IAgentRuntime>();
        runtime.Setup(x => x.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .Returns<AgentRunRequest, CancellationToken>((request, _) =>
            {
                runtimeCalled.TrySetResult(request);
                return Task.FromResult(new AgentRunResult
                {
                    Response = "ok",
                    RunId = request.ExistingRunId,
                    Status = AgentRunStatus.Completed
                });
            });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => runtime.Object);
        services.AddScoped<IAgentRuntime>(_ => runtime.Object);
        var provider = services.BuildServiceProvider();

        var service = new SubAgentExecutionService(
            resolver.Object,
            runStore.Object,
            subAgentRegistry.Object,
            executionContext,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SubAgentExecutionService>.Instance,
            provider);

        var result = await service.SpawnAsync(new SpawnAgentRunInput
        {
            AgentId = agentId,
            Message = "background run"
        });

        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.RunId.ShouldBe(createdRunId);

        var runtimeRequest = await runtimeCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        runtimeRequest.ExistingRunId.ShouldBe(createdRunId);
        runtimeRequest.AgentId.ShouldBe(agentId);
        runtimeRequest.EnableRunTracking.ShouldBeTrue();
    }

    [Fact]
    public async Task SpawnAsync_SubAgentType_UsesTemplateToolGroups()
    {
        var runtimeCalled = new TaskCompletionSource<AgentRunRequest>(TaskCreationOptions.RunContinuationsAsynchronously);

        var resolver = new Mock<IAgentResolver>();
        resolver.Setup(x => x.ResolveAgentAsync(
                null,
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentResolution.SuccessWithoutExecutor("openai", "gpt-5.4", null, null, AgentExecutionMode.Single));

        var runStore = new Mock<IRunStore>();
        runStore.Setup(x => x.CreateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRun run, CancellationToken _) =>
            {
                run.Id = Guid.NewGuid();
                return run;
            });

        var subAgentRegistry = new Mock<ISubAgentRegistry>();
        subAgentRegistry.Setup(x => x.Get("researcher"))
            .Returns(new SubAgentTypeDefinition(
                Name: "researcher",
                Description: "Research tasks",
                ToolGroups: ["web-search", "file"],
                ExcludedToolGroups: [],
                MaxTurns: 10,
                DefaultModel: "gpt-5.4"));

        var runtime = new Mock<IAgentRuntime>();
        runtime.Setup(x => x.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .Returns<AgentRunRequest, CancellationToken>((request, _) =>
            {
                runtimeCalled.TrySetResult(request);
                return Task.FromResult(new AgentRunResult { Response = "ok" });
            });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => runtime.Object);
        services.AddScoped<IAgentRuntime>(_ => runtime.Object);
        var provider = services.BuildServiceProvider();

        var service = new SubAgentExecutionService(
            resolver.Object,
            runStore.Object,
            subAgentRegistry.Object,
            new AgentExecutionContextAccessor(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SubAgentExecutionService>.Instance,
            provider);

        var result = await service.SpawnAsync(new SpawnAgentRunInput
        {
            Message = "collect references",
            SubAgentType = "researcher"
        });

        result.Succeeded.ShouldBeTrue();

        var runtimeRequest = await runtimeCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        runtimeRequest.ToolGroups.ShouldNotBeNull();
        runtimeRequest.ToolGroups.ShouldContain("web-search");
        runtimeRequest.ToolGroups.ShouldContain("file");
        runtimeRequest.Model.ShouldBe("gpt-5.4");
    }
}
