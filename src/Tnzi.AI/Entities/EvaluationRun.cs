namespace Tnzi.AI.Entities;

/// <summary>
/// 评估运行实体 — 记录 Agent 评估执行结果
/// </summary>
public class EvaluationRun : CreationAuditedEntity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 关联的 Agent ID
    /// </summary>
    public Guid AgentId { get; set; }

    /// <summary>
    /// 总用例数
    /// </summary>
    public int CaseCount { get; set; }

    /// <summary>
    /// 通过用例数
    /// </summary>
    public int PassedCount { get; set; }

    /// <summary>
    /// 平均评分（0.0 ~ 1.0）
    /// </summary>
    public double AverageScore { get; set; }

    /// <summary>
    /// 运行状态
    /// </summary>
    public EvaluationRunStatus Status { get; set; } = EvaluationRunStatus.Running;

    /// <summary>
    /// 评估结果 JSON 序列化
    /// </summary>
    public string ResultsJson { get; set; } = string.Empty;

    /// <summary>
    /// Agent 版本号（关联 AgentVersion.Version，可选）
    /// </summary>
    public int? AgentVersionNumber { get; set; }

    /// <summary>
    /// 评估耗时
    /// </summary>
    public TimeSpan Duration { get; set; }
}
