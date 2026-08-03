namespace Tnzi.AI.Services;

/// <summary>
/// Agent 验证服务 - 检查 Agent 配置的完整性和有效性
/// </summary>
public interface IAgentValidationService
{
    /// <summary>
    /// 验证单个 Agent 配置的完整性
    /// </summary>
    /// <param name="agentId">Agent ID</param>
    /// <returns>验证结果（包含各项检查详情）</returns>
    Task<Result<AgentValidationResultDto>> ValidateAsync(Guid agentId);

    /// <summary>
    /// 获取所有已启用 Agent 的健康摘要
    /// </summary>
    /// <returns>健康摘要（总数、健康数、异常数、异常详情）</returns>
    Task<Result<AgentHealthSummaryDto>> GetHealthSummaryAsync();
}
