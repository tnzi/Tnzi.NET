namespace Tnzi.AI.Agents;

/// <summary>
/// 子 Agent 类型定义 — 描述一种可复用的子 Agent 配置模板
/// </summary>
/// <param name="Name">类型名称（如 "general-purpose", "bash", "researcher"）</param>
/// <param name="Description">类型描述</param>
/// <param name="ToolGroups">可用工具组列表</param>
/// <param name="ExcludedToolGroups">排除的工具组列表</param>
/// <param name="MaxTurns">最大对话轮次</param>
/// <param name="Instructions">默认系统指令（可选）</param>
/// <param name="DefaultModel">默认模型（可选）</param>
/// <param name="DefaultApprovalMode">默认工具审批模式（可选）</param>
/// <param name="CapabilityTags">能力标签（可选）</param>
public record SubAgentTypeDefinition(
    string Name,
    string Description,
    IReadOnlyList<string> ToolGroups,
    IReadOnlyList<string> ExcludedToolGroups,
    int MaxTurns,
    string? Instructions = null,
    string? DefaultModel = null,
    ToolApprovalMode? DefaultApprovalMode = null,
    IReadOnlyList<string>? CapabilityTags = null);
