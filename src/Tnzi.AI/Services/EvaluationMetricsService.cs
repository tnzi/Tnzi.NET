namespace Tnzi.AI.Services;

/// <summary>
/// 评估指标分析服务实现 - 趋势分析和版本比较
/// </summary>
public class EvaluationMetricsService : ApplicationService, IEvaluationMetricsService
{
    private readonly IRepository<EvaluationRun, Guid> _repository;

    public EvaluationMetricsService(IServiceProvider serviceProvider, IRepository<EvaluationRun, Guid> repository)
        : base(serviceProvider)
    {
        _repository = Check.NotNull(repository);
    }

    public async Task<Result<EvaluationTrendDto>> GetTrendAsync(Guid agentId, int lastNRuns, CancellationToken ct = default)
    {
        Check.NotEmpty(agentId);

        if (lastNRuns <= 0)
            return Fail<EvaluationTrendDto>("lastNRuns must be greater than 0", 400);

        var runs = await _repository
            .Where(e => e.AgentId == agentId && e.Status == EvaluationRunStatus.Completed)
            .OrderByDescending(e => e.CreationTime)
            .Take(lastNRuns)
            .Select(e => new EvaluationTrendPointDto
            {
                RunId = e.Id,
                Date = e.CreationTime,
                Score = e.AverageScore,
                PassRate = e.CaseCount > 0 ? (double)e.PassedCount / e.CaseCount : 0
            })
            .ToListAsync(ct);

        // 按时间正序排列（从旧到新）
        runs.Reverse();

        return Ok(new EvaluationTrendDto
        {
            AgentId = agentId,
            Points = runs
        });
    }

    public async Task<Result<VersionComparisonDto>> CompareVersionsAsync(Guid agentId, int versionA, int versionB, CancellationToken ct = default)
    {
        Check.NotEmpty(agentId);

        if (versionA == versionB)
            return Fail<VersionComparisonDto>("Version A and B must be different", 400);

        var statsA = await GetVersionStatsAsync(agentId, versionA, ct);
        var statsB = await GetVersionStatsAsync(agentId, versionB, ct);

        if (statsA.RunCount == 0 && statsB.RunCount == 0)
            return Fail<VersionComparisonDto>("No evaluation runs found for either version", 404, ErrorCodes.EvaluationNotFound);

        var scoreDelta = statsB.AverageScore - statsA.AverageScore;

        int? winner = null;
        if (statsA.RunCount > 0 && statsB.RunCount > 0)
        {
            if (Math.Abs(scoreDelta) > 0.001)
                winner = scoreDelta > 0 ? versionB : versionA;
        }

        return Ok(new VersionComparisonDto
        {
            AgentId = agentId,
            VersionA = statsA,
            VersionB = statsB,
            ScoreDelta = scoreDelta,
            Winner = winner
        });
    }

    private async Task<VersionStatsDto> GetVersionStatsAsync(Guid agentId, int versionNumber, CancellationToken ct)
    {
        // 投影查询：只加载聚合所需的数字字段，避免读取完整实体（含 ResultsJson 等大字段）
        var projections = await _repository
            .Where(e => e.AgentId == agentId
                && e.AgentVersionNumber == versionNumber
                && e.Status == EvaluationRunStatus.Completed)
            .Select(e => new { e.AverageScore, e.CaseCount, e.PassedCount })
            .ToListAsync(ct);

        if (projections.Count == 0)
        {
            return new VersionStatsDto
            {
                VersionNumber = versionNumber,
                RunCount = 0,
                AverageScore = 0,
                AveragePassRate = 0,
                TotalCases = 0,
                TotalPassed = 0
            };
        }

        var totalCases = projections.Sum(r => r.CaseCount);
        var totalPassed = projections.Sum(r => r.PassedCount);

        return new VersionStatsDto
        {
            VersionNumber = versionNumber,
            RunCount = projections.Count,
            AverageScore = projections.Average(r => r.AverageScore),
            AveragePassRate = totalCases > 0 ? (double)totalPassed / totalCases : 0,
            TotalCases = totalCases,
            TotalPassed = totalPassed
        };
    }
}
