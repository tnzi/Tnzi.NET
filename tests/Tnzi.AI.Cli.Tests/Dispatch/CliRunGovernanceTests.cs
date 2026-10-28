using System.Runtime.CompilerServices;

namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 外部执行域与内建路径共用的三道治理：Agent 停用开关、按用户的用量上限、按模型逐行记账的成本。
/// </summary>
/// <remarks>
/// 外部执行按红线①不进中间件管线，而内建路径上这三件事分别长在 <c>AgentResolver</c>（停用）、
/// <c>QuotaMiddleware</c>（配额）与逐次调用的用量记录里。外部路径若不显式补上，就是一条
/// 绕过它们的通道，而它恰恰是两条路里更贵的那条。用例跑真实执行器与 SQLite 仓储，
/// 只有进程与协议层是假的。
/// </remarks>
public class CliRunGovernanceTests : IntegratedTestBase<CliBudgetDbContext>
{
    private static readonly Guid AgentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RuntimeId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(20);

    private readonly string _workspaces = TestScratch.NewPath("governance");

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<CliRun, Guid>, EFCoreRepository<CliBudgetDbContext, CliRun, Guid>>();
        services.AddScoped<IRepository<CliRunMessage, Guid>, EFCoreRepository<CliBudgetDbContext, CliRunMessage, Guid>>();
        services.AddScoped<IRepository<CliAgentBinding, Guid>, EFCoreRepository<CliBudgetDbContext, CliAgentBinding, Guid>>();
        services.AddScoped<IRepository<CliRuntime, Guid>, EFCoreRepository<CliBudgetDbContext, CliRuntime, Guid>>();
    }

    public override void Dispose()
    {
        TestScratch.Delete(_workspaces);

        base.Dispose();
    }

    // ── Agent 停用开关 ────────────────────────────────────────────────────────

    [Fact]
    public async Task Enqueue_DisabledAgent_IsRejected_AndNothingIsQueued()
    {
        await SeedBindingAsync();
        var dispatcher = CreateDispatcher(TestAgents.Repository(AgentId, isEnabled: false));

        var result = await dispatcher.EnqueueAsync(new CliRunRequestDto { AgentId = AgentId, Prompt = "x" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.ErrorCode.ShouldBe(ErrorCodes.AgentDisabled);
        (await DbContext.Set<CliRun>().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Enqueue_AgentThatNoLongerExists_IsRejected_AndNothingIsQueued()
    {
        // 绑定比 Agent 活得久（Agent 被删了、绑定行还在）：入队就该当场说不，而不是排进队列再失败
        await SeedBindingAsync();
        var dispatcher = CreateDispatcher(new Mock<IRepository<Agent, Guid>>().Object);

        var result = await dispatcher.EnqueueAsync(new CliRunRequestDto { AgentId = AgentId, Prompt = "x" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
        result.ErrorCode.ShouldBe(ErrorCodes.AgentNotFound);
        (await DbContext.Set<CliRun>().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Enqueue_EnabledAgent_IsQueued()
    {
        await SeedBindingAsync();
        var dispatcher = CreateDispatcher(TestAgents.Repository(AgentId));

        var result = await dispatcher.EnqueueAsync(new CliRunRequestDto { AgentId = AgentId, Prompt = "x" });

        result.Succeeded.ShouldBeTrue(result.Message);
        (await DbContext.Set<CliRun>().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Execute_AgentDisabledWhileQueued_FailsTheRun_AndNeverStartsAProcess()
    {
        // 停用开关要拦住已经排进队列的运行：队列可能积压很久，管理员按下停用正是为了让它别再跑
        var run = await SeedClaimedRunAsync();
        var fixture = new ExecutorFixture(this) { Agents = TestAgents.Repository(AgentId, isEnabled: false) };

        await fixture.Build().ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var persisted = await ReloadAsync(run.Id);
        persisted.Status.ShouldBe(CliRunStatus.Failed);
        persisted.Error.ShouldBe("The agent is disabled.");
        persisted.StartedAt.ShouldBeNull();
        fixture.ProcessStarts.ShouldBe(0);
    }

    [Fact]
    public async Task Execute_RuntimeDisabledWhileQueued_FailsTheRun_AndNeverStartsAProcess()
    {
        // 入队时只拦了新入队的；管理员停用运行时正是要已经排着的那些也别再跑
        var run = await SeedClaimedRunAsync(CliRuntimeStatus.Disabled);
        var fixture = new ExecutorFixture(this);

        await fixture.Build().ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var persisted = await ReloadAsync(run.Id);
        persisted.Status.ShouldBe(CliRunStatus.Failed);
        persisted.Error.ShouldBe("The bound external CLI runtime has been disabled by an administrator.");
        persisted.StartedAt.ShouldBeNull();
        fixture.ProcessStarts.ShouldBe(0);
    }

    // ── 工作目录被别的运行占着 ──────────────────────────────────────────────────

    /// <summary>
    /// 占用者已经结束：标记是它硬崩溃时留下的。用户目录模式下那个目录是同一 Agent 所有线程共用的，
    /// 此前别的线程的每一次运行都以 Unknown 失败，直到有人手工删掉标记文件。
    /// </summary>
    [Fact]
    public async Task Execute_WorkspaceLeftClaimedByAFinishedRun_ReclaimsItAndRuns()
    {
        var run = await SeedClaimedRunAsync();
        var crashed = await SeedOtherRunAsync(CliRunStatus.Failed);
        var fixture = new ExecutorFixture(this) { ClaimedBy = crashed };

        await fixture.Build().ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        (await ReloadAsync(run.Id)).Status.ShouldBe(CliRunStatus.Completed);
        fixture.ProcessStarts.ShouldBe(1);
    }

    /// <summary>占用者还在执行：两个 agent 同时写一个目录会互相破坏工作成果，照样拒绝，但按工作区失败归类。</summary>
    [Fact]
    public async Task Execute_WorkspaceClaimedByARunInProgress_FailsAsAWorkspaceFailure()
    {
        var run = await SeedClaimedRunAsync();
        var busy = await SeedOtherRunAsync(CliRunStatus.Running);
        var fixture = new ExecutorFixture(this) { ClaimedBy = busy };

        await fixture.Build().ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var persisted = await ReloadAsync(run.Id);
        persisted.Status.ShouldBe(CliRunStatus.Failed);
        persisted.FailureReason.ShouldBe(CliRunFailureReason.WorkspacePrepareFailed);
        persisted.Error!.ShouldContain(busy.ToString());
        fixture.ProcessStarts.ShouldBe(0);
    }

    /// <summary>标记归属一个本库不认识的运行：分不清它是不是还活着，失败关闭。</summary>
    [Fact]
    public async Task Execute_WorkspaceClaimedByAnUnknownRun_IsNotReclaimed()
    {
        var run = await SeedClaimedRunAsync();
        var fixture = new ExecutorFixture(this) { ClaimedBy = Guid.NewGuid() };

        await fixture.Build().ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        (await ReloadAsync(run.Id)).FailureReason.ShouldBe(CliRunFailureReason.WorkspacePrepareFailed);
        fixture.ProcessStarts.ShouldBe(0);
    }

    [Fact]
    public async Task Execute_RuntimeOnline_StartsTheProcess()
    {
        var run = await SeedClaimedRunAsync(CliRuntimeStatus.Online);
        var fixture = new ExecutorFixture(this);

        await fixture.Build().ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        (await ReloadAsync(run.Id)).Status.ShouldBe(CliRunStatus.Completed);
        fixture.ProcessStarts.ShouldBe(1);
    }

    [Fact]
    public async Task Execute_ExecutableMissing_NamesTheHostThatRanIt_NotOnlyTheRegisteringHost()
    {
        // 认领不看运行时登记在哪台机器上：报登记宿主会把排查引到一台本来就装着 CLI 的机器上。
        // 报的是这次运行的执行目标（本机宿主就是机器名，这里是测试宿主）
        var run = await SeedClaimedRunAsync();
        var fixture = new ExecutorFixture(this) { ResolvedExecutable = null };

        await fixture.Build().ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var persisted = await ReloadAsync(run.Id);
        persisted.Status.ShouldBe(CliRunStatus.Failed);
        persisted.FailureReason.ShouldBe(CliRunFailureReason.ExecutableNotFound);
        persisted.Error.ShouldBe(
            "Executable for provider 'claude' was not found on host 'fake-remote'. " +
            "The bound runtime was registered on host 'TEST-HOST'.");
        fixture.ProcessStarts.ShouldBe(0);
    }

    // ── 准备阶段别处写下的事实不被整实体回写盖掉 ────────────────────────────────

    [Fact]
    public async Task Execute_CancelRequestedOnAnotherReplicaDuringPreparation_IsHonoured()
    {
        // 运行在认领时加载；取消请求落在另一个副本上，只改了库里的 CancelRequested。
        // 进入 Running 若是整实体回写，就把它改回 false：续租再也看不见取消，运行照常跑完并计费
        var run = await SeedClaimedRunAsync();
        var fixture = new ExecutorFixture(this)
        {
            OnPrepare = () => DbContext.Set<CliRun>().IgnoreQueryFilters()
                .Where(r => r.Id == run.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.CancelRequested, true))
        };

        await fixture.Build().ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var persisted = await ReloadAsync(run.Id);
        persisted.Status.ShouldBe(CliRunStatus.Cancelled);
        persisted.CancelRequested.ShouldBeTrue();
        fixture.ProcessStarts.ShouldBe(0);
    }

    [Fact]
    public async Task Execute_LeaseRenewedDuringPreparation_IsNotRolledBackWhenTheRunStarts()
    {
        // 准备阶段比租约长时续租写下了新租约；进入 Running 若写回认领时的旧值，回收器会把正在跑的运行打回队列
        var run = await SeedClaimedRunAsync();
        var renewed = DateTime.UtcNow.AddHours(1);
        DateTime? leaseAtStart = null;
        var fixture = new ExecutorFixture(this)
        {
            OnPrepare = () => DbContext.Set<CliRun>().IgnoreQueryFilters()
                .Where(r => r.Id == run.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.LeaseExpiresAt, renewed)),
            OnProcessStart = async () => leaseAtStart = (await ReloadAsync(run.Id)).LeaseExpiresAt
        };

        await fixture.Build().ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        fixture.ProcessStarts.ShouldBe(1);
        leaseAtStart.ShouldNotBeNull();
        leaseAtStart!.Value.ShouldBe(renewed, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Execute_ManyEvents_PersistsEveryOne_WithoutKeepingThemTrackedInTheRunsContext()
    {
        // 执行器的作用域活过整条运行：事件行若留在它的上下文里被跟踪，每次保存都要扫一遍此前的全部事件，
        // 一条运行累计 O(n²)、内存线性增长（ACP 每个流式分块就是一条事件）
        var run = await SeedClaimedRunAsync();
        var fixture = new ExecutorFixture(this) { EventCount = 200 };

        await fixture.Build().ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        (await ReloadAsync(run.Id)).Status.ShouldBe(CliRunStatus.Completed);
        DbContext.ChangeTracker.Entries<CliRunMessage>().Count().ShouldBe(0);
        var sequences = await DbContext.Set<CliRunMessage>().AsNoTracking().IgnoreQueryFilters()
            .Where(m => m.RunId == run.Id)
            .Select(m => m.Sequence)
            .OrderBy(s => s)
            .ToListAsync();
        sequences.Count.ShouldBe(200);
        sequences.ShouldBe(Enumerable.Range(sequences[0], 200));
    }

    // ── 成本按模型逐行记账 ────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_MultiModelTurn_EachUsageRowCarriesItsOwnReportedCost_RowsSumToTheRunTotal()
    {
        var run = await SeedClaimedRunAsync();
        var usageLog = new CapturingUsageLog();
        var fixture = new ExecutorFixture(this)
        {
            UsageLog = usageLog,
            Usage = new Dictionary<string, CliAgentTokenUsage>
            {
                ["claude-opus"] = new() { InputTokens = 1000, OutputTokens = 500, ReportedCostUsd = 0.40m },
                ["claude-haiku"] = new() { InputTokens = 100, OutputTokens = 20, ReportedCostUsd = 0.01m }
            }
        };

        await fixture.Build().ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var persisted = await ReloadAsync(run.Id);
        persisted.EstimatedCostUsd.ShouldBe(0.41m);

        usageLog.Rows.Count.ShouldBe(2);
        usageLog.Rows.Single(r => r.Model == "claude-opus").Cost.ShouldBe(0.40m);
        usageLog.Rows.Single(r => r.Model == "claude-haiku").Cost.ShouldBe(0.01m);
        usageLog.Rows.Sum(r => r.Cost ?? 0m).ShouldBe(persisted.EstimatedCostUsd!.Value);
    }

    [Fact]
    public async Task Execute_MultiModelTurn_WithoutReportedCost_EachRowIsPricedForItsOwnModel()
    {
        var run = await SeedClaimedRunAsync();
        var usageLog = new CapturingUsageLog();
        var calculator = new Mock<ICostCalculator>();
        calculator.Setup(c => c.CalculateCost("claude", "claude-opus", 1000L, 500L, 0L, 0L)).Returns(0.30m);
        calculator.Setup(c => c.CalculateCost("claude", "claude-haiku", 100L, 20L, 0L, 0L)).Returns(0.02m);
        var fixture = new ExecutorFixture(this)
        {
            UsageLog = usageLog,
            CostCalculator = calculator.Object,
            Usage = new Dictionary<string, CliAgentTokenUsage>
            {
                ["claude-opus"] = new() { InputTokens = 1000, OutputTokens = 500 },
                ["claude-haiku"] = new() { InputTokens = 100, OutputTokens = 20 }
            }
        };

        await fixture.Build().ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var persisted = await ReloadAsync(run.Id);
        persisted.EstimatedCostUsd.ShouldBe(0.32m);
        usageLog.Rows.Single(r => r.Model == "claude-opus").Cost.ShouldBe(0.30m);
        usageLog.Rows.Single(r => r.Model == "claude-haiku").Cost.ShouldBe(0.02m);
    }

    // ── 按用户的用量上限 ───────────────────────────────────────────────────────

    [Fact]
    public async Task Enqueue_WhenUsageLimitIsReached_IsRejectedWith429_CarryingTheWindow_AndNothingIsQueued()
    {
        await SeedBindingAsync();
        var resetsAt = new DateTime(2026, 10, 21, 15, 0, 0, DateTimeKind.Utc);
        var limits = new Mock<IUsageLimitService>();
        limits.Setup(l => l.CheckAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UsageLimitDenialDto { Window = UsageLimitWindow.Session, ResetsAt = resetsAt });
        var dispatcher = CreateDispatcher(TestAgents.Repository(AgentId), limits.Object);

        var result = await dispatcher.EnqueueAsync(new CliRunRequestDto { AgentId = AgentId, Prompt = "x" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(429);
        result.ErrorCode.ShouldBe(ErrorCodes.UsageLimitReached);
        var denial = result.ErrorDetails.ShouldBeOfType<UsageLimitDenialDto>();
        denial.Window.ShouldBe(UsageLimitWindow.Session);
        denial.ResetsAt.ShouldBe(resetsAt);
        (await DbContext.Set<CliRun>().CountAsync()).ShouldBe(0);
        limits.Verify(l => l.CheckAsync(TestHelper.DefaultTestUserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Enqueue_WhenUsageLimitAllows_IsQueued()
    {
        // 请求里的模型怎么采纳见 CliRunEnqueueModelTests
        await SeedBindingAsync();
        var limits = new Mock<IUsageLimitService>();
        var dispatcher = CreateDispatcher(TestAgents.Repository(AgentId), limits.Object);

        var result = await dispatcher.EnqueueAsync(new CliRunRequestDto { AgentId = AgentId, Prompt = "x" });

        result.Succeeded.ShouldBeTrue(result.Message);
        (await DbContext.Set<CliRun>().SingleAsync()).Status.ShouldBe(CliRunStatus.Queued);
    }

    [Fact]
    public async Task Execute_WhenTheLimitWasReachedWhileQueued_FailsAsUserUsageLimit_WithTheResetTime_AndNeverStartsAProcess()
    {
        var run = await SeedClaimedRunAsync();
        var resetsAt = new DateTime(2026, 10, 23, 10, 0, 0, DateTimeKind.Utc);
        var limits = UsageLimitBeginning(Result<UsageLimitReservation>.Failure(
            "You have reached your weekly usage limit.", 429, ErrorCodes.UsageLimitReached,
            new UsageLimitDenialDto { Window = UsageLimitWindow.Weekly, ResetsAt = resetsAt }));
        var fixture = new ExecutorFixture(this) { UsageLimits = limits.Object };

        await fixture.Build().ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var persisted = await ReloadAsync(run.Id);
        persisted.Status.ShouldBe(CliRunStatus.Failed);
        persisted.FailureReason.ShouldBe(CliRunFailureReason.UserUsageLimit);
        persisted.UsageLimitResetsAt.ShouldBe(resetsAt);
        persisted.Error.ShouldBe("You have reached your weekly usage limit.");
        persisted.StartedAt.ShouldBeNull();
        fixture.ProcessStarts.ShouldBe(0);
        limits.Verify(l => l.BeginAsync(TestHelper.DefaultTestUserId, It.IsAny<CancellationToken>()), Times.Once);
        limits.Verify(l => l.SettleAsync(It.IsAny<UsageLimitReservation>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Execute_WhenTheMeterItselfFails_FailsClosed_ButIsNotLabelledAUsageLimit()
    {
        // 计量子系统失败不是「到上限了」：照样不跑（失败关闭），但不能贴成上限把排查引到上限配置上去
        var run = await SeedClaimedRunAsync();
        var limits = UsageLimitBeginning(Result<UsageLimitReservation>.Failure(
            "The usage limit could not be checked.", 500, ErrorCodes.UsageLimitCheckFailed));
        var fixture = new ExecutorFixture(this) { UsageLimits = limits.Object };

        await fixture.Build().ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        var persisted = await ReloadAsync(run.Id);
        persisted.Status.ShouldBe(CliRunStatus.Failed);
        persisted.FailureReason.ShouldBe(CliRunFailureReason.Unknown);
        persisted.UsageLimitResetsAt.ShouldBeNull();
        fixture.ProcessStarts.ShouldBe(0);
    }

    [Fact]
    public async Task Execute_Completed_SettlesOnce_WithTheTurnsCostSummedOverModels()
    {
        var run = await SeedClaimedRunAsync();
        var reservation = new UsageLimitReservation(TestHelper.DefaultTestUserId, DateTime.UtcNow);
        var limits = UsageLimitBeginning(Result<UsageLimitReservation>.Success(reservation));
        var fixture = new ExecutorFixture(this)
        {
            UsageLimits = limits.Object,
            Usage = new Dictionary<string, CliAgentTokenUsage>
            {
                ["claude-opus"] = new() { InputTokens = 100, OutputTokens = 50, ReportedCostUsd = 0.40m },
                ["claude-haiku"] = new() { InputTokens = 10, OutputTokens = 5, ReportedCostUsd = 0.01m }
            }
        };

        await fixture.Build().ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        (await ReloadAsync(run.Id)).Status.ShouldBe(CliRunStatus.Completed);
        fixture.ProcessStarts.ShouldBe(1);
        limits.Verify(l => l.SettleAsync(reservation, 0.41m, It.IsAny<CancellationToken>()), Times.Once);
        limits.Verify(l => l.SettleAsync(It.IsAny<UsageLimitReservation>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_FailingBeforeTheProcessStarts_SettlesNothingSpent()
    {
        // 开窗口之后、开进程之前失败（这里是 Agent 已停用）：一分钱都没花，结算 0
        var run = await SeedClaimedRunAsync();
        var reservation = new UsageLimitReservation(TestHelper.DefaultTestUserId, DateTime.UtcNow);
        var limits = UsageLimitBeginning(Result<UsageLimitReservation>.Success(reservation));
        var fixture = new ExecutorFixture(this)
        {
            UsageLimits = limits.Object,
            Agents = TestAgents.Repository(AgentId, isEnabled: false)
        };

        await fixture.Build().ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        limits.Verify(l => l.SettleAsync(reservation, 0m, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_ProcessRanButReportedNoUsage_SettlesZero()
    {
        // 没有任何用量可据以估一个金额：宁可这一轮少记，也不凭空记一笔
        var run = await SeedClaimedRunAsync();
        var reservation = new UsageLimitReservation(TestHelper.DefaultTestUserId, DateTime.UtcNow);
        var limits = UsageLimitBeginning(Result<UsageLimitReservation>.Success(reservation));
        var fixture = new ExecutorFixture(this) { UsageLimits = limits.Object };

        await fixture.Build().ExecuteAsync(run.Id, CancellationToken.None).WaitAsync(TestTimeout);

        fixture.ProcessStarts.ShouldBe(1);
        limits.Verify(l => l.SettleAsync(reservation, 0m, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static Mock<IUsageLimitService> UsageLimitBeginning(Result<UsageLimitReservation> begun)
    {
        var limits = new Mock<IUsageLimitService>();
        limits.Setup(l => l.BeginAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(begun);
        limits.Setup(l => l.SettleAsync(It.IsAny<UsageLimitReservation>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        return limits;
    }

    private async Task SeedBindingAsync()
    {
        DbContext.Set<CliAgentBinding>().Add(new CliAgentBinding { AgentId = AgentId, CliRuntimeId = RuntimeId });
        DbContext.Set<CliRuntime>().Add(new CliRuntime
        {
            Id = RuntimeId,
            HostId = "TEST-HOST",
            ProviderKey = "claude",
            Name = "claude @ TEST-HOST",
            ExecutablePath = "/nonexistent/claude",
            Status = CliRuntimeStatus.Online
        });
        await DbContext.SaveChangesAsync();
    }

    /// <summary>同一 Agent 另一条线程上的一次运行，处于给定状态。</summary>
    private async Task<Guid> SeedOtherRunAsync(CliRunStatus status)
    {
        var other = new CliRun
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            ThreadId = Guid.NewGuid(),
            Status = status,
            Prompt = "another thread"
        };
        DbContext.Set<CliRun>().Add(other);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return other.Id;
    }

    private async Task<CliRun> SeedClaimedRunAsync(CliRuntimeStatus runtimeStatus = CliRuntimeStatus.Online)
    {
        DbContext.Set<CliAgentBinding>().Add(new CliAgentBinding
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            MaterializeSkills = false,
            InjectAgentInstructions = false
        });
        DbContext.Set<CliRuntime>().Add(new CliRuntime
        {
            Id = RuntimeId,
            HostId = "TEST-HOST",
            ProviderKey = "claude",
            Name = "claude @ TEST-HOST",
            ExecutablePath = "/nonexistent/claude",
            Status = runtimeStatus
        });

        var run = new CliRun
        {
            AgentId = AgentId,
            CliRuntimeId = RuntimeId,
            Status = CliRunStatus.Dispatched,
            ClaimedByHostId = "TEST-HOST",
            DispatchedAt = DateTime.UtcNow,
            LeaseExpiresAt = DateTime.UtcNow.AddMinutes(5),
            Prompt = "do the thing"
        };
        DbContext.Set<CliRun>().Add(run);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return run;
    }

    private async Task<CliRun> ReloadAsync(Guid runId)
        => await DbContext.Set<CliRun>().AsNoTracking().IgnoreQueryFilters().FirstAsync(r => r.Id == runId);

    private CliAgentDispatcher CreateDispatcher(IRepository<Agent, Guid> agents, IUsageLimitService? usageLimits = null)
        => new(
            ServiceProvider.GetRequiredService<IRepository<CliRun, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliRunMessage, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliAgentBinding, Guid>>(),
            ServiceProvider.GetRequiredService<IRepository<CliRuntime, Guid>>(),
            Mock.Of<IRepository<AgentThread, Guid>>(),
            agents,
            new CliRunSignalHub(),
            new CliRunCancellationRegistry(),
            EnabledOptions(),
            ServiceProvider,
            usageLimits: usageLimits);

    private static IOptionsMonitor<CliAgentOptions> EnabledOptions()
    {
        var monitor = new Mock<IOptionsMonitor<CliAgentOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(new CliAgentOptions { Enabled = true });
        return monitor.Object;
    }

    /// <summary>执行器夹具：除进程与协议层外全是真的；可替换 Agent 仓储、用量上限、成本与用量记录。</summary>
    private sealed class ExecutorFixture(CliRunGovernanceTests owner)
    {
        public IRepository<Agent, Guid> Agents { get; init; } = TestAgents.Repository(AgentId);

        public IUsageLimitService? UsageLimits { get; init; }

        public ICostCalculator? CostCalculator { get; init; }

        public IUsageLogService? UsageLog { get; init; }

        public IReadOnlyDictionary<string, CliAgentTokenUsage> Usage { get; init; }
            = new Dictionary<string, CliAgentTokenUsage>();

        public string? ResolvedExecutable { get; init; } = "/nonexistent/claude";

        public int EventCount { get; init; } = 1;

        /// <summary>Runs while the workspace is being prepared, i.e. after the claim and before the run is marked Running.</summary>
        public Func<Task>? OnPrepare { get; init; }

        /// <summary>Runs when the process starts, i.e. after the run is marked Running.</summary>
        public Func<Task>? OnProcessStart { get; init; }

        /// <summary>
        /// The work directory carries an identity marker of this run (another thread's): preparing refuses
        /// unless the context marks exactly that run as reclaimable.
        /// </summary>
        public Guid? ClaimedBy { get; init; }

        public int ProcessStarts { get; private set; }

        public CliRunExecutor Build()
        {
            var services = owner.ServiceProvider;
            var runs = services.GetRequiredService<IRepository<CliRun, Guid>>();

            var descriptor = new CliProviderDescriptor
            {
                Key = "claude",
                DisplayName = "Claude Code",
                Protocol = CliAgentProtocol.StreamJson,
                DefaultExecutable = "claude"
            };

            var registry = new Mock<ICliProviderRegistry>();
            registry.Setup(r => r.Find("claude")).Returns(descriptor);

            var adapter = new CompletingAdapter(Usage, EventCount);
            var adapters = new Mock<ICliProtocolAdapterFactory>();
            adapters.Setup(f => f.IsImplemented(CliAgentProtocol.StreamJson)).Returns(true);
            adapters.Setup(f => f.Create(CliAgentProtocol.StreamJson)).Returns(adapter);

            var host = new TestCliHost(async (_, _) =>
            {
                ProcessStarts++;
                if (OnProcessStart is not null)
                {
                    await OnProcessStart();
                }

                return new ExitedProcess();
            }, ResolvedExecutable);

            var workspaceRoot = Path.Combine(owner._workspaces, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workspaceRoot);
            var workspace = new CliWorkspace
            {
                RootDirectory = workspaceRoot,
                WorkDirectory = workspaceRoot,
                OutputDirectory = workspaceRoot,
                LogDirectory = workspaceRoot
            };
            var preparer = new Mock<ICliWorkspacePreparer>();
            preparer
                .Setup(p => p.PrepareAsync(It.IsAny<ICliHostTarget>(), It.IsAny<CliRunContext>(), It.IsAny<CancellationToken>()))
                .Returns<ICliHostTarget, CliRunContext, CancellationToken>(async (_, context, _) =>
                {
                    if (ClaimedBy is { } owner && context.ReclaimableClaimRunId != owner)
                    {
                        throw new CliWorkspaceClaimedException("/repos/shared", owner);
                    }

                    if (OnPrepare is not null)
                    {
                        await OnPrepare();
                    }

                    return workspace;
                });

            return new CliRunExecutor(
                runs,
                services.GetRequiredService<IRepository<CliRunMessage, Guid>>(),
                services.GetRequiredService<IRepository<CliAgentBinding, Guid>>(),
                services.GetRequiredService<IRepository<CliRuntime, Guid>>(),
                Agents,
                registry.Object,
                adapters.Object,
                host,
                preparer.Object,
                Mock.Of<ICliBriefComposer>(),
                Mock.Of<ICliMcpConfigComposer>(),
                new CliRunTokenService(runs, NullLogger<CliRunTokenService>.Instance),
                new CliLaunchEnvironmentComposer(Mock.Of<ICliAuthStatusProbe>(), NullLogger<CliLaunchEnvironmentComposer>.Instance),
                Mock.Of<IAgentGrantService>(),
                Mock.Of<ISkillService>(),
                new CliRunSignalHub(),
                EnabledOptions(),
                services.GetRequiredService<ICurrentTenant>(),
                NullLogger<CliRunExecutor>.Instance,
                services.GetRequiredService<IServiceScopeFactory>(),
                costCalculator: CostCalculator,
                usageLogService: UsageLog,
                usageLimits: UsageLimits);
        }
    }

    /// <summary>发 <paramref name="eventCount"/> 条事件即结束，终态带上给定的按模型用量。</summary>
    private sealed class CompletingAdapter(IReadOnlyDictionary<string, CliAgentTokenUsage> usage, int eventCount = 1) : ICliProtocolAdapter
    {
        public CliAgentProtocol Protocol => CliAgentProtocol.StreamJson;

        public CliProcessSpec BuildProcess(CliAgentLaunchContext context)
            => new() { ExecutablePath = context.ExecutablePath, WorkingDirectory = context.WorkingDirectory };

        public async IAsyncEnumerable<CliAgentEvent> RunAsync(
            ICliAgentTransport transport,
            CliAgentLaunchContext context,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            for (var i = 0; i < eventCount; i++)
            {
                yield return new CliAgentEvent { Type = CliAgentEventType.Text, Content = "done", SessionId = "sess-1" };
            }
        }

        public CliAgentResult GetResult(CliSessionOutcome outcome) => new()
        {
            Status = CliRunStatus.Completed,
            Output = "done",
            SessionId = "sess-1",
            DurationMs = 1234,
            Usage = usage
        };
    }

    private sealed class ExitedProcess : ICliProcess
    {
        public ICliAgentTransport Transport { get; } = new FakeCliAgentTransport([]);

        public int ProcessId => 4242;

        public bool HasExited => true;

        public int? ExitCode => 0;

        public Task TerminateAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<int> WaitForExitAsync(CancellationToken cancellationToken) => Task.FromResult(0);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>记下每一行用量（带成本的那个重载）。</summary>
    private sealed class CapturingUsageLog : IUsageLogService
    {
        public List<(string Model, decimal? Cost, long DurationMs)> Rows { get; } = [];

        public Task LogUsageAsync(
            string operationType, string provider, string model, int inputTokens, int outputTokens,
            long durationMs, bool isSuccess, string? errorMessage = null, Guid? agentId = null,
            Guid? threadId = null, CancellationToken ct = default)
        {
            Rows.Add((model, null, durationMs));
            return Task.CompletedTask;
        }

        public Task LogUsageAsync(
            string operationType, string provider, string model, int inputTokens, int outputTokens,
            long durationMs, bool isSuccess, string? errorMessage, Guid? agentId, Guid? threadId,
            decimal? estimatedCostUsd, CancellationToken ct = default)
        {
            Rows.Add((model, estimatedCostUsd, durationMs));
            return Task.CompletedTask;
        }
    }
}

/// <summary>按 Id 返回一个 Agent 的仓储替身（调度器与执行器都按 <c>GetAsync</c> 读它）。</summary>
internal static class TestAgents
{
    public static IRepository<Agent, Guid> Repository(Guid agentId, bool isEnabled = true)
    {
        var agents = new Mock<IRepository<Agent, Guid>>();
        agents
            .Setup(r => r.GetAsync(agentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Agent { Id = agentId, Name = "agent", IsEnabled = isEnabled });
        return agents.Object;
    }
}
