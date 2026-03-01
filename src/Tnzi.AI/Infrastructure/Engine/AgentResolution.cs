namespace Tnzi.AI.Infrastructure.Engine;

/// <summary>
/// Agent 解析结果
/// </summary>
public class AgentResolution
{
    /// <summary>创建的 AgentExecutor 实例</summary>
    public AgentExecutor? Agent { get; init; }

    /// <summary>提供商名称</summary>
    public string Provider { get; init; } = string.Empty;

    /// <summary>模型名称</summary>
    public string? Model { get; init; }

    /// <summary>Agent ID（当通过已定义 Agent 创建时非 null）</summary>
    public Guid? AgentId { get; init; }

    /// <summary>错误码（仅失败时非 null）</summary>
    public string? ErrorCode { get; init; }

    /// <summary>是否解析成功</summary>
    public bool IsSuccess => Agent != null;

    /// <summary>
    /// 创建成功结果
    /// </summary>
    public static AgentResolution Success(AgentExecutor agent, string provider, string? model, Guid? agentId)
    {
        return new AgentResolution { Agent = agent, Provider = provider, Model = model, AgentId = agentId };
    }

    /// <summary>
    /// 创建失败结果
    /// </summary>
    public static AgentResolution Failure(string provider, string? model, Guid? agentId, string errorCode)
    {
        return new AgentResolution { Provider = provider, Model = model, AgentId = agentId, ErrorCode = errorCode };
    }
}
