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
    BudgetExceeded = 2
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
