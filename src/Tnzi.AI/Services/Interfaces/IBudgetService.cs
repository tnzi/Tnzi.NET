namespace Tnzi.AI.Services.Interfaces;

/// <summary>
/// USD 成本预算管理服务
/// </summary>
public interface IBudgetService
{
    /// <summary>
    /// 检查预算是否允许继续执行
    /// </summary>
    /// <param name="userId">用户 ID（可选）</param>
    /// <param name="tenantId">租户 ID（可选）</param>
    /// <param name="agentId">Agent ID（可选）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>预算检查结果</returns>
    Task<BudgetCheckResult> CheckBudgetAsync(Guid? userId, Guid? tenantId, Guid? agentId, CancellationToken ct = default);

    /// <summary>
    /// 更新花费记录（UsageLoggingMiddleware 写入 UsageLog 后，预算自动从聚合中体现，此方法用于主动失效缓存）
    /// </summary>
    /// <param name="userId">用户 ID（可选）</param>
    /// <param name="tenantId">租户 ID（可选）</param>
    /// <param name="agentId">Agent ID（可选）</param>
    /// <param name="costUsd">本次花费（美元）</param>
    /// <param name="ct">取消令牌</param>
    Task UpdateSpendAsync(Guid? userId, Guid? tenantId, Guid? agentId, decimal costUsd, CancellationToken ct = default);

    /// <summary>
    /// 获取预算摘要
    /// </summary>
    /// <param name="tenantId">租户 ID（可选）</param>
    /// <param name="startTime">开始时间</param>
    /// <param name="endTime">结束时间</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>预算摘要</returns>
    Task<BudgetSummaryDto> GetSummaryAsync(Guid? tenantId, DateTime startTime, DateTime endTime, CancellationToken ct = default);
}
