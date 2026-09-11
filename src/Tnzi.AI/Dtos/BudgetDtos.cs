namespace Tnzi.AI.Dtos;

/// <summary>
/// 预算检查状态
/// </summary>
public enum BudgetStatus
{
    /// <summary>预算充足</summary>
    WithinBudget = 0,

    /// <summary>已达预警阈值</summary>
    WarningThreshold = 1,

    /// <summary>预算已超限</summary>
    BudgetExceeded = 2,

    /// <summary>
    /// 无法判定 —— 预算已启用，但本周期有用量而<b>没有任何一条记下了成本</b>，
    /// 聚合出来的 0 美元是「量不出来」而不是「没花钱」。
    /// </summary>
    /// <remarks>
    /// 典型成因：只开了 <c>AI:Budget:Enabled</c> 而没开 <c>AI:CostTracking:Enabled</c>，
    /// 或开了却一条费率（<c>ModelCosts</c> / <c>DefaultCostRate</c>）都没配。
    /// 此状态下请求仍然放行（<c>IsAllowed=true</c>）：预算是 advisory 管控，
    /// 一个勾选框不该让所有 AI 请求当场停摆。要拦死请由宿主按本状态自行决定。
    /// </remarks>
    Indeterminate = 3
}

/// <summary>
/// 预算检查结果
/// </summary>
public class BudgetCheckResult
{
    /// <summary>是否允许继续（未超限）</summary>
    public bool IsAllowed { get; set; }

    /// <summary>当前状态</summary>
    public BudgetStatus Status { get; set; }

    /// <summary>当前周期已花费（美元）</summary>
    public decimal CurrentSpendUsd { get; set; }

    /// <summary>预算上限（美元）</summary>
    public decimal BudgetLimitUsd { get; set; }

    /// <summary>使用率（0-1）</summary>
    public double UsagePercentage { get; set; }

    /// <summary>拒绝/预警原因</summary>
    public string? Reason { get; set; }
}

/// <summary>
/// 预算摘要
/// </summary>
public class BudgetSummaryDto
{
    /// <summary>当前周期开始时间</summary>
    public DateTime PeriodStart { get; set; }

    /// <summary>当前周期结束时间</summary>
    public DateTime PeriodEnd { get; set; }

    /// <summary>当前周期已花费（美元）</summary>
    public decimal CurrentSpendUsd { get; set; }

    /// <summary>预算上限（美元）</summary>
    public decimal BudgetLimitUsd { get; set; }

    /// <summary>使用率（0-1）</summary>
    public double UsagePercentage { get; set; }

    /// <summary>预算状态</summary>
    public BudgetStatus Status { get; set; }

    /// <summary>按 Agent 分组的花费明细</summary>
    public List<AgentSpendDto> ByAgent { get; set; } = [];
}

/// <summary>
/// 按 Agent 分组的花费
/// </summary>
public class AgentSpendDto
{
    /// <summary>Agent ID</summary>
    public Guid? AgentId { get; set; }

    /// <summary>Agent 名称</summary>
    public string AgentName { get; set; } = string.Empty;

    /// <summary>花费（美元）</summary>
    public decimal SpendUsd { get; set; }

    /// <summary>该 Agent 的预算上限（如配置了 PerAgentBudgets）</summary>
    public decimal? AgentBudgetLimitUsd { get; set; }

    /// <summary>请求数</summary>
    public int RequestCount { get; set; }
}
