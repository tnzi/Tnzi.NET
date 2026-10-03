namespace Tnzi.AI.Dtos;

/// <summary>
/// 设置单条授权启用状态的请求体。Request body for toggling a single grant's enabled flag.
/// </summary>
public sealed class SetGrantEnabledDto
{
    /// <summary>目标启用状态。Target enabled state.</summary>
    public bool Enabled { get; init; }
}

/// <summary>
/// 设置单条授权优先级的请求体。Request body for setting a single grant's priority.
/// </summary>
public sealed class SetGrantPriorityDto
{
    /// <summary>
    /// 优先级：只决定列表投影（AgentDto 的工具组 / 技能 / 知识库列表）里的顺序（越大越靠前），运行时不据此裁决先后。
    /// Priority: only orders the list projections (higher first); the runtime does not rank grants by it.
    /// </summary>
    public int Priority { get; init; }
}

/// <summary>
/// 一条授权的治理视图（含已禁用条目）。Key 是资源的按值引用：工具组名 / 工具名 / 技能 slug / 知识库 Id。
/// Governance view of a single grant, disabled grants included. Key is the by-value resource
/// reference: tool group name, tool name, skill slug, or knowledge base id.
/// </summary>
public sealed class AgentGrantDto
{
    /// <summary>授权 Id，供启停 / 删除端点寻址。Grant id, addressed by the enable and delete endpoints.</summary>
    public Guid Id { get; init; }

    /// <summary>资源键。Resource key.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>是否启用；禁用的授权对运行时不可见。Whether the grant is enabled; disabled grants are invisible to the runtime.</summary>
    public bool IsEnabled { get; init; }

    /// <summary>优先级（仅决定列表投影顺序）。Priority (only orders the list projections).</summary>
    public int Priority { get; init; }
}

/// <summary>
/// 一个 Agent 的全部授权（含已禁用条目），按资源类别分组。
/// All grants of one agent, disabled ones included, grouped by resource category.
/// </summary>
public sealed class AgentGrantListDto
{
    /// <summary>工具组授权（GrantType=Group）。Tool group grants.</summary>
    public List<AgentGrantDto> ToolGroups { get; init; } = new();

    /// <summary>单工具授权（GrantType=Tool）。Single-tool grants.</summary>
    public List<AgentGrantDto> ToolNames { get; init; } = new();

    /// <summary>技能授权。Skill grants.</summary>
    public List<AgentGrantDto> Skills { get; init; } = new();

    /// <summary>知识库授权（Key 为知识库 Id）。Knowledge base grants (Key is the knowledge base id).</summary>
    public List<AgentGrantDto> KnowledgeBases { get; init; } = new();
}

/// <summary>
/// 反向查询的一行：一个持有该资源已启用授权的（未删除）Agent。
/// One reverse-lookup row: a (non-deleted) agent holding an enabled grant for the resource.
/// </summary>
public sealed class AgentGrantUsageDto
{
    /// <summary>Agent Id。Agent id.</summary>
    public Guid AgentId { get; init; }

    /// <summary>Agent 名称。Agent name.</summary>
    public string AgentName { get; init; } = string.Empty;

    /// <summary>Agent 本身是否启用。Whether the agent itself is enabled.</summary>
    public bool AgentIsEnabled { get; init; }
}
