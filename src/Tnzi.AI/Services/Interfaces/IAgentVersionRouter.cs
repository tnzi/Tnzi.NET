namespace Tnzi.AI.Services.Interfaces;

/// <summary>
/// Agent 版本路由器 — 支持 A/B 测试流量分配
/// 当 Agent 配置了 A/B 测试时，根据流量百分比选择不同版本的配置
/// </summary>
public interface IAgentVersionRouter
{
    /// <summary>
    /// 路由到合适的 Agent 版本。
    /// 如果 Agent 配置了 A/B 测试，按流量百分比选择版本 A 或版本 B 的配置并覆盖 Agent 实体。
    /// 如果未配置 A/B 测试，直接返回原始 Agent 实体（passthrough）。
    /// </summary>
    /// <param name="agent">当前 Agent 实体</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>可能被 A/B 版本覆盖后的 Agent 实体（不修改原始对象，返回新对象）</returns>
    Task<AgentVersionRouteResult> RouteAsync(Agent agent, CancellationToken ct);
}

/// <summary>
/// Agent 版本路由结果
/// </summary>
public class AgentVersionRouteResult
{
    /// <summary>路由后的 Agent 实体（可能是版本覆盖后的副本）</summary>
    public required Agent Agent { get; init; }

    /// <summary>是否应用了 A/B 测试路由</summary>
    public bool IsAbTestRouted { get; init; }

    /// <summary>选中的版本号（仅 A/B 路由时有值）</summary>
    public int? SelectedVersion { get; init; }

    /// <summary>选中的变体（"A" 或 "B"，仅 A/B 路由时有值）</summary>
    public string? SelectedVariant { get; init; }

    /// <summary>
    /// 路由后变体的资源授权投影（来自版本快照，而非当前 live junction）。
    /// 仅 A/B 路由时有值；passthrough 时为 <c>null</c>，此时 resolver 回退读取 live grants。
    /// 资源列（ToolGroups/ToolNames/SkillSlugs/KnowledgeBaseIds）已从 Agent 实体删除（grant 化），
    /// 无法随 Agent 对象传递，故必须经此字段单独携带——否则 A/B 路由静默使用 live 资源而非变体快照。
    /// The routed variant's grant projection sourced from the version SNAPSHOT (not the live junction).
    /// Non-null only when A/B-routed; null on passthrough → resolver falls back to reading live grants.
    /// </summary>
    public AgentGrantsProjection? SnapshotGrants { get; init; }

    /// <summary>
    /// 创建 passthrough 结果（无 A/B 测试）
    /// </summary>
    public static AgentVersionRouteResult Passthrough(Agent agent) =>
        new() { Agent = agent, IsAbTestRouted = false };
}
