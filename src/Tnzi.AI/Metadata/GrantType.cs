namespace Tnzi.AI.Metadata;

/// <summary>
/// Agent 工具授权的粒度类型。
/// 区分 <see cref="AgentToolGrant.ToolKey"/> 是按整组授权还是按单个工具授权。
/// Granularity of an Agent tool grant - whether the key authorizes a whole group or a single tool.
/// </summary>
public enum GrantType
{
    /// <summary>按工具组授权（ToolKey = 工具组名）。Group-level grant - ToolKey is the tool group name.</summary>
    Group = 0,

    /// <summary>按单个工具授权（ToolKey = 工具名）。Tool-level grant - ToolKey is an individual tool name.</summary>
    Tool = 1
}
