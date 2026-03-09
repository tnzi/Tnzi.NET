namespace Tnzi.AI.Engine.Router;

/// <summary>
/// Router 执行配置
/// </summary>
public class RouterConfiguration
{
    /// <summary>可路由的目标 Agent 名称到 AgentId 的映射</summary>
    public Dictionary<string, Guid> Targets { get; set; } = [];

    /// <summary>是否允许 router 直接回答而不转发</summary>
    public bool AllowDirectResponse { get; set; } = true;
}
