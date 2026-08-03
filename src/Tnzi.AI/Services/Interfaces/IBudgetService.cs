namespace Tnzi.AI.Services;

/// <summary>
/// USD 成本预算管理服务。
/// </summary>
/// <remarks>
/// ⚠️ <b>ADVISORY / best-effort，非硬性原子上限。</b>
/// 与 Token 配额（<c>IQuotaService.ReserveQuotaAsync</c> 采用单条 SQL 原子 check-and-decrement，
/// 消除 TOCTOU 竞态）不同，USD 预算检查是基于 UsageLog 聚合 + 进程级内存缓存（默认 60s TTL）实现的，
/// <b>不做 reserve-then-settle</b>。因此并发请求可能在缓存窗口内短暂超支，不应将其当作硬性的、
/// 实时精确的成本闸门，仅作为告警 / 软性管控使用。要收紧竞态窗口可调小 <c>AI:Budget:CacheTtlSeconds</c>。
/// </remarks>
public interface IBudgetService
{
    /// <summary>
    /// 检查预算是否允许继续执行（advisory / best-effort，见接口注释，非原子硬闸门）。
    /// </summary>
    /// <param name="userId">用户 ID（可选）</param>
    /// <param name="tenantId">租户 ID（可选）</param>
    /// <param name="agentId">Agent ID（可选）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>预算检查结果</returns>
    Task<BudgetCheckResult> CheckBudgetAsync(Guid? userId, Guid? tenantId, Guid? agentId, CancellationToken ct = default);

    /// <summary>
    /// 主动失效内存中的花费聚合缓存。
    /// </summary>
    /// <remarks>
    /// 这是一个<b>可选的缓存失效钩子</b>，框架内部并不调用它（花费在 <c>UsageLoggingMiddleware</c>
    /// 写入 <c>UsageLog</c> 后会随下一次缓存过期自动从聚合中体现）。当宿主希望在记账后立即让下一次
    /// <see cref="CheckBudgetAsync"/> 重新从 DB 聚合（而非等待 TTL）时可主动调用。<paramref name="costUsd"/>
    /// 仅用于语义清晰，实现只失效缓存，不做 reserve-then-settle 结算。
    /// </remarks>
    /// <param name="userId">用户 ID（可选）</param>
    /// <param name="tenantId">租户 ID（可选）</param>
    /// <param name="agentId">Agent ID（可选）</param>
    /// <param name="costUsd">本次花费（美元，仅语义参数）</param>
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
