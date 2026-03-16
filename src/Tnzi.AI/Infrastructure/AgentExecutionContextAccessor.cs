namespace Tnzi.AI.Infrastructure;

/// <summary>
/// 当前 AI 执行请求访问器。
/// </summary>
public interface IAgentExecutionContextAccessor
{
    AgentRunRequest? CurrentRequest { get; set; }
}

/// <summary>
/// 基于 AsyncLocal 的当前 AI 执行请求访问器。
/// </summary>
public sealed class AgentExecutionContextAccessor : IAgentExecutionContextAccessor
{
    private static readonly AsyncLocal<AgentRunRequest?> CurrentRequestHolder = new();

    public AgentRunRequest? CurrentRequest
    {
        get => CurrentRequestHolder.Value;
        set => CurrentRequestHolder.Value = value;
    }
}
