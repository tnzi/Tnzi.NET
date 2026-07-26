using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tnzi.AI.Tests;

public class SubAgentExecutionServiceTests
{
    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static IOptionsMonitor<SubAgentOptions> DefaultOptions(Action<SubAgentOptions>? configure = null)
    {
        var opts = new SubAgentOptions();
        configure?.Invoke(opts);
        return new ConstantOptionsMonitor<SubAgentOptions>(opts);
    }

    private static SubAgentExecutionService CreateService(
        IAgentResolver resolver,
        IRunStore runStore,
        ISubAgentRegistry subAgentRegistry,
        IAgentExecutionContextAccessor executionContext,
        IServiceScopeFactory scopeFactory,
        IServiceProvider provider,
        IOptionsMonitor<SubAgentOptions>? options = null)
    {
        return new SubAgentExecutionService(
            resolver,
            runStore,
            subAgentRegistry,
            executionContext,
            scopeFactory,
            NullLogger<SubAgentExecutionService>.Instance,
            provider,
            options ?? DefaultOptions());
    }

    // ------------------------------------------------------------------
    // Original tests (updated to pass new required IOptionsMonitor arg)
    // ------------------------------------------------------------------

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
            .ReturnsAsync(AgentResolution.Success(Mock.Of<IAgentExecutor>(), "openai", "gpt-5.4", agentId, null, AgentExecutionMode.Single));

        var runStore = new Mock<IRunStore>();
        runStore.Setup(x => x.CreateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRun run, CancellationToken _) =>
            {
                run.Id = createdRunId;
                return run;
            });
        runStore.Setup(x => x.CountDescendantsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        runStore.Setup(x => x.GetParentRunIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

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

        var service = CreateService(
            resolver.Object, runStore.Object, subAgentRegistry.Object, executionContext,
            provider.GetRequiredService<IServiceScopeFactory>(), provider);

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
            .ReturnsAsync(AgentResolution.Success(Mock.Of<IAgentExecutor>(), "openai", "gpt-5.4", null, null, AgentExecutionMode.Single));

        var runStore = new Mock<IRunStore>();
        runStore.Setup(x => x.CreateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRun run, CancellationToken _) =>
            {
                run.Id = Guid.NewGuid();
                return run;
            });
        runStore.Setup(x => x.CountDescendantsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        runStore.Setup(x => x.GetParentRunIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

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

        var service = CreateService(
            resolver.Object, runStore.Object, subAgentRegistry.Object,
            new AgentExecutionContextAccessor(),
            provider.GetRequiredService<IServiceScopeFactory>(), provider);

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

    // ------------------------------------------------------------------
    // B11: depth + descendant cap enforcement
    // ------------------------------------------------------------------

    private (Mock<IAgentResolver>, Mock<IRunStore>, Mock<ISubAgentRegistry>, ServiceProvider) BuildBasicDeps(Guid agentId, Guid? parentRunId = null)
    {
        var resolver = new Mock<IAgentResolver>();
        resolver.Setup(x => x.ResolveAgentAsync(
                agentId,
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentResolution.Success(Mock.Of<IAgentExecutor>(), "openai", "gpt-5.4", agentId, null, AgentExecutionMode.Single));

        var runStore = new Mock<IRunStore>();
        runStore.Setup(x => x.CreateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRun run, CancellationToken _) =>
            {
                run.Id = Guid.NewGuid();
                return run;
            });

        var subAgentRegistry = new Mock<ISubAgentRegistry>();

        var services = new ServiceCollection();
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        return (resolver, runStore, subAgentRegistry, provider);
    }

    [Fact]
    public async Task SpawnAsync_ExceedsMaxDepth_ReturnsFailure()
    {
        // Simulate a chain already at MaxDepth = 2
        // parentRunId → grandparentRunId → null (depth 2, child would be depth 3 which exceeds MaxDepth=2)
        var agentId = Guid.NewGuid();
        var parentRunId = Guid.NewGuid();
        var grandparentRunId = Guid.NewGuid();

        var (resolver, runStore, subAgentRegistry, provider) = BuildBasicDeps(agentId, parentRunId);

        // BuildRequestAsync calls GetAsync(parentRunId) to inherit RootRunId
        runStore.Setup(x => x.GetAsync(parentRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRun { Id = parentRunId, ParentRunId = grandparentRunId, RootRunId = grandparentRunId });

        // depth walk: parentRunId → grandparentRunId → null
        runStore.Setup(x => x.GetParentRunIdAsync(parentRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(grandparentRunId);
        runStore.Setup(x => x.GetParentRunIdAsync(grandparentRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);
        runStore.Setup(x => x.CountDescendantsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Create an accessor with a currentRunId so parentRunId is used
        var executionContext = new AgentExecutionContextAccessor();
        executionContext.Properties[ContextPropertyKeys.CurrentRunId] = parentRunId;

        var runtimeInvoked = false;
        var services2 = new ServiceCollection();
        services2.AddLogging();
        var runtime = new Mock<IAgentRuntime>();
        runtime.Setup(x => x.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .Callback(() => runtimeInvoked = true)
            .ReturnsAsync(new AgentRunResult { Response = "should not be reached" });
        services2.AddScoped<IAgentRuntime>(_ => runtime.Object);
        var provider2 = services2.BuildServiceProvider();

        var service = CreateService(
            resolver.Object, runStore.Object, subAgentRegistry.Object, executionContext,
            provider2.GetRequiredService<IServiceScopeFactory>(), provider2,
            DefaultOptions(o => { o.MaxDepth = 2; o.MaxDescendantsPerRoot = 100; }));

        var result = await service.SpawnAsync(new SpawnAgentRunInput
        {
            AgentId = agentId,
            Message = "nested too deep"
        });

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldContain("depth");
        runtimeInvoked.ShouldBeFalse();
        // CreateAsync must not have been called
        runStore.Verify(x => x.CreateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SpawnAsync_ExceedsMaxDescendants_ReturnsFailure()
    {
        var agentId = Guid.NewGuid();
        var rootRunId = Guid.NewGuid();
        var parentRunId = Guid.NewGuid();

        var (resolver, runStore, subAgentRegistry, provider) = BuildBasicDeps(agentId);

        // BuildRequestAsync calls GetAsync(parentRunId) to inherit the true RootRunId
        runStore.Setup(x => x.GetAsync(parentRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRun { Id = parentRunId, ParentRunId = rootRunId, RootRunId = rootRunId });

        // Parent is a direct child of root (depth 2), well within MaxDepth
        runStore.Setup(x => x.GetParentRunIdAsync(parentRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rootRunId);
        runStore.Setup(x => x.GetParentRunIdAsync(rootRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        // Descendant count already at limit.
        // After FIX 3, prepared.RootRunId == rootRunId (inherited from parent),
        // so CountDescendantsAsync is called with the true rootRunId.
        runStore.Setup(x => x.CountDescendantsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(5); // MaxDescendantsPerRoot = 5 → already at cap

        var executionContext = new AgentExecutionContextAccessor();
        executionContext.Properties[ContextPropertyKeys.CurrentRunId] = parentRunId;

        var runtimeInvoked = false;
        var services2 = new ServiceCollection();
        services2.AddLogging();
        var runtime = new Mock<IAgentRuntime>();
        runtime.Setup(x => x.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .Callback(() => runtimeInvoked = true)
            .ReturnsAsync(new AgentRunResult { Response = "should not be reached" });
        services2.AddScoped<IAgentRuntime>(_ => runtime.Object);
        var provider2 = services2.BuildServiceProvider();

        var service = CreateService(
            resolver.Object, runStore.Object, subAgentRegistry.Object, executionContext,
            provider2.GetRequiredService<IServiceScopeFactory>(), provider2,
            DefaultOptions(o =>
            {
                o.MaxDepth = 10;
                o.MaxDescendantsPerRoot = 5;
            }));

        var result = await service.SpawnAsync(new SpawnAgentRunInput
        {
            AgentId = agentId,
            Message = "too many descendants"
        });

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldContain("descendants");
        runtimeInvoked.ShouldBeFalse();
        runStore.Verify(x => x.CreateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ------------------------------------------------------------------
    // FIX 3: RootRunId propagation - 3-level chain shares one root
    // ------------------------------------------------------------------

    /// <summary>
    /// A → spawns B (B.RootRunId = A.Id) → B spawns C
    /// C.RootRunId MUST be A.Id (the true tree root), not B.Id.
    /// This proves MaxDescendantsPerRoot actually bounds the whole tree.
    /// </summary>
    [Fact]
    public async Task SpawnAsync_ThreeLevelChain_ChildInheritsGrandparentAsRoot()
    {
        var agentId = Guid.NewGuid();
        var rootRunId = Guid.NewGuid();    // A - the true root
        var parentRunId = Guid.NewGuid();  // B - the immediate parent
        Guid? capturedRootRunId = null;

        var resolver = new Mock<IAgentResolver>();
        resolver.Setup(x => x.ResolveAgentAsync(
                agentId,
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentResolution.Success(Mock.Of<IAgentExecutor>(), "openai", "gpt-5.4", agentId, null, AgentExecutionMode.Single));

        var runStore = new Mock<IRunStore>();
        runStore.Setup(x => x.CreateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRun run, CancellationToken _) =>
            {
                run.Id = Guid.NewGuid();
                capturedRootRunId = run.RootRunId; // capture what was assigned
                return run;
            });

        // B's stored record: ParentRunId = A (rootRunId), RootRunId = A (rootRunId)
        runStore.Setup(x => x.GetAsync(parentRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRun { Id = parentRunId, ParentRunId = rootRunId, RootRunId = rootRunId });

        runStore.Setup(x => x.GetParentRunIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null); // depth check: parentRunId has no parent in mock (irrelevant for this test)
        runStore.Setup(x => x.CountDescendantsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var subAgentRegistry = new Mock<ISubAgentRegistry>();

        var runtimeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new Mock<IAgentRuntime>();
        runtime.Setup(x => x.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .Returns<AgentRunRequest, CancellationToken>((_, _) =>
            {
                runtimeStarted.TrySetResult();
                return Task.FromResult(new AgentRunResult { Response = "ok" });
            });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IAgentRuntime>(_ => runtime.Object);
        var provider = services.BuildServiceProvider();

        // Context: current run is B (parentRunId)
        var executionContext = new AgentExecutionContextAccessor();
        executionContext.Properties[ContextPropertyKeys.CurrentRunId] = parentRunId;

        var service = CreateService(
            resolver.Object, runStore.Object, subAgentRegistry.Object, executionContext,
            provider.GetRequiredService<IServiceScopeFactory>(), provider);

        // Spawn C from B's context
        var result = await service.SpawnAsync(new SpawnAgentRunInput
        {
            AgentId = agentId,
            Message = "third-level task"
        });

        result.Succeeded.ShouldBeTrue();

        // C's RootRunId must be A (rootRunId), NOT B (parentRunId)
        capturedRootRunId.ShouldBe(rootRunId,
            "C must inherit A's rootRunId so MaxDescendantsPerRoot bounds the whole tree");
    }

    // ------------------------------------------------------------------
    // B12: CTS lifecycle - register on spawn, dispose on finish
    // ------------------------------------------------------------------

    [Fact]
    public async Task SpawnAsync_RegistersAndRemovesCts()
    {
        var agentId = Guid.NewGuid();
        var createdRunId = Guid.NewGuid();
        var runtimeStarted = new SemaphoreSlim(0, 1);
        var runtimeReleased = new SemaphoreSlim(0, 1);

        var resolver = new Mock<IAgentResolver>();
        resolver.Setup(x => x.ResolveAgentAsync(
                agentId,
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentResolution.Success(Mock.Of<IAgentExecutor>(), "openai", "gpt-5.4", agentId, null, AgentExecutionMode.Single));

        var runStore = new Mock<IRunStore>();
        runStore.Setup(x => x.CreateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRun run, CancellationToken _) =>
            {
                run.Id = createdRunId;
                return run;
            });
        runStore.Setup(x => x.CountDescendantsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        runStore.Setup(x => x.GetParentRunIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var registry = new SubAgentRunCancellationRegistry();

        var runtime = new Mock<IAgentRuntime>();
        runtime.Setup(x => x.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .Returns<AgentRunRequest, CancellationToken>(async (_, ct) =>
            {
                runtimeStarted.Release();            // signal: runtime is executing
                await runtimeReleased.WaitAsync(ct); // wait until released or cancelled
                return new AgentRunResult { Response = "ok", Status = AgentRunStatus.Completed };
            });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IAgentRuntime>(_ => runtime.Object);
        var provider = services.BuildServiceProvider();

        var service = new SubAgentExecutionService(
            resolver.Object,
            runStore.Object,
            Mock.Of<ISubAgentRegistry>(),
            new AgentExecutionContextAccessor(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SubAgentExecutionService>.Instance,
            provider,
            DefaultOptions(),
            registry);

        var result = await service.SpawnAsync(new SpawnAgentRunInput
        {
            AgentId = agentId,
            Message = "test cts lifecycle"
        });

        result.Succeeded.ShouldBeTrue();

        // Wait until the background task is inside RunAsync
        await runtimeStarted.WaitAsync(TimeSpan.FromSeconds(5));

        // CTS must be registered while the task is running
        registry.TryCancel(createdRunId).ShouldBeTrue("CTS should be registered while background task is running");

        // Now the background task will observe cancellation and exit the finally block
        // The registry Unregister in the finally will remove it
        await Task.Delay(200); // allow finally block to run

        // CTS should now be unregistered (Unregister called in finally)
        registry.TryCancel(createdRunId).ShouldBeFalse("CTS should be removed after task finishes");
    }

    [Fact]
    public async Task KillAsync_CancelsRunningBackgroundTask()
    {
        // Arrange: a background task that blocks on a semaphore and observes CancellationToken
        var agentId = Guid.NewGuid();
        var createdRunId = Guid.NewGuid();
        var runtimeStarted = new SemaphoreSlim(0, 1);
        var taskCancelledTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var resolver = new Mock<IAgentResolver>();
        resolver.Setup(x => x.ResolveAgentAsync(
                agentId, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentResolution.Success(Mock.Of<IAgentExecutor>(), "openai", "gpt-5.4", agentId, null, AgentExecutionMode.Single));

        var runStore = new Mock<IRunStore>();
        runStore.Setup(x => x.CreateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRun run, CancellationToken _) =>
            {
                run.Id = createdRunId;
                return run;
            });
        runStore.Setup(x => x.CountDescendantsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        runStore.Setup(x => x.GetParentRunIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var registry = new SubAgentRunCancellationRegistry();

        var runtime = new Mock<IAgentRuntime>();
        runtime.Setup(x => x.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .Returns<AgentRunRequest, CancellationToken>(async (_, ct) =>
            {
                runtimeStarted.Release(); // signal: runtime is executing
                try
                {
                    await Task.Delay(Timeout.Infinite, ct); // block until cancelled
                }
                catch (OperationCanceledException)
                {
                    taskCancelledTcs.TrySetResult(true);
                    throw;
                }
                return new AgentRunResult { Response = "unreachable" };
            });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IAgentRuntime>(_ => runtime.Object);
        var provider = services.BuildServiceProvider();

        var service = new SubAgentExecutionService(
            resolver.Object,
            runStore.Object,
            Mock.Of<ISubAgentRegistry>(),
            new AgentExecutionContextAccessor(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SubAgentExecutionService>.Instance,
            provider,
            DefaultOptions(),
            registry);

        await service.SpawnAsync(new SpawnAgentRunInput
        {
            AgentId = agentId,
            Message = "long running task"
        });

        // Wait until the background task is inside RunAsync
        await runtimeStarted.WaitAsync(TimeSpan.FromSeconds(5));

        // Act: trip the CTS via the registry (simulates what CancelAsync does)
        var cancelled = registry.TryCancel(createdRunId);
        cancelled.ShouldBeTrue("TryCancel should find and trip the registered CTS");

        // Assert: the background task observed the cancellation
        var taskObservedCancellation = await taskCancelledTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        taskObservedCancellation.ShouldBeTrue("Background task should have observed cancellation via the CTS");
    }
}

/// <summary>
/// Simple IOptionsMonitor wrapper that always returns the same value.
/// </summary>
file sealed class ConstantOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
