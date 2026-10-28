namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 批准通道的序号纪律：结局事件与适配器的事件共用一个序号，重放还没追上停机前落库的事件时不能写出新结局。
/// </summary>
public class CliRunApprovalsTests : IntegratedTestBase<CliBudgetDbContext>
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IRepository<CliRunPermissionRequest, Guid>, EFCoreRepository<CliBudgetDbContext, CliRunPermissionRequest, Guid>>();
    }

    /// <summary>
    /// 服务端不在时用户答了一个请求；接回来重放到这个请求时，停机前它之后还落过别的事件。此刻写出结局，
    /// 结局就占了那些事件的序号（下一条重放出来的事件被当成「已落库」跳过，或撞上唯一索引）。必须等重放追上才写。
    /// </summary>
    [Fact]
    public async Task AnAnswerGivenWhileTheServerWasDown_IsNotWrittenUntilTheReplayHasCaughtUp()
    {
        var run = new CliRun { Id = Guid.NewGuid(), Status = CliRunStatus.Running, Prompt = "p" };
        var adapter = new Mock<ICliProtocolAdapter>();
        adapter.Setup(a => a.AnswerPermissionAsync(It.IsAny<string>(), It.IsAny<CliPermissionDecision>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var persisted = new List<(CliAgentEventType Type, int Slot)>();
        using var sequence = new CliEventSequence(0);
        using var watchdog = new CliRunWatchdog(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), null, CancellationToken.None);

        var approvals = new CliRunApprovals(
            run, adapter.Object, sequence, watchdog, ServiceProvider.GetRequiredService<IServiceScopeFactory>(), new CliRunSignalHub(),
            new CliApprovalOptions { Timeout = TimeSpan.FromMinutes(5), PollInterval = TimeSpan.FromMilliseconds(20) },
            persistedThrough: 3,
            (evt, slot) =>
            {
                persisted.Add((evt.Type, slot));
                return Task.CompletedTask;
            },
            NullLogger.Instance);

        // 重放到序号 1：请求（停机前落库的 1..3 里的第一条）
        sequence.Current = 1;
        var opened = await approvals.OpenAsync(new CliAgentEvent
        {
            Type = CliAgentEventType.PermissionRequest,
            Tool = "Bash",
            Permission = new CliAgentPermission
            {
                ProtocolKey = "req-1",
                Options = [new CliPermissionOption("allow_once", "Allow once", CliPermissionOptionKinds.AllowOnce)]
            }
        }, 1);
        await DbContext.Set<CliRunPermissionRequest>().IgnoreQueryFilters().Where(r => r.Id == opened.Permission!.RequestId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, CliPermissionStatus.Allowed)
                .SetProperty(r => r.OptionId, "allow_once"));

        await RunForAsync(approvals, TimeSpan.FromMilliseconds(300));
        persisted.ShouldBeEmpty("the replay is still behind the events written before the restart");
        adapter.Verify(a => a.AnswerPermissionAsync(It.IsAny<string>(), It.IsAny<CliPermissionDecision>(), It.IsAny<CancellationToken>()), Times.Never);

        // 重放追上了
        sequence.Current = 3;
        await RunForAsync(approvals, TimeSpan.FromMilliseconds(300));

        persisted.ShouldBe([(CliAgentEventType.PermissionResolved, 4)]);
        adapter.Verify(a => a.AnswerPermissionAsync("req-1", It.Is<CliPermissionDecision>(d => d.Allow && d.OptionId == "allow_once"),
            It.IsAny<CancellationToken>()), Times.Once);
        (await DbContext.Set<CliRunPermissionRequest>().AsNoTracking().IgnoreQueryFilters().SingleAsync()).ResolvedSequence.ShouldBe(4);
    }

    /// <summary>等人批准时 CLI 没有输出是应该的：空闲与工具看门狗暂停，硬超时照常。</summary>
    [Fact]
    public void WhileARequestWaitsForAnAnswer_TheIdleAndToolWatchdogsDoNotTrip_ButTheHardTimeoutDoes()
    {
        using var waiting = new CliRunWatchdog(TimeSpan.FromMilliseconds(-1), TimeSpan.FromMilliseconds(-1), null, CancellationToken.None);
        waiting.Observe(new CliAgentEvent { Type = CliAgentEventType.ToolUse, CallId = "t1" });
        waiting.BeginApprovalWait();

        waiting.CheckAndTrip().ShouldBeFalse();
        waiting.Failure.ShouldBeNull();

        using var hard = new CliRunWatchdog(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), TimeSpan.FromMilliseconds(-1), CancellationToken.None);
        hard.BeginApprovalWait();

        hard.CheckAndTrip().ShouldBeTrue();
        hard.Failure.ShouldBe(CliRunFailureReason.HardTimeout);
    }

    /// <summary>最后一个请求有了结局，计时从那一刻重新起算：等人的那段不算 CLI 的停滞，之后照常判。</summary>
    [Fact]
    public async Task AfterTheLastAnswer_TheWatchdogsCountFromThatMoment()
    {
        using var watchdog = new CliRunWatchdog(TimeSpan.FromMilliseconds(200), TimeSpan.FromMinutes(30), null, CancellationToken.None);
        watchdog.BeginApprovalWait();
        watchdog.BeginApprovalWait();
        await Task.Delay(300);

        watchdog.EndApprovalWait();
        watchdog.CheckAndTrip().ShouldBeFalse("one request is still waiting");

        watchdog.EndApprovalWait();
        watchdog.EndApprovalWait(); // 多余的一次不会让计数变负
        watchdog.CheckAndTrip().ShouldBeFalse("the idle time restarts when the last request is answered");

        await Task.Delay(300);
        watchdog.CheckAndTrip().ShouldBeTrue();
        watchdog.Failure.ShouldBe(CliRunFailureReason.IdleTimeout);
    }

    private static async Task RunForAsync(CliRunApprovals approvals, TimeSpan duration)
    {
        using var stop = new CancellationTokenSource(duration);
        await approvals.RunAsync(stop.Token);
    }
}
