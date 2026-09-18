namespace Tnzi.AI.Tests;

/// <summary>
/// <c>task</c> 工具组背后的 <see cref="IAgentRuntimeControlService"/> 必须按调用方归属收口：
/// 默认（<see cref="AgentRunAccessScope.Caller"/>）只看得见、只动得了自己起的运行；
/// 管理端显式传 <see cref="AgentRunAccessScope.Tenant"/> 才是整租户范围。
/// </summary>
/// <remarks>
/// 此前 list / get / wait / kill / send_input 全走 <c>RunStore</c> 的租户级查询，没有任何调用方维度：
/// 拿到 task 组的用户能列出并读到别人 spawn 的输入/输出摘要、取消别人的运行、向别人暂停中的运行送输入。
/// 别人的运行一律 404 而不是 403 —— 不泄露存在性。归属键是 <c>AgentRun.CreatorId</c>；
/// 调用方身份取当前运行请求的 <c>UserId</c>（后台子 Agent 的作用域里没有当前用户），无则环境用户；
/// 都没有 = 未知调用方，只看得见同样无主的运行（失败关闭）。
/// </remarks>
public class AgentRunOwnershipScopingTests
{
    private readonly Guid _caller = Guid.NewGuid();
    private readonly Guid _someoneElse = Guid.NewGuid();

    [Fact]
    public async Task ListRuns_CallerScope_QueriesOnlyTheCallersRuns()
    {
        var runStore = new Mock<IRunStore>();
        runStore.Setup(x => x.ListByOwnerAsync(_caller, null, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new AgentRun { Id = Guid.NewGuid(), CreatorId = _caller, InputSummary = "mine" }]);

        var service = CreateService(runStore.Object, callerUserId: _caller);

        var result = await service.ListRunsAsync();

        result.Succeeded.ShouldBeTrue();
        result.Data!.Select(r => r.InputSummary).ShouldBe(["mine"]);
        runStore.Verify(x => x.ListAsync(It.IsAny<AgentRunStatus?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListRuns_TenantScope_QueriesTheWholeTenant()
    {
        var runStore = new Mock<IRunStore>();
        runStore.Setup(x => x.ListAsync(null, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new AgentRun { Id = Guid.NewGuid(), CreatorId = _someoneElse, InputSummary = "theirs" }]);

        var service = CreateService(runStore.Object, callerUserId: _caller);

        var result = await service.ListRunsAsync(scope: AgentRunAccessScope.Tenant);

        result.Data!.Select(r => r.InputSummary).ShouldBe(["theirs"]);
        runStore.Verify(x => x.ListByOwnerAsync(It.IsAny<Guid?>(), It.IsAny<AgentRunStatus?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetState_OtherUsersRun_Returns404()
    {
        var runId = Guid.NewGuid();
        var runStore = RunStoreWith(new AgentRun { Id = runId, CreatorId = _someoneElse, Status = AgentRunStatus.Running, InputSummary = "secret prompt" });
        var service = CreateService(runStore.Object, callerUserId: _caller);

        var result = await service.GetStateAsync(runId);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
        result.ErrorCode.ShouldBe(ErrorCodes.RunNotFound);
    }

    [Fact]
    public async Task GetState_OwnRun_Succeeds()
    {
        var runId = Guid.NewGuid();
        var runStore = RunStoreWith(new AgentRun { Id = runId, CreatorId = _caller, Status = AgentRunStatus.Running });
        var service = CreateService(runStore.Object, callerUserId: _caller);

        var result = await service.GetStateAsync(runId);

        result.Succeeded.ShouldBeTrue();
        result.Data!.RunId.ShouldBe(runId);
    }

    [Fact]
    public async Task GetState_TenantScope_OtherUsersRun_Succeeds()
    {
        var runId = Guid.NewGuid();
        var runStore = RunStoreWith(new AgentRun { Id = runId, CreatorId = _someoneElse, Status = AgentRunStatus.Running });
        var service = CreateService(runStore.Object, callerUserId: _caller);

        var result = await service.GetStateAsync(runId, AgentRunAccessScope.Tenant);

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task Kill_OtherUsersRun_Returns404AndNeverReachesTheDispatcher()
    {
        var runId = Guid.NewGuid();
        var runStore = RunStoreWith(new AgentRun { Id = runId, CreatorId = _someoneElse, Status = AgentRunStatus.Running });
        var dispatcher = new Mock<IAgentRunSignalDispatcher>();
        var service = CreateService(runStore.Object, callerUserId: _caller, dispatcher: dispatcher.Object);

        var result = await service.KillAsync(runId);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
        dispatcher.Verify(x => x.CancelAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Kill_OwnRun_ReachesTheDispatcher()
    {
        var runId = Guid.NewGuid();
        var runStore = RunStoreWith(new AgentRun { Id = runId, CreatorId = _caller, Status = AgentRunStatus.Running });
        var dispatcher = new Mock<IAgentRunSignalDispatcher>();
        dispatcher.Setup(x => x.CancelAsync(runId, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
        var service = CreateService(runStore.Object, callerUserId: _caller, dispatcher: dispatcher.Object);

        var result = await service.KillAsync(runId);

        result.Succeeded.ShouldBeTrue();
        dispatcher.Verify(x => x.CancelAsync(runId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendInput_OtherUsersRun_Returns404AndNeverReachesTheDispatcher()
    {
        var runId = Guid.NewGuid();
        var runStore = RunStoreWith(new AgentRun { Id = runId, CreatorId = _someoneElse, Status = AgentRunStatus.RequiresClarification });
        var dispatcher = new Mock<IAgentRunSignalDispatcher>();
        var service = CreateService(runStore.Object, callerUserId: _caller, dispatcher: dispatcher.Object);

        var result = await service.SendInputAsync(runId, new SendAgentRunInput { Message = "continue" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
        dispatcher.Verify(x => x.DispatchInputAsync(It.IsAny<Guid>(), It.IsAny<SendAgentRunInput>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Wait_OtherUsersRun_Returns404()
    {
        var runId = Guid.NewGuid();
        var runStore = RunStoreWith(new AgentRun { Id = runId, CreatorId = _someoneElse, Status = AgentRunStatus.Completed });
        var service = CreateService(runStore.Object, callerUserId: _caller);

        var result = await service.WaitAsync(runId, new WaitAgentRunInput { TimeoutSeconds = 1 });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task UnknownCaller_OwnedRun_Returns404()
    {
        // 不知道调用方是谁 ≠ 就是那个人：无身份的调用只看得见同样无主的运行
        var runId = Guid.NewGuid();
        var runStore = RunStoreWith(new AgentRun { Id = runId, CreatorId = _someoneElse, Status = AgentRunStatus.Running });
        var service = CreateService(runStore.Object, callerUserId: null);

        var result = await service.GetStateAsync(runId);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task UnknownCaller_UnownedRun_IsVisible()
    {
        var runId = Guid.NewGuid();
        var runStore = RunStoreWith(new AgentRun { Id = runId, CreatorId = null, Status = AgentRunStatus.Running });
        var service = CreateService(runStore.Object, callerUserId: null);

        var result = await service.GetStateAsync(runId);

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task CallerIdentity_ComesFromTheCurrentRunRequest_NotOnlyTheAmbientUser()
    {
        // 后台子 Agent 在新作用域里跑：环境里没有当前用户，身份只能来自正在执行的请求
        var runId = Guid.NewGuid();
        var runStore = RunStoreWith(new AgentRun { Id = runId, CreatorId = _caller, Status = AgentRunStatus.Running });
        var accessor = new AgentExecutionContextAccessor
        {
            CurrentRequest = new AgentRunRequest { UserMessage = "parent", UserId = _caller }
        };
        var service = CreateService(runStore.Object, callerUserId: null, accessor: accessor);

        var result = await service.GetStateAsync(runId);

        result.Succeeded.ShouldBeTrue();
    }

    // ---------------------------------------------------------------------

    private static Mock<IRunStore> RunStoreWith(AgentRun run)
    {
        var runStore = new Mock<IRunStore>();
        runStore.Setup(x => x.GetWithNodesAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        runStore.Setup(x => x.GetAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        return runStore;
    }

    private static AgentRuntimeControlService CreateService(
        IRunStore runStore,
        Guid? callerUserId,
        IAgentRunSignalDispatcher? dispatcher = null,
        IAgentExecutionContextAccessor? accessor = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (callerUserId.HasValue)
        {
            var user = new Mock<ICurrentUser>();
            user.Setup(u => u.Id).Returns(callerUserId);
            user.Setup(u => u.IsAuthenticated).Returns(true);
            services.AddSingleton(user.Object);
        }

        return new AgentRuntimeControlService(
            Mock.Of<ISubAgentExecutionService>(),
            dispatcher ?? Mock.Of<IAgentRunSignalDispatcher>(),
            runStore,
            Mock.Of<IWorkflowService>(),
            Mock.Of<ISubAgentRegistry>(),
            services.BuildServiceProvider(),
            executionContextAccessor: accessor ?? new AgentExecutionContextAccessor());
    }
}
