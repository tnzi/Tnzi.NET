using Tnzi.Exceptions;
using AgentThreadEntity = Tnzi.AI.Entities.AgentThread;

namespace Tnzi.AI.Tests;

/// <summary>
/// 非工作流运行的续跑：授权在改状态之前，且以运行归属人的身份跑。
/// </summary>
/// <remarks>
/// 2026-09-12 前 <c>AgentRuntime.ResumeAsync</c> 先把运行改成 Running 再进管线，续跑请求不带 UserId；
/// 管理员续跑别人的运行时 <c>HistoryMiddleware</c> 的线程归属校验用环境用户（管理员）比对线程创建者 → 404，
/// catch 块又把运行写成 Failed / Error="Thread not found"：一次误点就终结了用户侧的澄清流程。
/// </remarks>
public class AgentRuntimeResumeOwnershipTests
{
    private static (Mock<IRunStore> runStore, RunTracker tracker) TrackerFor(AgentRun run)
    {
        var runStore = new Mock<IRunStore>();
        runStore.Setup(x => x.GetWithNodesAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        runStore.Setup(x => x.UpdateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var traceStore = new Mock<ITraceStore>();
        traceStore.Setup(x => x.AddAsync(It.IsAny<AgentRunTrace>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentRunTrace trace, CancellationToken _) => trace);
        return (runStore, new RunTracker(runStore.Object, traceStore.Object, Mock.Of<ILogger<RunTracker>>()));
    }

    private static AgentRuntime RuntimeFor(RunTracker tracker, IAgentResolver resolver, IAgentThreadService? threadService, AgentExecutionContextAccessor? accessor = null)
        => new(
            resolver,
            Mock.Of<IAgentFactory>(),
            Mock.Of<IRepository<Agent, Guid>>(),
            tracker,
            Mock.Of<IWorkflowDelegator>(),
            accessor ?? new AgentExecutionContextAccessor(),
            new ServiceCollection().BuildServiceProvider(),
            new StaticOptionsMonitor<AIOptions>(new AIOptions()),
            Mock.Of<IEventPublisher>(),
            Mock.Of<ILogger<AgentRuntime>>(),
            threadService: threadService);

    [Fact]
    public async Task ResumeAsync_ThreadNotOwnedByRunOwner_Returns404_WithoutTouchingTheRun()
    {
        var owner = Guid.NewGuid();
        var run = new AgentRun { Id = Guid.NewGuid(), ThreadId = Guid.NewGuid(), CreatorId = owner, Status = AgentRunStatus.RequiresClarification, Nodes = [] };
        var (runStore, tracker) = TrackerFor(run);
        var threadService = new Mock<IAgentThreadService>();
        threadService.Setup(t => t.IsOwnerAsync(run.ThreadId!.Value, owner)).ReturnsAsync(false);

        var runtime = RuntimeFor(tracker, Mock.Of<IAgentResolver>(), threadService.Object);

        var ex = await Should.ThrowAsync<BusinessException>(() => runtime.ResumeAsync(run.Id, new ResumeRunInput { UserMessage = "more" }));

        ex.HttpStatusCode.ShouldBe(404);
        run.Status.ShouldBe(AgentRunStatus.RequiresClarification);
        run.Error.ShouldBeNull();
        runStore.Verify(x => x.UpdateAsync(It.IsAny<AgentRun>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResumeAsync_RunsAsTheRunOwner_NotAsTheResumer()
    {
        var owner = Guid.NewGuid();
        // AgentId 必须有：没有快照又没有 AgentId 的旧行（模板 spawn）自 2026-09-12 起拒绝续跑
        var run = new AgentRun { Id = Guid.NewGuid(), AgentId = Guid.NewGuid(), ThreadId = Guid.NewGuid(), CreatorId = owner, Status = AgentRunStatus.RequiresClarification, Nodes = [] };
        var (_, tracker) = TrackerFor(run);
        var threadService = new Mock<IAgentThreadService>();
        threadService.Setup(t => t.IsOwnerAsync(run.ThreadId!.Value, owner)).ReturnsAsync(true);

        var accessor = new AgentExecutionContextAccessor();
        Guid? userSeenByPipeline = null;
        var resolver = new Mock<IAgentResolver>();
        resolver.Setup(r => r.ResolveAgentAsync(It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<List<string>?>(), It.IsAny<CancellationToken>(), It.IsAny<List<string>?>()))
            .Callback(() => userSeenByPipeline = accessor.CurrentRequest?.UserId)
            .ReturnsAsync(AgentResolution.Failure("openai", null, null, ErrorCodes.AgentNotFound));

        var runtime = RuntimeFor(tracker, resolver.Object, threadService.Object, accessor);

        // 解析失败会让 RunAsync 抛 AgentNotFound；这里只关心管线看到的是谁
        await Should.ThrowAsync<BusinessException>(() => runtime.ResumeAsync(run.Id, new ResumeRunInput { UserMessage = "more" }));

        userSeenByPipeline.ShouldBe(owner);
    }

    [Fact]
    public async Task AgentThreadService_GetOrCreateThread_ChecksOwnershipAgainstTheRequestUser()
    {
        // 线程归属按当前运行请求的 UserId 比对（续跑以归属人身份跑），没有请求时才退回环境用户
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
        var owner = Guid.NewGuid();
        var admin = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var threadRepo = new Mock<IRepository<AgentThreadEntity, Guid>>();
        threadRepo.Setup(r => r.GetAsync(threadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentThreadEntity { Id = threadId, CreatorId = owner, Title = "t" });
        var messageRepo = new Mock<IRepository<AgentThreadMessage, Guid>>();
        var messages = new List<AgentThreadMessage>().BuildMock();
        messageRepo.As<IQueryable<AgentThreadMessage>>().Setup(q => q.Provider).Returns(messages.Provider);
        messageRepo.As<IQueryable<AgentThreadMessage>>().Setup(q => q.Expression).Returns(messages.Expression);
        messageRepo.As<IQueryable<AgentThreadMessage>>().Setup(q => q.ElementType).Returns(messages.ElementType);
        messageRepo.As<IQueryable<AgentThreadMessage>>().Setup(q => q.GetEnumerator()).Returns(messages.GetEnumerator());
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(u => u.Id).Returns(admin);
        var accessor = new AgentExecutionContextAccessor { CurrentRequest = new AgentRunRequest { UserMessage = "x", UserId = owner } };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(currentUser.Object);
        services.AddSingleton<IAgentExecutionContextAccessor>(accessor);
        var provider = services.BuildServiceProvider();

        var service = new AgentThreadService(threadRepo.Object, messageRepo.Object, Mock.Of<IRepository<Agent, Guid>>(), provider);

        var (_, resolved, _) = await service.GetOrCreateThreadAsync(threadId, null);
        resolved.ShouldBe(threadId);

        accessor.CurrentRequest = null;
        var ex = await Should.ThrowAsync<BusinessException>(() => service.GetOrCreateThreadAsync(threadId, null));
        ex.HttpStatusCode.ShouldBe(404);
    }
}
