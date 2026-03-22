namespace Tnzi.AI.Engine;

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

    /// <summary>执行模式</summary>
    public AgentExecutionMode ExecutionMode { get; init; } = AgentExecutionMode.Single;

    /// <summary>额外配置 JSON（当通过已定义 Agent 创建时传递）</summary>
    public string? AgentConfiguration { get; init; }

    /// <summary>错误码（仅失败时非 null）</summary>
    public string? ErrorCode { get; init; }

    /// <summary>是否解析成功</summary>
    public bool IsSuccess => Agent != null || ExecutionMode == AgentExecutionMode.ExternalCli;

    /// <summary>
    /// 创建 AgentExecutor 时使用的原始参数 — 供 SkillConstraintMiddleware 触发模型/Provider 覆盖时重建 Executor 使用
    /// </summary>
    public AgentCreationParameters? CreationParameters { get; init; }

    /// <summary>
    /// 创建成功结果
    /// </summary>
    public static AgentResolution Success(AgentExecutor agent, string provider, string? model, Guid? agentId, string? agentConfiguration = null, AgentExecutionMode executionMode = AgentExecutionMode.Single, AgentCreationParameters? creationParameters = null)
    {
        return new AgentResolution { Agent = agent, Provider = provider, Model = model, AgentId = agentId, AgentConfiguration = agentConfiguration, ExecutionMode = executionMode, CreationParameters = creationParameters };
    }

    /// <summary>
    /// 创建无 AgentExecutor 的成功结果（用于 ExternalCli 模式，不需要 ChatClient）
    /// </summary>
    public static AgentResolution SuccessWithoutExecutor(
        string provider, string? model, Guid? agentId,
        string? agentConfiguration, AgentExecutionMode executionMode)
    {
        return new AgentResolution
        {
            Agent = null, Provider = provider, Model = model,
            AgentId = agentId, AgentConfiguration = agentConfiguration,
            ExecutionMode = executionMode
        };
    }

    /// <summary>
    /// 创建失败结果
    /// </summary>
    public static AgentResolution Failure(string provider, string? model, Guid? agentId, string errorCode)
    {
        return new AgentResolution { Provider = provider, Model = model, AgentId = agentId, ErrorCode = errorCode };
    }
}

/// <summary>
/// AgentExecutor 创建时的原始参数快照 — 用于在模型/Provider 覆盖时重建执行器
/// </summary>
public record AgentCreationParameters(
    string? Instructions,
    string? Name,
    IEnumerable<string>? ToolGroups,
    double? Temperature,
    int? MaxTokens,
    IEnumerable<string>? UserPermissions);
