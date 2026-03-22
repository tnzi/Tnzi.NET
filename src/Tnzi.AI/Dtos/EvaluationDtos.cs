namespace Tnzi.AI.Dtos;

/// <summary>
/// 评估运行列表 DTO
/// </summary>
public class EvaluationRunDto
{
    public Guid Id { get; set; }

    /// <summary>关联的 Agent ID</summary>
    public Guid AgentId { get; set; }

    /// <summary>总用例数</summary>
    public int CaseCount { get; set; }

    /// <summary>通过用例数</summary>
    public int PassedCount { get; set; }

    /// <summary>平均评分（0.0 ~ 1.0）</summary>
    public double AverageScore { get; set; }

    /// <summary>运行状态</summary>
    public EvaluationRunStatus Status { get; set; }

    /// <summary>评估耗时</summary>
    public TimeSpan Duration { get; set; }

    /// <summary>创建时间</summary>
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 评估运行详情 DTO — 包含结果 JSON
/// </summary>
public class EvaluationRunDetailDto : EvaluationRunDto
{
    /// <summary>评估结果 JSON 序列化</summary>
    public string ResultsJson { get; set; } = string.Empty;
}

/// <summary>
/// 评估运行分页查询 DTO
/// </summary>
public class EvaluationRunQueryDto : PagedQueryDto
{
    /// <summary>按 Agent ID 过滤</summary>
    public Guid? AgentId { get; set; }

    /// <summary>按运行状态过滤</summary>
    public EvaluationRunStatus? Status { get; set; }
}
