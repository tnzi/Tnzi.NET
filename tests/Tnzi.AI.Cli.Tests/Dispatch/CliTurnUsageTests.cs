namespace Tnzi.AI.Cli.Tests;

/// <summary>
/// 会话累计值 → 这一轮的用量（<see cref="CliTurnUsage"/>）。
/// </summary>
/// <remarks>
/// 夹具是一条真实对话里同一个 claude 会话的最后三轮（claude 2.1.288，按 <c>--resume</c> 续接）：result 帧的 <c>modelUsage</c>
/// 每一轮都带着之前全部轮次。07:07 那一轮没有调用工具，Haiku（claude 在 WebSearch / WebFetch 里调用的小模型）的数字与上一轮完全相同，
/// Sonnet 的输出只多了 751 个 token，正是那条回复的长度。按上报值直接记账，那一天的 20 轮记了 1,019,118 个 token，
/// 按「本轮减上一轮」只有 198,639 个。
/// </remarks>
public class CliTurnUsageTests
{
    private const string Sonnet = "claude-sonnet-5-5";
    private const string Haiku = "claude-haiku-4-5";

    private static readonly Guid Run0704 = Guid.Parse("00000000-0000-0000-0000-000000000704");
    private static readonly Guid Run0706 = Guid.Parse("00000000-0000-0000-0000-000000000706");

    private static Dictionary<string, CliAgentTokenUsage> At0704() => new()
    {
        [Sonnet] = new() { InputTokens = 36, OutputTokens = 13509, CacheReadTokens = 660760, CacheWriteTokens = 54856, ReportedCostUsd = 0.486738m },
        [Haiku] = new() { InputTokens = 104486, OutputTokens = 4435, ReportedCostUsd = 0.216661m }
    };

    private static Dictionary<string, CliAgentTokenUsage> At0706() => new()
    {
        [Sonnet] = new() { InputTokens = 40, OutputTokens = 14389, CacheReadTokens = 791728, CacheWriteTokens = 57632, ReportedCostUsd = 0.532844m },
        [Haiku] = new() { InputTokens = 115978, OutputTokens = 4848, ReportedCostUsd = 0.240218m }
    };

    private static Dictionary<string, CliAgentTokenUsage> At0707() => new()
    {
        [Sonnet] = new() { InputTokens = 42, OutputTokens = 15140, CacheReadTokens = 859097, CacheWriteTokens = 58628, ReportedCostUsd = 0.557815m },
        [Haiku] = new() { InputTokens = 115978, OutputTokens = 4848, ReportedCostUsd = 0.240218m }
    };

    private static CliAgentResult Reported(IReadOnlyDictionary<string, CliAgentTokenUsage> totals, CliAgentTokenUsage? turn = null) => new()
    {
        Status = CliRunStatus.Completed,
        Usage = totals,
        UsageIsSessionTotal = true,
        TurnUsage = turn
    };

    [Fact]
    public void ResumedTurn_IsTheDifferenceFromThePreviousTurnsTotals_PerModel_WithCost()
    {
        var turn = CliTurnUsage.Resolve(Reported(At0706()), resumed: true, [new(Run0704, At0704())]);

        turn.BaselineRunId.ShouldBe(Run0704);
        turn.Usage[Sonnet].ShouldBe(new CliAgentTokenUsage
        {
            InputTokens = 4, OutputTokens = 880, CacheReadTokens = 130968, CacheWriteTokens = 2776, ReportedCostUsd = 0.046106m
        });
        // Claude Code 在内置工具里调用的小模型是真实消耗，照样记进这一轮
        turn.Usage[Haiku].ShouldBe(new CliAgentTokenUsage { InputTokens = 11492, OutputTokens = 413, ReportedCostUsd = 0.023557m });
        turn.SessionTotals.ShouldBe(At0706());
    }

    [Fact]
    public void ThreeTurnsInARow_EachTurnIsTheDifferenceBetweenAdjacentTotals_AndAnIdleModelDropsOut()
    {
        var second = CliTurnUsage.Resolve(Reported(At0706()), resumed: true, [new(Run0704, At0704())]);
        var third = CliTurnUsage.Resolve(Reported(At0707()), resumed: true,
            [new(Run0706, second.SessionTotals!), new(Run0704, At0704())]);

        third.BaselineRunId.ShouldBe(Run0706);
        third.Usage.Keys.ToList().ShouldBe([Sonnet], "Haiku was not called in this turn");
        third.Usage[Sonnet].ShouldBe(new CliAgentTokenUsage
        {
            InputTokens = 2, OutputTokens = 751, CacheReadTokens = 67369, CacheWriteTokens = 996, ReportedCostUsd = 0.024971m
        });

        // 两轮相加正好是最后一份累计值减第一份：没有一个 token 被记两次，也没有一个漏掉
        var summed = CliTurnUsage.Sum(second.Usage, third.Usage);
        summed[Sonnet].OutputTokens.ShouldBe(15140 - 13509);
        summed[Sonnet].ReportedCostUsd.ShouldBe(0.557815m - 0.486738m);
        summed[Haiku].ReportedCostUsd.ShouldBe(0.240218m - 0.216661m);
    }

    [Fact]
    public void ResumedTurn_WithTheCliTurnUsage_PicksTheNewestBaselineThatLeavesRoomForThisTurn()
    {
        // 主循环用量 = 这一轮 Sonnet 的那份（Haiku 是工具内部的调用，不在主循环里）
        var mainLoop = new CliAgentTokenUsage { InputTokens = 4, OutputTokens = 880, CacheReadTokens = 130968, CacheWriteTokens = 2776 };

        var turn = CliTurnUsage.Resolve(Reported(At0706(), mainLoop), resumed: true, [new(Run0704, At0704())]);

        turn.BaselineRunId.ShouldBe(Run0704);
        turn.Usage[Sonnet].OutputTokens.ShouldBe(880);
    }

    [Fact]
    public void Regenerate_WhenTheDroppedTurnWasNotCarriedOver_TheTooNewBaselineIsRejected()
    {
        // 线程回退过：最近结束的那一轮（被重新生成丢掉的 D）比被保留的 K 新。若 CLI 恢复的是 K 那一份，
        // 拿 D 去减会把这一轮算小；这一轮的主循环用量装不进「累计值 − D」，D 因此被排除
        var kept = At0704();
        var dropped = At0706();
        var thisTurn = new CliAgentTokenUsage { InputTokens = 5, OutputTokens = 900, CacheReadTokens = 130000, CacheWriteTokens = 3000, ReportedCostUsd = 0.05m };
        var totals = new Dictionary<string, CliAgentTokenUsage>(kept) { [Sonnet] = CliTurnUsage.Add(kept[Sonnet], thisTurn) };
        var mainLoop = thisTurn with { ReportedCostUsd = null };

        var turn = CliTurnUsage.Resolve(Reported(totals, mainLoop), resumed: true,
            [new(Run0706, dropped), new(Run0704, kept)]);

        turn.BaselineRunId.ShouldBe(Run0704);
        turn.Usage[Sonnet].OutputTokens.ShouldBe(900);
    }

    [Fact]
    public void Regenerate_WhenTheDroppedTurnWasCarriedOver_ItIsTheBaseline()
    {
        // claude 2.1.306 按会话 id 恢复它最后一次保存的总量，被丢掉的那一轮也在里面：减的就该是它
        var dropped = At0706();
        var thisTurn = new CliAgentTokenUsage { InputTokens = 3, OutputTokens = 500, CacheReadTokens = 70000, CacheWriteTokens = 900, ReportedCostUsd = 0.02m };
        var totals = new Dictionary<string, CliAgentTokenUsage>(dropped) { [Sonnet] = CliTurnUsage.Add(dropped[Sonnet], thisTurn) };

        var turn = CliTurnUsage.Resolve(Reported(totals, thisTurn with { ReportedCostUsd = null }), resumed: true,
            [new(Run0706, dropped), new(Run0704, At0704())]);

        turn.BaselineRunId.ShouldBe(Run0706);
        turn.Usage[Sonnet].ShouldBe(thisTurn);
        turn.Usage.ContainsKey(Haiku).ShouldBeFalse();
    }

    [Fact]
    public void ResumedButNothingWasCarriedOver_TheTotalsAreThisTurn()
    {
        // 续接了但 CLI 没恢复任何总量（例如配置目录被别的会话写过）：累计值装不下之前任何一份，它就是这一轮
        var fresh = new Dictionary<string, CliAgentTokenUsage>
        {
            [Sonnet] = new() { InputTokens = 3, OutputTokens = 200, CacheReadTokens = 20000, CacheWriteTokens = 5000, ReportedCostUsd = 0.03m }
        };

        var turn = CliTurnUsage.Resolve(Reported(fresh), resumed: true, [new(Run0706, At0706()), new(Run0704, At0704())]);

        turn.BaselineRunId.ShouldBeNull();
        turn.Ambiguous.ShouldBeTrue();
        turn.Usage[Sonnet].OutputTokens.ShouldBe(200);
        turn.SessionTotals.ShouldBe(fresh);
    }

    [Fact]
    public void NotResumed_TheTotalsAreThisTurn_EvenWhenOlderTotalsExist()
    {
        var turn = CliTurnUsage.Resolve(Reported(At0706()), resumed: false, [new(Run0704, At0704())]);

        turn.BaselineRunId.ShouldBeNull();
        turn.Ambiguous.ShouldBeFalse();
        turn.Usage[Sonnet].OutputTokens.ShouldBe(14389);
    }

    [Fact]
    public void OlderCliReportingTheTurnUsageAsATotalToo_StillSubtractsTheNewestCoveredBaseline()
    {
        // 第二条校验在「usage 也是累计值」的 CLI 上永远不成立；那时不能因此把整段会话再记一遍
        var cumulativeMainLoop = new CliAgentTokenUsage { InputTokens = 40, OutputTokens = 14389, CacheReadTokens = 791728, CacheWriteTokens = 57632 };

        var turn = CliTurnUsage.Resolve(Reported(At0706(), cumulativeMainLoop), resumed: true, [new(Run0704, At0704())]);

        turn.BaselineRunId.ShouldBe(Run0704);
        turn.Usage[Sonnet].OutputTokens.ShouldBe(880);
    }

    [Fact]
    public void PerTurnProtocols_ArePassedThrough_WithoutSessionTotals()
    {
        var acp = new CliAgentResult
        {
            Status = CliRunStatus.Completed,
            Usage = new Dictionary<string, CliAgentTokenUsage> { ["gpt-5"] = new() { InputTokens = 10, OutputTokens = 2 }, ["idle"] = new() }
        };

        var turn = CliTurnUsage.Resolve(acp, resumed: true, [new(Run0704, At0704())]);

        turn.Usage.Keys.ToList().ShouldBe(["gpt-5"]);
        turn.SessionTotals.ShouldBeNull();
    }

    [Fact]
    public void EstimateTotals_AddsTheStreamedTurnToThePreviousTotals()
    {
        var streamed = new Dictionary<string, CliAgentTokenUsage>
        {
            [Sonnet] = new() { InputTokens = 1, OutputTokens = 100, CacheReadTokens = 10, CacheWriteTokens = 5 }
        };

        var estimate = CliTurnUsage.EstimateTotals(At0704(), streamed);

        estimate[Sonnet].OutputTokens.ShouldBe(13609);
        // 逐帧的用量不带成本，相加后的成本不再可知：回落到按 token 估算，而不是只算有成本的那一半
        estimate[Sonnet].ReportedCostUsd.ShouldBeNull();
        estimate[Haiku].ShouldBe(At0704()[Haiku]);
    }
}
