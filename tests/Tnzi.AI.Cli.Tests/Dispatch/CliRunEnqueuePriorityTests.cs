namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 队列优先级是运维面的调度旋钮，不是用户可以自填的输入。
/// </summary>
/// <remarks>
/// 认领对所有租户共用一个队列（大者先）。此前用户端入队把请求体里的 <c>Priority</c> 原样落库：
/// 任何持有 <c>ai.agent.execute</c> 的用户带上 <c>int.MaxValue</c>，他的运行就永远排在所有租户、所有人之前。
/// 每条「被忽略」都配一条「被采纳」的对照，否则把字段整体删掉也一样绿。
/// </remarks>
public class CliRunEnqueuePriorityTests : IntegratedTestBase<CliThreadOwnershipDbContext>
{
    private static readonly Guid AgentId = Guid.Parse("11111111-1111-1111-1111-1111111111b1");
    private static readonly Guid RuntimeId = Guid.Parse("22222222-2222-2222-2222-2222222222b2");

    // 字段初始化先于基类构造函数执行，ConfigureServices 里就能用到它们。
    private readonly Mock<IPermissionChecker> _permissionChecker = new();
    private readonly Mock<ICurrentUser> _currentUser = new();

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<CliRun, Guid>, EFCoreRepository<CliThreadOwnershipDbContext, CliRun, Guid>>();
        services.AddScoped<IRepository<CliRunMessage, Guid>, EFCoreRepository<CliThreadOwnershipDbContext, CliRunMessage, Guid>>();
        services.AddScoped<IRepository<CliAgentBinding, Guid>, EFCoreRepository<CliThreadOwnershipDbContext, CliAgentBinding, Guid>>();
        services.AddScoped<IRepository<CliRuntime, Guid>, EFCoreRepository<CliThreadOwnershipDbContext, CliRuntime, Guid>>();
        services.AddScoped<IRepository<AgentThread, Guid>, EFCoreRepository<CliThreadOwnershipDbContext, AgentThread, Guid>>();

        _permissionChecker.Setup(p => p.IsGrantedAsync(It.IsAny<string>())).ReturnsAsync(false);
        services.AddScoped(_ => _permissionChecker.Object);

        SignIn(TestHelper.DefaultTestUserId);
        services.AddScoped(_ => _currentUser.Object);
    }

    [Fact]
    public async Task ASignedInUser_CannotRaiseTheQueuePriority()
    {
        await SeedBindingAsync();

        var run = await EnqueueAsync(priority: int.MaxValue);

        run.Priority.ShouldBe(0);
    }

    [Fact]
    public async Task ACallerWhoControlsCliRuns_SetsThePriority()
    {
        await SeedBindingAsync();
        _permissionChecker.Setup(p => p.IsGrantedAsync(CliPermissions.CliRunExecute)).ReturnsAsync(true);

        var run = await EnqueueAsync(priority: 7);

        run.Priority.ShouldBe(7);
    }

    [Fact]
    public async Task AnInProcessCallerWithoutASignedInUser_SetsThePriority()
    {
        // 后台任务、父 agent 派子任务这类进程内调用方没有登录用户，它们的优先级是代码写的，不是请求体
        await SeedBindingAsync();
        SignIn(userId: null);

        var run = await EnqueueAsync(priority: 3);

        run.Priority.ShouldBe(3);
    }

    // AgentRunId 是「这次外部执行属于哪条框架 AgentRun」的关联键，由门面等进程内调用方填写；
    // 用户端请求体里的值同理不可信：任意填一个就能把自己的运行挂到别人的 AgentRun 名下。

    [Fact]
    public async Task ASignedInUser_CannotLinkTheRunToAnAgentRun()
    {
        await SeedBindingAsync();

        var run = await EnqueueAsync(priority: 0, agentRunId: Guid.NewGuid());

        run.AgentRunId.ShouldBeNull();
    }

    [Fact]
    public async Task AnInProcessCallerWithoutASignedInUser_LinksTheAgentRun()
    {
        await SeedBindingAsync();
        SignIn(userId: null);
        var agentRunId = Guid.NewGuid();

        var run = await EnqueueAsync(priority: 0, agentRunId: agentRunId);

        run.AgentRunId.ShouldBe(agentRunId);
    }

    [Fact]
    public async Task ACallerWhoControlsCliRuns_LinksTheAgentRun()
    {
        await SeedBindingAsync();
        _permissionChecker.Setup(p => p.IsGrantedAsync(CliPermissions.CliRunExecute)).ReturnsAsync(true);
        var agentRunId = Guid.NewGuid();

        var run = await EnqueueAsync(priority: 0, agentRunId: agentRunId);

        run.AgentRunId.ShouldBe(agentRunId);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private void SignIn(Guid? userId)
    {
        _currentUser.Setup(m => m.Id).Returns(userId);
        _currentUser.Setup(m => m.IsAuthenticated).Returns(userId.HasValue);
    }

    private async Task<CliRun> EnqueueAsync(int priority, Guid? agentRunId = null)
    {
        var dispatcher = new CliAgentDispatcher(
            ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliRunMessage, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliAgentBinding, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliRuntime, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<AgentThread, Guid>>(),
            TestAgents.Repository(AgentId),
            new CliRunSignalHub(),
            new CliRunCancellationRegistry(),
            EnabledOptions(),
            ServiceProvider);

        var result = await dispatcher.EnqueueAsync(new CliRunRequestDto { AgentId = AgentId, Prompt = "x", Priority = priority, AgentRunId = agentRunId });
        result.Succeeded.ShouldBeTrue(result.Message);

        return await DbContext.Set<CliRun>().AsNoTracking().SingleAsync(r => r.Id == result.Data);
    }

    private static IOptionsMonitor<CliAgentOptions> EnabledOptions()
    {
        var monitor = new Mock<IOptionsMonitor<CliAgentOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(new CliAgentOptions { Enabled = true });
        return monitor.Object;
    }

    private async Task SeedBindingAsync()
    {
        DbContext.Set<CliAgentBinding>().Add(new CliAgentBinding
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            WorkDirectoryMode = CliWorkDirectoryMode.PerThread
        });
        DbContext.Set<CliRuntime>().Add(new CliRuntime
        {
            Id = RuntimeId,
            HostId = "TEST-HOST",
            ProviderKey = "claude",
            Name = "claude @ TEST-HOST",
            ExecutablePath = "/usr/bin/claude",
            Status = CliRuntimeStatus.Online
        });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }
}
