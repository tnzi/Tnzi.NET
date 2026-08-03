namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 分层看门狗。
/// </summary>
/// <remarks>
/// 分层的理由：两种停滞的合理等待时长差一个数量级。一次 <c>npm install</c> 跑十分钟很正常，
/// 而十分钟一个事件都不发几乎一定是卡死了。用同一个阈值必然要么误杀要么放过。
/// </remarks>
public class CliRunWatchdogTests
{
    [Fact]
    public void Idle_TripsWhenNoEventArrives()
    {
        using var watchdog = new CliRunWatchdog(
            idle: TimeSpan.FromMilliseconds(-1),
            tool: TimeSpan.FromMinutes(30),
            hard: null,
            CancellationToken.None);

        watchdog.CheckAndTrip().ShouldBeTrue();
        watchdog.Failure.ShouldBe(CliRunFailureReason.IdleTimeout);
        watchdog.Token.IsCancellationRequested.ShouldBeTrue();
    }

    [Fact]
    public void Idle_DoesNotTripWhileEventsKeepArriving()
    {
        using var watchdog = new CliRunWatchdog(
            idle: TimeSpan.FromMinutes(10),
            tool: TimeSpan.FromMinutes(30),
            hard: null,
            CancellationToken.None);

        watchdog.Observe(new CliAgentEvent { Type = CliAgentEventType.Text, Content = "progress" });

        watchdog.CheckAndTrip().ShouldBeFalse();
        watchdog.Failure.ShouldBeNull();
    }

    [Fact]
    public void PendingTool_UsesTheToolBudgetNotTheIdleBudget()
    {
        // 工具正在干活时"没有事件"是合理的，不该按空闲判死。
        using var watchdog = new CliRunWatchdog(
            idle: TimeSpan.FromMilliseconds(-1),
            tool: TimeSpan.FromMinutes(30),
            hard: null,
            CancellationToken.None);

        watchdog.Observe(new CliAgentEvent { Type = CliAgentEventType.ToolUse, CallId = "t1" });

        watchdog.CheckAndTrip().ShouldBeFalse();
    }

    [Fact]
    public void PendingTool_TripsWithToolTimeoutWhenTheResultNeverArrives()
    {
        using var watchdog = new CliRunWatchdog(
            idle: TimeSpan.FromMinutes(10),
            tool: TimeSpan.FromMilliseconds(-1),
            hard: null,
            CancellationToken.None);

        watchdog.Observe(new CliAgentEvent { Type = CliAgentEventType.ToolUse, CallId = "t1" });

        watchdog.CheckAndTrip().ShouldBeTrue();
        watchdog.Failure.ShouldBe(CliRunFailureReason.ToolTimeout);
    }

    [Fact]
    public void ParallelTools_KeepTheToolBudgetActiveUntilTheLastResult()
    {
        // 清零而不是重新起算，会让最慢的那个工具永远等不到判决。
        using var watchdog = new CliRunWatchdog(
            idle: TimeSpan.FromMilliseconds(-1),
            tool: TimeSpan.FromMinutes(30),
            hard: null,
            CancellationToken.None);

        watchdog.Observe(new CliAgentEvent { Type = CliAgentEventType.ToolUse, CallId = "t1" });
        watchdog.Observe(new CliAgentEvent { Type = CliAgentEventType.ToolUse, CallId = "t2" });
        watchdog.Observe(new CliAgentEvent { Type = CliAgentEventType.ToolResult, CallId = "t1" });

        // 还有一个工具在跑：仍走工具预算，不按空闲判死。
        watchdog.CheckAndTrip().ShouldBeFalse();

        watchdog.Observe(new CliAgentEvent { Type = CliAgentEventType.ToolResult, CallId = "t2" });

        // 工具全部收敛，回到空闲预算 —— 这里空闲预算已是负值，立刻判死。
        watchdog.CheckAndTrip().ShouldBeTrue();
        watchdog.Failure.ShouldBe(CliRunFailureReason.IdleTimeout);
    }

    [Fact]
    public void HardTimeout_IsOffByDefaultSoLongRunsAreNotKilledForRunningLong()
    {
        // 一个持续产出事件的长任务不该仅仅因为跑得久被杀 ——
        // 外部 agent 适合的正是「一次派一个完整任务」，几小时是正常量级。
        using var watchdog = new CliRunWatchdog(
            idle: TimeSpan.FromMinutes(10),
            tool: TimeSpan.FromMinutes(30),
            hard: null,
            CancellationToken.None);

        watchdog.Observe(new CliAgentEvent { Type = CliAgentEventType.Text, Content = "still working" });

        watchdog.CheckAndTrip().ShouldBeFalse();
    }

    [Fact]
    public void HardTimeout_TripsWhenConfigured()
    {
        using var watchdog = new CliRunWatchdog(
            idle: TimeSpan.FromMinutes(10),
            tool: TimeSpan.FromMinutes(30),
            hard: TimeSpan.FromMilliseconds(-1),
            CancellationToken.None);

        watchdog.CheckAndTrip().ShouldBeTrue();
        watchdog.Failure.ShouldBe(CliRunFailureReason.HardTimeout);
    }
}

/// <summary>
/// 稳定 brief。
/// </summary>
public class CliBriefComposerTests
{
    [Fact]
    public void Compose_IsByteStableAcrossCalls()
    {
        // brief 落在 provider 的缓存前缀里。内容一变就作废整段历史的 prompt cache，
        // 续接一次的成本按整段上下文重算 —— 所以这里绝不能出现时间戳 / 运行 ID / 触发者。
        var agent = new Agent
        {
            Name = "Reviewer",
            Description = "Reviews diffs",
            Instructions = "Be terse.",
            Persona = "You are meticulous."
        };

        var composer = new CliBriefComposer();
        var provider = CliBuiltInProviders.All["claude"];

        var first = composer.Compose(agent, provider);
        var second = composer.Compose(agent, provider);

        Encoding.UTF8.GetBytes(second).ShouldBe(Encoding.UTF8.GetBytes(first));
    }

    [Fact]
    public void Compose_IncludesPersonaAndInstructions()
    {
        var agent = new Agent
        {
            Name = "Reviewer",
            Instructions = "Be terse.",
            Persona = "You are meticulous."
        };

        var brief = new CliBriefComposer().Compose(agent, CliBuiltInProviders.All["claude"]);

        brief.ShouldContain("You are meticulous.");
        brief.ShouldContain("Be terse.");
        // 运行契约必须在场：无人值守下的交互式提问会静默吞掉整个任务。
        brief.ShouldContain("Do not ask interactive questions");
    }
}
