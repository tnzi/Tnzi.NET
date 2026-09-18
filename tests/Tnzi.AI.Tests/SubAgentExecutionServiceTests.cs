
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
    public async Task SpawnAsync_FromInsideAParentRun_RecordsTheParentRequestUserAsOwnerAndRuntimeUser()
    {
        // spawn_agent 工具不传 UserId；父运行的作用域里 CurrentRequest.UserId 就是发起人。
        // 归属写进 AgentRun.CreatorId（审计钩子只在为 null 时才用环境用户填），
        // 也写进后台请求的 UserId（配额计量、权限检查、权限规则评估全按它认人）。
        var owner = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        AgentRun? createdRun = null;
        var runtimeCalled = new TaskCompletionSource<AgentRunRequest>(TaskCreationOptions.RunContinuationsAsynchronously);

        var resolver = ResolverFor(agentId);
        var runStore = RunStoreCapturing(run => createdRun = run);
        var executionContext = new AgentExecutionContextAccessor
        {
            CurrentRequest = new AgentRunRequest { UserMessage = "parent", UserId = owner }
        };
        var provider = ProviderWithRuntime(runtimeCalled);

        var service = CreateService(
            resolver.Object, runStore.Object, Mock.Of<ISubAgentRegistry>(), executionContext,
            provider.GetRequiredService<IServiceScopeFactory>(), provider);

        var result = await service.SpawnAsync(new SpawnAgentRunInput { AgentId = agentId, Message = "child" });

        result.Succeeded.ShouldBeTrue();
        createdRun.ShouldNotBeNull();
        createdRun!.CreatorId.ShouldBe(owner);
        var runtimeRequest = await runtimeCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        runtimeRequest.UserId.ShouldBe(owner);
    }

    [Fact]
    public async Task SpawnAsync_FromAnHttpScope_RecordsTheCurrentUserAsOwner()
    {
        // 管理端 spawn 端点：没有正在执行的请求，归属取环境用户
        var admin = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        AgentRun? createdRun = null;
        var runtimeCalled = new TaskCompletionSource<AgentRunRequest>(TaskCreationOptions.RunContinuationsAsynchronously);

        var resolver = ResolverFor(agentId);
        var runStore = RunStoreCapturing(run => createdRun = run);
        var provider = ProviderWithRuntime(runtimeCalled, currentUserId: admin);

        var service = CreateService(
            resolver.Object, runStore.Object, Mock.Of<ISubAgentRegistry>(), new AgentExecutionContextAccessor(),
            provider.GetRequiredService<IServiceScopeFactory>(), provider);

        var result = await service.SpawnAsync(new SpawnAgentRunInput { AgentId = agentId, Message = "child" });

        result.Succeeded.ShouldBeTrue();
        createdRun!.CreatorId.ShouldBe(admin);
        (await runtimeCalled.Task.WaitAsync(TimeSpan.FromSeconds(5))).UserId.ShouldBe(admin);
    }

    [Fact]
    public async Task SpawnAsync_ExplicitInputUserId_WinsOverContext()
    {
        var explicitUser = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        AgentRun? createdRun = null;
        var runtimeCalled = new TaskCompletionSource<AgentRunRequest>(TaskCreationOptions.RunContinuationsAsynchronously);

        var resolver = ResolverFor(agentId);
        var runStore = RunStoreCapturing(run => createdRun = run);
        var provider = ProviderWithRuntime(runtimeCalled, currentUserId: Guid.NewGuid());

        var service = CreateService(
            resolver.Object, runStore.Object, Mock.Of<ISubAgentRegistry>(), new AgentExecutionContextAccessor(),
            provider.GetRequiredService<IServiceScopeFactory>(), provider);

        await service.SpawnAsync(new SpawnAgentRunInput { AgentId = agentId, Message = "child", UserId = explicitUser });

        createdRun!.CreatorId.ShouldBe(explicitUser);
    }

    [Fact]
    public async Task SpawnAsync_RestoresTheCallerTenantInsideTheBackgroundScope()
    {
        // 后台运行在新作用域、新执行流里跑；调用方的租户来自 HttpContext 上的 claim，
        // 父请求一结束 holder 就被置空，新作用域里的 ICurrentTenant 读到的是 null。
        // SpawnAsync 必须在调用方作用域快照租户，并在后台作用域里 Change() 回去，
        // 否则多租户下子运行写的每一行都落成 TenantId=null。
        var tenantId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var requestUser = new RequestBoundUser(Guid.NewGuid(), tenantId);
        var requestEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observedTenant = new TaskCompletionSource<Guid?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var runtime = new Mock<IAgentRuntime>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentUser>(requestUser);
        services.AddScoped<ICurrentTenant, CurrentTenant>();
        services.AddScoped<IAgentRuntime>(sp =>
        {
            var tenant = sp.GetRequiredService<ICurrentTenant>();
            runtime.Setup(x => x.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
                .Returns<AgentRunRequest, CancellationToken>(async (request, _) =>
                {
                    await requestEnded.Task;
                    observedTenant.TrySetResult(tenant.Id);
                    return new AgentRunResult { Response = "ok", RunId = request.ExistingRunId, Status = AgentRunStatus.Completed };
                });
            return runtime.Object;
        });
        var provider = services.BuildServiceProvider();

        var service = CreateService(
            ResolverFor(agentId).Object, RunStoreCapturing(_ => { }).Object, Mock.Of<ISubAgentRegistry>(),
            new AgentExecutionContextAccessor(), provider.GetRequiredService<IServiceScopeFactory>(), provider);

        var result = await service.SpawnAsync(new SpawnAgentRunInput { AgentId = agentId, Message = "child" });
        result.Succeeded.ShouldBeTrue();

        // 模拟父 HTTP 请求结束：HttpContext 派生的用户不再有租户
        requestUser.EndRequest();
        requestEnded.SetResult();

        (await observedTenant.Task.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBe(tenantId);
    }

    /// <summary>模拟 HttpContextCurrentUser：请求结束后所有 claim 读空。</summary>
    private sealed class RequestBoundUser : ICurrentUser
    {
        private readonly Guid _id;
        private readonly Guid _tenantId;
        private volatile bool _ended;

        public RequestBoundUser(Guid id, Guid tenantId)
        {
            _id = id;
            _tenantId = tenantId;
        }

        public void EndRequest() => _ended = true;

        public Guid? Id => _ended ? null : _id;
        public Guid? TenantId => _ended ? null : _tenantId;
        public bool IsAuthenticated => !_ended;
        public string? UserName => null;
        public string[] Roles => [];
        public bool IsInRole(string roleName) => false;
    }

    private static Mock<IAgentResolver> ResolverFor(Guid agentId)
    {
        var resolver = new Mock<IAgentResolver>();
        resolver.Setup(x => x.ResolveAgentAsync(
                agentId, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgentResolution.Success(Mock.Of<IAgentExecutor>(), "openai", "gpt-5.4", agentId, null, AgentExecutionMode.Single));
        return resolver;
    }

    private static Mock<IRunStore> RunStoreCapturing(Action<AgentRun> onCreate)
    {
        var runStore = new Mock<IRunStore>();
        runStore.Setup(x => x.CreateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRun run, CancellationToken _) =>
            {
                run.Id = Guid.NewGuid();
                onCreate(run);
                return run;
            });
        runStore.Setup(x => x.CountDescendantsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        runStore.Setup(x => x.GetParentRunIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);
        return runStore;
    }

    private static ServiceProvider ProviderWithRuntime(TaskCompletionSource<AgentRunRequest> runtimeCalled, Guid? currentUserId = null)
    {
        var runtime = new Mock<IAgentRuntime>();
        runtime.Setup(x => x.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .Returns<AgentRunRequest, CancellationToken>((request, _) =>
            {
                runtimeCalled.TrySetResult(request);
                return Task.FromResult(new AgentRunResult { Response = "ok", RunId = request.ExistingRunId, Status = AgentRunStatus.Completed });
            });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IAgentRuntime>(_ => runtime.Object);
        if (currentUserId.HasValue)
        {
            var user = new Mock<ICurrentUser>();
            user.Setup(u => u.Id).Returns(currentUserId);
            user.Setup(u => u.IsAuthenticated).Returns(true);
            services.AddSingleton(user.Object);
        }
        return services.BuildServiceProvider();
    }

    // ------------------------------------------------------------------
    // SubAgentOptions：Enabled / MaxConcurrentSubAgents / TimeoutSeconds 的运行时读者
    // ------------------------------------------------------------------

    [Fact]
    public async Task SpawnAsync_WhenDisabled_Returns403AndCreatesNoRun()
    {
        // 管理端把 Enabled 关掉 → 保存 200；此前 spawn_agent 照常起后台运行，没有任何症状
        var agentId = Guid.NewGuid();
        var created = false;
        var runtimeCalled = new TaskCompletionSource<AgentRunRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolver = ResolverFor(agentId);
        var runStore = RunStoreCapturing(_ => created = true);
        var provider = ProviderWithRuntime(runtimeCalled);

        var service = CreateService(
            resolver.Object, runStore.Object, Mock.Of<ISubAgentRegistry>(), new AgentExecutionContextAccessor(),
            provider.GetRequiredService<IServiceScopeFactory>(), provider,
            DefaultOptions(o => o.Enabled = false));

        var result = await service.SpawnAsync(new SpawnAgentRunInput { AgentId = agentId, Message = "child" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        result.ErrorCode.ShouldBe(ErrorCodes.SubAgentsDisabled);
        created.ShouldBeFalse();
        runtimeCalled.Task.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task SpawnAsync_ActiveDescendantsAtMaxConcurrent_Returns429()
    {
        var agentId = Guid.NewGuid();
        var rootRunId = Guid.NewGuid();
        var parentRunId = Guid.NewGuid();
        var created = false;
        var runtimeCalled = new TaskCompletionSource<AgentRunRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolver = ResolverFor(agentId);
        var runStore = RunStoreCapturing(_ => created = true);
        runStore.Setup(x => x.GetAsync(parentRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRun { Id = parentRunId, ParentRunId = rootRunId, RootRunId = rootRunId });
        runStore.Setup(x => x.GetParentRunIdAsync(parentRunId, It.IsAny<CancellationToken>())).ReturnsAsync(rootRunId);
        runStore.Setup(x => x.GetParentRunIdAsync(rootRunId, It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);
        // 树里已有 3 个还在跑的后代（总数 3 < MaxDescendantsPerRoot 25，只有并发上限会拦）
        runStore.Setup(x => x.CountActiveDescendantsAsync(rootRunId, It.IsAny<CancellationToken>())).ReturnsAsync(3);
        var executionContext = new AgentExecutionContextAccessor();
        executionContext.Properties[ContextPropertyKeys.CurrentRunId] = parentRunId;
        var provider = ProviderWithRuntime(runtimeCalled);

        var service = CreateService(
            resolver.Object, runStore.Object, Mock.Of<ISubAgentRegistry>(), executionContext,
            provider.GetRequiredService<IServiceScopeFactory>(), provider,
            DefaultOptions(o => o.MaxConcurrentSubAgents = 3));

        var result = await service.SpawnAsync(new SpawnAgentRunInput { AgentId = agentId, Message = "child" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(429);
        result.ErrorCode.ShouldBe(ErrorCodes.SubAgentLimitExceeded);
        created.ShouldBeFalse();
    }

    [Fact]
    public async Task SpawnAsync_ActiveDescendantsBelowMaxConcurrent_Spawns()
    {
        var agentId = Guid.NewGuid();
        var rootRunId = Guid.NewGuid();
        var parentRunId = Guid.NewGuid();
        var runtimeCalled = new TaskCompletionSource<AgentRunRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolver = ResolverFor(agentId);
        var runStore = RunStoreCapturing(_ => { });
        runStore.Setup(x => x.GetAsync(parentRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRun { Id = parentRunId, ParentRunId = rootRunId, RootRunId = rootRunId });
        runStore.Setup(x => x.GetParentRunIdAsync(parentRunId, It.IsAny<CancellationToken>())).ReturnsAsync(rootRunId);
        runStore.Setup(x => x.GetParentRunIdAsync(rootRunId, It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);
        runStore.Setup(x => x.CountActiveDescendantsAsync(rootRunId, It.IsAny<CancellationToken>())).ReturnsAsync(2);
        var executionContext = new AgentExecutionContextAccessor();
        executionContext.Properties[ContextPropertyKeys.CurrentRunId] = parentRunId;
        var provider = ProviderWithRuntime(runtimeCalled);

        var service = CreateService(
            resolver.Object, runStore.Object, Mock.Of<ISubAgentRegistry>(), executionContext,
            provider.GetRequiredService<IServiceScopeFactory>(), provider,
            DefaultOptions(o => o.MaxConcurrentSubAgents = 3));

        var result = await service.SpawnAsync(new SpawnAgentRunInput { AgentId = agentId, Message = "child" });

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task SpawnAsync_TopLevelSpawn_OwnerAtMaxConcurrent_Returns429()
    {
        // 没有父运行的 spawn（管理端端点、未开运行追踪的聊天里的 spawn_agent）是一棵新树的根：
        // 树内计数恒为 0，并发上限此前在这条路径上一次都不会触发。按归属人计数。
        var owner = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var created = false;
        var runtimeCalled = new TaskCompletionSource<AgentRunRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolver = ResolverFor(agentId);
        var runStore = RunStoreCapturing(_ => created = true);
        runStore.Setup(x => x.CountActiveRootRunsByOwnerAsync(owner, It.IsAny<CancellationToken>())).ReturnsAsync(3);
        var provider = ProviderWithRuntime(runtimeCalled, currentUserId: owner);

        var service = CreateService(
            resolver.Object, runStore.Object, Mock.Of<ISubAgentRegistry>(), new AgentExecutionContextAccessor(),
            provider.GetRequiredService<IServiceScopeFactory>(), provider,
            DefaultOptions(o => o.MaxConcurrentSubAgents = 3));

        var result = await service.SpawnAsync(new SpawnAgentRunInput { AgentId = agentId, Message = "child" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(429);
        result.ErrorCode.ShouldBe(ErrorCodes.SubAgentLimitExceeded);
        created.ShouldBeFalse();
        runStore.Verify(x => x.CountActiveDescendantsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SpawnAsync_TopLevelSpawn_OwnerBelowMaxConcurrent_Spawns()
    {
        var owner = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var runtimeCalled = new TaskCompletionSource<AgentRunRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolver = ResolverFor(agentId);
        var runStore = RunStoreCapturing(_ => { });
        runStore.Setup(x => x.CountActiveRootRunsByOwnerAsync(owner, It.IsAny<CancellationToken>())).ReturnsAsync(2);
        var provider = ProviderWithRuntime(runtimeCalled, currentUserId: owner);

        var service = CreateService(
            resolver.Object, runStore.Object, Mock.Of<ISubAgentRegistry>(), new AgentExecutionContextAccessor(),
            provider.GetRequiredService<IServiceScopeFactory>(), provider,
            DefaultOptions(o => o.MaxConcurrentSubAgents = 3));

        var result = await service.SpawnAsync(new SpawnAgentRunInput { AgentId = agentId, Message = "child" });

        result.Succeeded.ShouldBeTrue();
        runStore.Verify(x => x.CountActiveRootRunsByOwnerAsync(owner, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SpawnAsync_TopLevelSpawn_UnknownOwner_CountsTheUnownedRoots()
    {
        // 认不出调用者时按「无主」计数，与 ListByOwnerAsync(null) 同口径；不是跳过上限
        var agentId = Guid.NewGuid();
        var created = false;
        var runtimeCalled = new TaskCompletionSource<AgentRunRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolver = ResolverFor(agentId);
        var runStore = RunStoreCapturing(_ => created = true);
        runStore.Setup(x => x.CountActiveRootRunsByOwnerAsync(null, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var provider = ProviderWithRuntime(runtimeCalled);

        var service = CreateService(
            resolver.Object, runStore.Object, Mock.Of<ISubAgentRegistry>(), new AgentExecutionContextAccessor(),
            provider.GetRequiredService<IServiceScopeFactory>(), provider,
            DefaultOptions(o => o.MaxConcurrentSubAgents = 1));

        var result = await service.SpawnAsync(new SpawnAgentRunInput { AgentId = agentId, Message = "child" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(429);
        created.ShouldBeFalse();
    }

    [Fact]
    public async Task SpawnAsync_MarksTheRuntimeRequestAsBackground()
    {
        // 后台运行的判据必须来自请求本身，且不能是 ParentRunId：根 spawn 没有父运行，
        // 却同样是后台运行（AsyncAgentAllowedTools 白名单、子 Agent 标记都要按它认）
        var agentId = Guid.NewGuid();
        var runtimeCalled = new TaskCompletionSource<AgentRunRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolver = ResolverFor(agentId);
        var runStore = RunStoreCapturing(_ => { });
        var provider = ProviderWithRuntime(runtimeCalled);

        var service = CreateService(
            resolver.Object, runStore.Object, Mock.Of<ISubAgentRegistry>(), new AgentExecutionContextAccessor(),
            provider.GetRequiredService<IServiceScopeFactory>(), provider);

        (await service.SpawnAsync(new SpawnAgentRunInput { AgentId = agentId, Message = "child" })).Succeeded.ShouldBeTrue();

        var request = await runtimeCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        request.IsBackground.ShouldBeTrue();
        request.ParentRunId.ShouldBeNull();
    }

    [Fact]
    public async Task SpawnAsync_TimeoutSeconds_CancelsTheBackgroundRun()
    {
        // 后台运行没有任何超时：一个失控的子 Agent 会一直跑到进程结束
        var agentId = Guid.NewGuid();
        var observedCancellation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolver = ResolverFor(agentId);
        var runStore = RunStoreCapturing(_ => { });

        var runtime = new Mock<IAgentRuntime>();
        runtime.Setup(x => x.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .Returns<AgentRunRequest, CancellationToken>(async (_, ct) =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                }
                catch (OperationCanceledException)
                {
                    observedCancellation.TrySetResult(true);
                    throw;
                }
                return new AgentRunResult { Response = "never" };
            });
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IAgentRuntime>(_ => runtime.Object);
        var provider = services.BuildServiceProvider();

        var service = CreateService(
            resolver.Object, runStore.Object, Mock.Of<ISubAgentRegistry>(), new AgentExecutionContextAccessor(),
            provider.GetRequiredService<IServiceScopeFactory>(), provider,
            DefaultOptions(o => o.TimeoutSeconds = 1));

        var result = await service.SpawnAsync(new SpawnAgentRunInput { AgentId = agentId, Message = "runaway" });

        result.Succeeded.ShouldBeTrue();
        (await observedCancellation.Task.WaitAsync(TimeSpan.FromSeconds(10))).ShouldBeTrue();
    }

    /// <summary>
    /// 超时经注册表触发而不是裸 CancelAfter：运行时收到 OperationCanceledException 时注册表里已经记着「超时」，
    /// 收尾才写得成 Failed("Timed out ...") 而不是与 kill 同款的 Cancelled。
    /// </summary>
    [Fact]
    public async Task SpawnAsync_TimeoutSeconds_IsRecordedAsTimedOutInTheRegistry()
    {
        var agentId = Guid.NewGuid();
        var registry = new SubAgentRunCancellationRegistry();
        var observed = new TaskCompletionSource<RunCancellation?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Guid? createdRunId = null;
        var resolver = ResolverFor(agentId);
        var runStore = RunStoreCapturing(run => createdRunId = run.Id);

        var runtime = new Mock<IAgentRuntime>();
        runtime.Setup(x => x.RunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .Returns<AgentRunRequest, CancellationToken>(async (request, ct) =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                }
                catch (OperationCanceledException)
                {
                    // 与 AgentRuntime 的收尾同一时机读原因：还在 finally 之前，注册表条目仍在
                    observed.TrySetResult(registry.GetCancellation(request.ExistingRunId!.Value));
                    throw;
                }
                return new AgentRunResult { Response = "never" };
            });
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IAgentRuntime>(_ => runtime.Object);
        var provider = services.BuildServiceProvider();

        var service = new SubAgentExecutionService(
            resolver.Object, runStore.Object, Mock.Of<ISubAgentRegistry>(), new AgentExecutionContextAccessor(),
            provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<SubAgentExecutionService>.Instance, provider,
            DefaultOptions(o => o.TimeoutSeconds = 1), registry);

        var result = await service.SpawnAsync(new SpawnAgentRunInput { AgentId = agentId, Message = "runaway" });
        result.Succeeded.ShouldBeTrue();

        var cancellation = await observed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.ShouldNotBeNull();
        cancellation.Kind.ShouldBe(RunCancellationKind.TimedOut);
        cancellation.Reason.ShouldContain("timed out", Case.Insensitive);
        createdRunId.ShouldNotBeNull();
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
        // 服务层按租户桶查（Moq 不执行默认接口成员，须直接 setup 带租户键的重载）
        subAgentRegistry.Setup(x => x.GetForTenant("researcher", It.IsAny<string>()))
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
        result.Message!.ShouldContain("depth");
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
        result.Message!.ShouldContain("descendants");
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
        registry.TryCancel(createdRunId, "test kill").ShouldBeTrue("CTS should be registered while background task is running");

        // Now the background task will observe cancellation and exit the finally block
        // The registry Unregister in the finally will remove it
        await Task.Delay(200); // allow finally block to run

        // CTS should now be unregistered (Unregister called in finally)
        registry.TryCancel(createdRunId, "test kill").ShouldBeFalse("CTS should be removed after task finishes");
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
        var cancelled = registry.TryCancel(createdRunId, "test kill");
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
