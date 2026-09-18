namespace Tnzi.AI.Evaluation;

/// <summary>
/// Agent 评估器接口 - 用于评估 Agent 的响应质量
/// </summary>
[ExperimentalApi(Reason = "Agent evaluation is in preview")]
public interface IAgentEvaluator
{
    /// <summary>
    /// 评估单个测试用例
    /// </summary>
    Task<EvaluationResult> EvaluateAsync(EvaluationCase evaluationCase, CancellationToken ct = default);

    /// <summary>
    /// 批量评估多个测试用例
    /// </summary>
    Task<EvaluationSummary> EvaluateBatchAsync(List<EvaluationCase> cases, CancellationToken ct = default);
}

/// <summary>
/// 评估用例
/// </summary>
public class EvaluationCase
{
    /// <summary>
    /// 用例 ID（默认自动生成）
    /// </summary>
    public string CaseId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 输入内容
    /// </summary>
    public string Input { get; set; } = string.Empty;

    /// <summary>
    /// 期望输出（可选，用于自动评分）
    /// </summary>
    public string? ExpectedOutput { get; set; }

    /// <summary>
    /// 被评估的 Agent（为空则跑默认提供商的裸模型，不带任何 Agent 配置）
    /// </summary>
    /// <remarks>
    /// 评估器必须真的跑这个 Agent：此前这四个字段不存在，AgentId / VersionNumber 只写进评估运行记录，
    /// 用例跑的是默认提供商的裸模型，通过率与版本对比对目标 Agent 毫无意义。
    /// </remarks>
    public Guid? AgentId { get; set; }

    /// <summary>
    /// 钉住的 Agent 版本号（空则跑当前配置）；评估器据此加载该版本的配置快照执行
    /// </summary>
    public int? VersionNumber { get; set; }

    /// <summary>
    /// 提供商覆盖（可选）
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// 模型覆盖（可选）
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// 扩展元数据
    /// </summary>
    public Dictionary<string, object?> Metadata { get; set; } = new();
}

/// <summary>
/// 评估结果
/// </summary>
public class EvaluationResult
{
    /// <summary>
    /// 用例 ID
    /// </summary>
    public string CaseId { get; set; } = string.Empty;

    /// <summary>
    /// 实际输出
    /// </summary>
    public string ActualOutput { get; set; } = string.Empty;

    /// <summary>
    /// 是否通过
    /// </summary>
    public bool Passed { get; set; }

    /// <summary>
    /// 评分（0.0 ~ 1.0）
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// 评估原因说明
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// 评估耗时
    /// </summary>
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// 评估汇总
/// </summary>
public class EvaluationSummary
{
    /// <summary>
    /// 所有评估结果
    /// </summary>
    public List<EvaluationResult> Results { get; set; } = [];

    /// <summary>
    /// 总用例数
    /// </summary>
    public int TotalCases { get; set; }

    /// <summary>
    /// 通过用例数
    /// </summary>
    public int PassedCases { get; set; }

    /// <summary>
    /// 通过率
    /// </summary>
    public double PassRate => TotalCases > 0 ? (double)PassedCases / TotalCases : 0;

    /// <summary>
    /// 平均评分
    /// </summary>
    public double AverageScore => Results.Count > 0 ? Results.Average(r => r.Score) : 0;

    /// <summary>
    /// 总耗时
    /// </summary>
    public TimeSpan TotalDuration { get; set; }
}
