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
}
