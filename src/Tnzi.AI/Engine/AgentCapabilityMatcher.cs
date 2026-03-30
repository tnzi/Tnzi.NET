namespace Tnzi.AI.Engine;

/// <summary>
/// Agent 能力匹配器 — 用于 Workflow 模板和 RouterNode 的动态选 Agent。
/// 轻量实现，不引入重型 planning/control 接口。
/// </summary>
[ExperimentalApi(Reason = "Capability matching API may evolve as workflow templates mature")]
public static class AgentCapabilityMatcher
{
    /// <summary>按领域 + 角色 + 质量/延迟/成本约束筛选最佳 Agent</summary>
    public static IReadOnlyList<Agent> FindBestMatch(IEnumerable<Agent> candidates, AgentMatchCriteria criteria)
    {
        Check.NotNull(candidates);
        Check.NotNull(criteria);

        var filtered = candidates
            .Where(a => a.IsEnabled)
            .Where(a => MatchesDomains(a, criteria.RequiredDomains))
            .Where(a => MatchesRoles(a, criteria.RequiredRoles))
            .Where(a => criteria.MinQualityTier == null || a.QualityTier >= criteria.MinQualityTier.Value)
            .Where(a => criteria.MaxLatencyTier == null || a.LatencyTier <= criteria.MaxLatencyTier.Value)
            .Where(a => criteria.MaxCostTier == null || a.CostTier <= criteria.MaxCostTier.Value)
            .ToList();

        if (filtered.Count <= 1)
            return filtered;

        // 综合评分：Quality 越高越好，Latency 和 Cost 越低越好
        // Score = QualityTier * 2 - LatencyTier - CostTier（范围 -8 ~ 8）
        return filtered
            .OrderByDescending(a => a.QualityTier * 2 - a.LatencyTier - a.CostTier)
            .ThenBy(a => a.CostTier)
            .ToList();
    }

    /// <summary>
    /// 检查 Agent 的 Domains 是否与所需领域有交集
    /// </summary>
    private static bool MatchesDomains(Agent agent, List<string>? requiredDomains)
    {
        if (requiredDomains == null || requiredDomains.Count == 0)
            return true;

        if (agent.Domains == null || agent.Domains.Count == 0)
            return false;

        return requiredDomains.Any(d => agent.Domains.Contains(d, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 检查 Agent 的 Roles 是否与所需角色有交集
    /// </summary>
    private static bool MatchesRoles(Agent agent, List<string>? requiredRoles)
    {
        if (requiredRoles == null || requiredRoles.Count == 0)
            return true;

        if (agent.Roles == null || agent.Roles.Count == 0)
            return false;

        return requiredRoles.Any(r => agent.Roles.Contains(r, StringComparer.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Agent 能力匹配条件
/// </summary>
public class AgentMatchCriteria
{
    /// <summary>所需领域（与 Agent.Domains 取交集）</summary>
    public List<string>? RequiredDomains { get; init; }

    /// <summary>所需角色（与 Agent.Roles 取交集）</summary>
    public List<string>? RequiredRoles { get; init; }

    /// <summary>最低质量等级（1-5, 5=最高）</summary>
    public int? MinQualityTier { get; init; }

    /// <summary>最高延迟等级（1-5, 1=最快）</summary>
    public int? MaxLatencyTier { get; init; }

    /// <summary>最高成本等级（1-5, 1=最便宜）</summary>
    public int? MaxCostTier { get; init; }
}
