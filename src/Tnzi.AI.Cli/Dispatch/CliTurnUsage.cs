namespace Tnzi.AI.Cli.Dispatch;

/// <summary>
/// 一次进程报出的用量 → 这一轮的用量。
/// </summary>
/// <remarks>
/// <para>
/// claude 在 result 帧里报的 <c>modelUsage</c> / <c>costUSD</c> 是<b>会话累计值</b>：续接（包括截断续接分叉出的新会话）的进程
/// 从这个会话自己保存的总量接着算（claude 2.1.306 存在会话记录里，按会话 id 以最后一次保存为准；更早的版本存在配置目录里，
/// 只在上次的会话 id 与这次续接的相同时恢复）。直接按它记账，长对话的每一轮都会把之前全部的花费再记一遍。
/// </para>
/// <para>
/// 这一轮 = 累计值 − 进程恢复出来的那份。恢复出来的是哪份，框架看不见，只能从<b>同一会话上已结束的轮次</b>记下的累计值里认：
/// 从最近的往前试，取第一份满足两条的：
/// </para>
/// <list type="number">
/// <item>累计值在每个模型、每项计数上都不小于它（累计值只增不减；少了一个模型或某项变小，就不可能是从它接着算的）；</item>
/// <item>差值至少装得下 CLI 自己数的这一轮主循环用量（<see cref="CliAgentResult.TurnUsage"/>）。基线认得太新（编辑、重新生成后
/// 被丢掉的那一轮其实没被恢复）时差值会比这一轮还小，这一条把它排除；基线认得太旧则差值偏大，所以从最近的往前试。</item>
/// </list>
/// <para>
/// 没有一份同时满足两条时，退回满足第一条的最近一份（较旧的 CLI 版本若把 <c>usage</c> 也报成累计值，第二条永远不成立，
/// 不能因此把整段会话再记一遍）；第一条都不满足，说明这次没有恢复，累计值就是这一轮。
/// </para>
/// </remarks>
internal static class CliTurnUsage
{
    /// <summary>成本比较的容差：CLI 的成本是浮点数，相减会有尾差。</summary>
    private const decimal CostTolerance = 0.000001m;

    /// <summary>
    /// 一份候选基线：同一会话上已结束的一轮在结束时的会话累计值。
    /// </summary>
    /// <param name="RunId">记下它的运行。</param>
    /// <param name="Totals">按模型的累计值。</param>
    internal sealed record Baseline(Guid RunId, IReadOnlyDictionary<string, CliAgentTokenUsage> Totals);

    /// <summary>
    /// 折算结果。
    /// </summary>
    /// <param name="Usage">这一轮的用量（按模型，用量为零的模型不列）。</param>
    /// <param name="SessionTotals">这一轮结束时的会话累计值，下一轮拿它当基线；协议不报累计值时为 null。</param>
    /// <param name="BaselineRunId">认定为基线的那一轮；没有扣减时为 null。</param>
    /// <param name="Ambiguous">续接了却没有任何一份候选能被累计值覆盖：可能是没恢复（那就是对的），也可能是恢复了一份框架没记下的总量（那就记多了）。</param>
    internal sealed record Resolution(
        IReadOnlyDictionary<string, CliAgentTokenUsage> Usage,
        IReadOnlyDictionary<string, CliAgentTokenUsage>? SessionTotals,
        Guid? BaselineRunId,
        bool Ambiguous);

    /// <summary>
    /// 折算一次进程的用量。
    /// </summary>
    /// <param name="result">适配器给出的结果。</param>
    /// <param name="resumed">这次进程是否带着续接指针启动。没有续接时 CLI 不会恢复任何总量。</param>
    /// <param name="candidates">候选基线，最近结束的在前。</param>
    public static Resolution Resolve(CliAgentResult result, bool resumed, IReadOnlyList<Baseline> candidates)
    {
        Check.NotNull(result);
        Check.NotNull(candidates);

        if (!result.UsageIsSessionTotal)
        {
            return new Resolution(WithoutEmpty(result.Usage), null, null, false);
        }

        if (!resumed)
        {
            return new Resolution(WithoutEmpty(result.Usage), result.Usage, null, false);
        }

        Baseline? covered = null;
        foreach (var candidate in candidates)
        {
            if (!HasTokens(candidate.Totals.Values) || !Covers(result.Usage, candidate.Totals))
            {
                continue;
            }

            covered ??= candidate;
            var delta = Subtract(result.Usage, candidate.Totals);
            if (result.TurnUsage is not { } turn || CoversTurn(delta, turn))
            {
                return new Resolution(delta, result.Usage, candidate.RunId, false);
            }
        }

        return covered is not null
            ? new Resolution(Subtract(result.Usage, covered.Totals), result.Usage, covered.RunId, false)
            : new Resolution(WithoutEmpty(result.Usage), result.Usage, null, candidates.Count > 0);
    }

    /// <summary>
    /// 一轮没跑完（没有 result 帧）时，下一轮最可能恢复出来的会话累计值：上一份累计值加上这一轮逐帧看到的用量。
    /// </summary>
    /// <remarks>
    /// 进程被收尾时先关 stdin，claude 若因此正常退出，会把这一轮已经花掉的那部分存进会话；被强杀则不存，下一轮从更早那份接着算。
    /// 两种都可能，所以这份估算只是候选之一，下一轮按 <see cref="Resolve"/> 的规则在它与更早的那份之间选。
    /// </remarks>
    public static IReadOnlyDictionary<string, CliAgentTokenUsage> EstimateTotals(
        IReadOnlyDictionary<string, CliAgentTokenUsage>? previousTotals,
        IReadOnlyDictionary<string, CliAgentTokenUsage> turnUsage)
    {
        var totals = new Dictionary<string, CliAgentTokenUsage>(previousTotals ?? new Dictionary<string, CliAgentTokenUsage>(), StringComparer.Ordinal);
        foreach (var (model, usage) in turnUsage)
        {
            totals[model] = totals.TryGetValue(model, out var before) ? Add(before, usage) : usage;
        }

        return totals;
    }

    /// <summary>两份用量相加（按模型）。成本只在两边都可知时相加，见 <see cref="Add(CliAgentTokenUsage, CliAgentTokenUsage)"/>。</summary>
    public static IReadOnlyDictionary<string, CliAgentTokenUsage> Sum(
        IReadOnlyDictionary<string, CliAgentTokenUsage> a, IReadOnlyDictionary<string, CliAgentTokenUsage> b)
    {
        var sum = new Dictionary<string, CliAgentTokenUsage>(a, StringComparer.Ordinal);
        foreach (var (model, usage) in b)
        {
            sum[model] = sum.TryGetValue(model, out var earlier) ? Add(earlier, usage) : usage;
        }

        return sum;
    }

    /// <summary>
    /// 同一模型两份用量相加。自报成本只在两份都可知时相加（没用 token 的那份可知为 0）；
    /// 有一份用了 token 却没报成本时整项不报，回落到按相加后的 token 估算，而不是只算报了的那一半。
    /// </summary>
    public static CliAgentTokenUsage Add(CliAgentTokenUsage a, CliAgentTokenUsage b)
    {
        static bool CostKnown(CliAgentTokenUsage u) => u.ReportedCostUsd.HasValue || !HasTokens(u);

        return new CliAgentTokenUsage
        {
            InputTokens = a.InputTokens + b.InputTokens,
            OutputTokens = a.OutputTokens + b.OutputTokens,
            CacheReadTokens = a.CacheReadTokens + b.CacheReadTokens,
            CacheWriteTokens = a.CacheWriteTokens + b.CacheWriteTokens,
            ReportedCostUsd = (a.ReportedCostUsd.HasValue || b.ReportedCostUsd.HasValue) && CostKnown(a) && CostKnown(b)
                ? (a.ReportedCostUsd ?? 0m) + (b.ReportedCostUsd ?? 0m)
                : null
        };
    }

    /// <summary>累计值在基线的每个模型、每项计数上都不小于基线。</summary>
    private static bool Covers(
        IReadOnlyDictionary<string, CliAgentTokenUsage> totals, IReadOnlyDictionary<string, CliAgentTokenUsage> baseline)
    {
        foreach (var (model, before) in baseline)
        {
            if (!HasTokens(before))
            {
                continue;
            }

            if (!totals.TryGetValue(model, out var now)
                || now.InputTokens < before.InputTokens
                || now.OutputTokens < before.OutputTokens
                || now.CacheReadTokens < before.CacheReadTokens
                || now.CacheWriteTokens < before.CacheWriteTokens)
            {
                return false;
            }

            if (now.ReportedCostUsd is { } costNow && before.ReportedCostUsd is { } costBefore
                && costNow + CostTolerance < costBefore)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>差值（各模型相加）在每项计数上都装得下这一轮的主循环用量。</summary>
    private static bool CoversTurn(IReadOnlyDictionary<string, CliAgentTokenUsage> delta, CliAgentTokenUsage turn)
        => delta.Values.Sum(u => u.InputTokens) >= turn.InputTokens
           && delta.Values.Sum(u => u.OutputTokens) >= turn.OutputTokens
           && delta.Values.Sum(u => u.CacheReadTokens) >= turn.CacheReadTokens
           && delta.Values.Sum(u => u.CacheWriteTokens) >= turn.CacheWriteTokens;

    /// <summary>累计值减基线（按模型；调用方已确认不会出现负数）。差为零的模型不列。</summary>
    private static Dictionary<string, CliAgentTokenUsage> Subtract(
        IReadOnlyDictionary<string, CliAgentTokenUsage> totals, IReadOnlyDictionary<string, CliAgentTokenUsage> baseline)
    {
        var delta = new Dictionary<string, CliAgentTokenUsage>(StringComparer.Ordinal);
        foreach (var (model, now) in totals)
        {
            var before = baseline.GetValueOrDefault(model);
            var usage = before is null
                ? now
                : new CliAgentTokenUsage
                {
                    InputTokens = now.InputTokens - before.InputTokens,
                    OutputTokens = now.OutputTokens - before.OutputTokens,
                    CacheReadTokens = now.CacheReadTokens - before.CacheReadTokens,
                    CacheWriteTokens = now.CacheWriteTokens - before.CacheWriteTokens,
                    ReportedCostUsd = now.ReportedCostUsd is { } costNow && before.ReportedCostUsd is { } costBefore
                        ? Math.Max(0m, costNow - costBefore)
                        : before.ReportedCostUsd is null && !HasTokens(before) ? now.ReportedCostUsd : null
                };

            if (HasTokens(usage))
            {
                delta[model] = usage;
            }
        }

        return delta;
    }

    private static Dictionary<string, CliAgentTokenUsage> WithoutEmpty(IReadOnlyDictionary<string, CliAgentTokenUsage> usage)
        => usage.Where(u => HasTokens(u.Value)).ToDictionary(u => u.Key, u => u.Value, StringComparer.Ordinal);

    private static bool HasTokens(IEnumerable<CliAgentTokenUsage> usage) => usage.Any(HasTokens);

    private static bool HasTokens(CliAgentTokenUsage usage)
        => usage.InputTokens + usage.OutputTokens + usage.CacheReadTokens + usage.CacheWriteTokens > 0;
}
