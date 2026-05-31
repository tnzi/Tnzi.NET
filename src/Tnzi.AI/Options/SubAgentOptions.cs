namespace Tnzi.AI.Options;

public class SubAgentOptions
{
    public bool Enabled { get; set; } = true;
    public int MaxConcurrentSubAgents { get; set; } = 3;
    public int TimeoutSeconds { get; set; } = 900;

    /// <summary>
    /// 调用链最大深度（根 → 叶，默认 5）。超过此深度拒绝 Spawn。
    /// </summary>
    public int MaxDepth { get; set; } = 5;

    /// <summary>
    /// 单个根 Run 下最大后代数量（默认 25）。超过此数量拒绝 Spawn。
    /// </summary>
    public int MaxDescendantsPerRoot { get; set; } = 25;

    /// <summary>
    /// 全局禁止的工具名称列表（所有 Agent 包括主 Agent 均不可使用）
    /// </summary>
    public List<string> GlobalDisallowedTools { get; set; } = [];

    /// <summary>
    /// 子 Agent 额外禁止的工具名称列表（主 Agent 可用，子 Agent 禁止）
    /// </summary>
    public List<string> SubAgentDisallowedTools { get; set; } = [];

    /// <summary>
    /// 异步/后台 Agent 仅允许的工具名称列表（白名单模式，为空则不限制）
    /// </summary>
    public List<string> AsyncAgentAllowedTools { get; set; } = [];
}
