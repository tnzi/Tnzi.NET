namespace Tnzi.AI.Metadata;

/// <summary>
/// Agent 执行模式
/// </summary>
public enum AgentExecutionMode
{
    /// <summary>单 Agent 执行（默认）</summary>
    Single = 0,

    /// <summary>Agent 间转接（Handoff）</summary>
    Handoff = 1,

    /// <summary>Agent 作为工具（父 Agent 调用子 Agent 作为工具函数）</summary>
    AgentAsTools = 2,

    /// <summary>路由 Agent 选择最合适的目标 Agent</summary>
    Router = 3
}
