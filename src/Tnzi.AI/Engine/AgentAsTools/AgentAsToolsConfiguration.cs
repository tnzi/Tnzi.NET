namespace Tnzi.AI.Engine.AgentAsTools;

/// <summary>
/// AgentAsTools 执行策略配置
/// </summary>
public class AgentAsToolsConfiguration
{
    /// <summary>可调用的子 Agent（显示名 → Agent ID）</summary>
    public Dictionary<string, Guid> Agents { get; set; } = new();

    /// <summary>是否对子 Agent 启用流式转发（需同时注册 IAgentStreamForwarder，默认关闭）</summary>
    public bool EnableChildStreaming { get; set; }

    /// <summary>子 Agent 最大并发数（默认 3, 范围 [2,4]）</summary>
    public int MaxConcurrentSubAgents { get; set; } = 3;

    /// <summary>单个子 Agent 超时时间（默认 15 分钟）</summary>
    public TimeSpan SubAgentTimeout { get; set; } = TimeSpan.FromMinutes(15);
}
