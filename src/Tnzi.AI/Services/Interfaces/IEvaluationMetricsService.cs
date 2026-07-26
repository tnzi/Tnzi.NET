namespace Tnzi.AI.Services.Interfaces;

/// <summary>
/// 评估指标分析服务接口 - 提供跨运行趋势和版本比较
/// </summary>
public interface IEvaluationMetricsService
{
    /// <summary>
    /// 获取评分趋势（最近 N 次运行）
    /// </summary>
    Task<Result<EvaluationTrendDto>> GetTrendAsync(Guid agentId, int lastNRuns, CancellationToken ct = default);

    /// <summary>
    /// A/B 版本比较 - 对比两个版本的评估指标
    /// </summary>
    Task<Result<VersionComparisonDto>> CompareVersionsAsync(Guid agentId, int versionA, int versionB, CancellationToken ct = default);
}
