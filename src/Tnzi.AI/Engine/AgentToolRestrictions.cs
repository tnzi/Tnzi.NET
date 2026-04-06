namespace Tnzi.AI.Engine;

/// <summary>
/// Agent 工具限制分层 — 借鉴 Claude Code 4 层白名单/黑名单系统。
/// </summary>
/// <remarks>
/// <para>优先级顺序（deny > allow）：</para>
/// <para>1. GlobalDisallowedTools — 所有 Agent 禁止的工具</para>
/// <para>2. SubAgentDisallowedTools — 子 Agent 额外禁止的工具</para>
/// <para>3. AsyncAgentAllowedTools — 异步/后台 Agent 仅允许的工具（白名单模式）</para>
/// <para>4. MCP 工具始终放行</para>
/// <para>子 Agent 的 allowedTools 替换（非合并）父级规则，防权限泄漏。</para>
/// </remarks>
public class AgentToolRestrictions
{
    /// <summary>
    /// 全局禁止的工具列表（所有 Agent 包括主 Agent）
    /// </summary>
    public HashSet<string> GlobalDisallowedTools { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 子 Agent 额外禁止的工具列表
    /// </summary>
    public HashSet<string> SubAgentDisallowedTools { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 异步/后台 Agent 仅允许的工具列表（白名单模式，为空则不限制）
    /// </summary>
    public HashSet<string> AsyncAgentAllowedTools { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 判断工具是否允许执行
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <param name="isSubAgent">是否为子 Agent</param>
    /// <param name="isAsync">是否为异步/后台 Agent</param>
    /// <returns>是否允许</returns>
    public bool IsToolAllowed(string toolName, bool isSubAgent = false, bool isAsync = false)
    {
        Check.NotNullOrWhiteSpace(toolName);

        // MCP 工具始终放行（以 "mcp_" 或 "mcp__" 开头）
        if (toolName.StartsWith("mcp_", StringComparison.OrdinalIgnoreCase))
            return true;

        // 1. 全局禁止检查
        if (GlobalDisallowedTools.Contains(toolName))
            return false;

        // 2. 子 Agent 额外禁止检查
        if (isSubAgent && SubAgentDisallowedTools.Contains(toolName))
            return false;

        // 3. 异步 Agent 白名单检查（有配置时生效）
        if (isAsync && AsyncAgentAllowedTools.Count > 0 && !AsyncAgentAllowedTools.Contains(toolName))
            return false;

        return true;
    }

    /// <summary>
    /// 过滤工具列表，返回允许的工具
    /// </summary>
    public IReadOnlyList<AITool> FilterTools(IList<AITool> tools, bool isSubAgent = false, bool isAsync = false)
    {
        Check.NotNull(tools);
        return tools.Where(t => IsToolAllowed(t.Name, isSubAgent, isAsync)).ToList();
    }
}
