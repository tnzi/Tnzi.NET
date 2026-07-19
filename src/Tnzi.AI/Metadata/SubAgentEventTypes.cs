namespace Tnzi.AI.Metadata;

/// <summary>
/// 子 Agent 流式事件类型常量 (AgentAsTools 模式)
/// </summary>
public static class SubAgentEventTypes
{
    public const string Started = "sub_agent_started";
    public const string Completed = "sub_agent_completed";
    public const string Failed = "sub_agent_failed";
    public const string TimedOut = "sub_agent_timed_out";
}
