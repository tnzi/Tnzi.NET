namespace Tnzi.AI.Infrastructure;

/// <summary>
/// 当前 AI 执行请求访问器。
/// </summary>
public interface IAgentExecutionContextAccessor
{
    AgentRunRequest? CurrentRequest { get; set; }

    /// <summary>
    /// 当前执行周期的共享属性包 — 工具与中间件间传递数据（例如 ClarificationRequest、Todos）
    /// </summary>
    Dictionary<string, object> Properties { get; }
}

/// <summary>
/// 基于 AsyncLocal 的当前 AI 执行请求访问器。
/// </summary>
public sealed class AgentExecutionContextAccessor : IAgentExecutionContextAccessor
{
    private static readonly AsyncLocal<AgentRunRequest?> CurrentRequestHolder = new();
    private static readonly AsyncLocal<Dictionary<string, object>?> PropertiesHolder = new();

    public AgentRunRequest? CurrentRequest
    {
        get => CurrentRequestHolder.Value;
        set => CurrentRequestHolder.Value = value;
    }

    public Dictionary<string, object> Properties
    {
        get => PropertiesHolder.Value ??= [];
    }
}
