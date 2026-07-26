namespace Tnzi.AI.Dtos;

/// <summary>
/// 评估运行列表 DTO
/// </summary>
public class EvaluationRunDto
{
    public Guid Id { get; set; }

    /// <summary>关联的 Agent ID</summary>
    public Guid AgentId { get; set; }

    /// <summary>Agent 版本号</summary>
    public int? AgentVersionNumber { get; set; }

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
/// 评估运行详情 DTO - 包含结果 JSON
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

/// <summary>
/// 创建并运行评估 DTO
/// </summary>
public class CreateEvaluationRunDto
{
    /// <summary>目标 Agent ID</summary>
    [Required]
    public Guid AgentId { get; set; }

    /// <summary>Agent 版本号（可选，不指定则使用当前版本）</summary>
    public int? VersionNumber { get; set; }

    /// <summary>评估测试用例列表</summary>
    [Required]
    public List<EvaluationCaseDto> Cases { get; set; } = null!;
}

/// <summary>
/// 评估测试用例 DTO
/// </summary>
public class EvaluationCaseDto
{
    /// <summary>输入内容</summary>
    [Required]
    public string Input { get; set; } = string.Empty;

    /// <summary>期望输出（可选，用于自动评分）</summary>
    public string? ExpectedOutput { get; set; }
}

/// <summary>
/// 批量评估 DTO - 对多个 Agent/版本并行评估
/// </summary>
public class BatchEvaluationDto
{
    /// <summary>评估目标列表（Agent + 可选版本号）</summary>
    [Required]
    public List<BatchEvaluationTargetDto> Targets { get; set; } = null!;

    /// <summary>共享测试用例（所有目标使用同一套用例）</summary>
    [Required]
    public List<EvaluationCaseDto> Cases { get; set; } = null!;
}

/// <summary>
/// 批量评估目标 DTO
/// </summary>
public class BatchEvaluationTargetDto
{
    /// <summary>Agent ID</summary>
    [Required]
    public Guid AgentId { get; set; }

    /// <summary>Agent 版本号（可选）</summary>
    public int? VersionNumber { get; set; }
}

/// <summary>
/// 批量评估结果 DTO
/// </summary>
public class BatchEvaluationResultDto
{
    /// <summary>各目标评估结果</summary>
    public List<EvaluationRunDetailDto> Results { get; set; } = [];

    /// <summary>总耗时</summary>
    public TimeSpan TotalDuration { get; set; }
}

/// <summary>
/// 评估趋势数据点
/// </summary>
public class EvaluationTrendPointDto
{
    /// <summary>评估运行 ID</summary>
    public Guid RunId { get; set; }

    /// <summary>评估日期</summary>
    public DateTime Date { get; set; }

    /// <summary>平均评分</summary>
    public double Score { get; set; }

    /// <summary>通过率</summary>
    public double PassRate { get; set; }
}

/// <summary>
/// 评估趋势 DTO - 展示最近 N 次运行的评分变化
/// </summary>
public class EvaluationTrendDto
{
    /// <summary>Agent ID</summary>
    public Guid AgentId { get; set; }

    /// <summary>趋势数据点列表</summary>
    public List<EvaluationTrendPointDto> Points { get; set; } = [];
}

/// <summary>
/// 版本评估统计
/// </summary>
public class VersionStatsDto
{
    /// <summary>版本号</summary>
    public int VersionNumber { get; set; }

    /// <summary>评估运行数</summary>
    public int RunCount { get; set; }

    /// <summary>平均评分</summary>
    public double AverageScore { get; set; }

    /// <summary>平均通过率</summary>
    public double AveragePassRate { get; set; }

    /// <summary>总用例数</summary>
    public int TotalCases { get; set; }

    /// <summary>总通过数</summary>
    public int TotalPassed { get; set; }
}

/// <summary>
/// 版本比较 DTO - A/B 版本评估比较
/// </summary>
public class VersionComparisonDto
{
    /// <summary>Agent ID</summary>
    public Guid AgentId { get; set; }

    /// <summary>版本 A 统计</summary>
    public VersionStatsDto VersionA { get; set; } = null!;

    /// <summary>版本 B 统计</summary>
    public VersionStatsDto VersionB { get; set; } = null!;

    /// <summary>评分差异（B - A，正值表示 B 更好）</summary>
    public double ScoreDelta { get; set; }

    /// <summary>胜出版本号（评分更高者）</summary>
    public int? Winner { get; set; }
}
