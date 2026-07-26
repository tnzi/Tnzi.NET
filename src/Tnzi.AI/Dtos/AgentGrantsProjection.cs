namespace Tnzi.AI.Dtos;

/// <summary>
/// Agent 授权投影 - 把三类 junction grant 实体（工具/技能/知识库）的<b>已启用</b>条目
/// 折叠成扁平的值列表，供 AgentResolver 与 DTO 消费（取代 Agent 上的 JSON 列）。
/// 仅包含 <c>IsEnabled == true</c> 的授权；各列表按 Priority 降序、再按创建时间升序排列。
/// Read-side projection of an Agent's grants - flattens the <b>enabled</b> entries of the three
/// junction grant entities (tool/skill/knowledge) into plain value lists for the resolver/DTO to
/// consume (replacing the JSON columns on Agent). Only <c>IsEnabled == true</c> grants are included;
/// each list is ordered by Priority descending, then by creation time ascending.
/// </summary>
public sealed record AgentGrantsProjection
{
    /// <summary>工具组键列表（GrantType=Group 的已启用工具授权）。Enabled tool-group keys (GrantType=Group).</summary>
    public IReadOnlyList<string> ToolGroups { get; init; } = Array.Empty<string>();

    /// <summary>单工具键列表（GrantType=Tool 的已启用工具授权）。Enabled single-tool keys (GrantType=Tool).</summary>
    public IReadOnlyList<string> ToolNames { get; init; } = Array.Empty<string>();

    /// <summary>技能 slug 列表（已启用技能授权）。Enabled skill slugs.</summary>
    public IReadOnlyList<string> SkillSlugs { get; init; } = Array.Empty<string>();

    /// <summary>知识库 Id 列表（已启用知识库授权）。Enabled knowledge base ids.</summary>
    public IReadOnlyList<Guid> KnowledgeBaseIds { get; init; } = Array.Empty<Guid>();
}
