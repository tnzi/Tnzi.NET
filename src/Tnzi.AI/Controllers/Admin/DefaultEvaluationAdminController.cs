namespace Tnzi.AI.Controllers.Admin;

/// <summary>
/// 评估运行管理控制器 - 提供评估管理、批量评估、趋势分析和版本比较
/// </summary>
[DefaultController]
[Route("admin/ai/evaluations")]
[ApiAuthorize(PermissionName = "ai.evaluation.view")]
public class DefaultEvaluationAdminController : ApiAdminControllerBase
{
    protected readonly IEvaluationService EvaluationService;
    protected readonly IEvaluationMetricsService MetricsService;

    public DefaultEvaluationAdminController(IEvaluationService evaluationService, IEvaluationMetricsService metricsService)
    {
        EvaluationService = Check.NotNull(evaluationService);
        MetricsService = Check.NotNull(metricsService);
    }

    /// <summary>
    /// 获取评估运行详情
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<EvaluationRunDetailDto>> GetById(Guid id)
    {
        var result = await EvaluationService.GetByIdAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 分页查询评估运行列表
    /// </summary>
    [HttpPost("query")]
    public virtual async Task<ApiResult<IPagedList<EvaluationRunDto>>> GetList([FromBody] EvaluationRunQueryDto query)
    {
        var result = await EvaluationService.GetListAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除评估运行记录
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ApiAuthorize(PermissionName = "ai.evaluation.delete")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await EvaluationService.DeleteAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建并执行评估运行
    /// </summary>
    [HttpPost("run")]
    [ApiAuthorize(PermissionName = "ai.evaluation.execute")]
    public virtual async Task<ApiResult<EvaluationRunDetailDto>> CreateAndRun([FromBody] CreateEvaluationRunDto dto, CancellationToken ct)
    {
        var result = await EvaluationService.CreateAndRunAsync(dto, ct);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量评估多个 Agent/版本
    /// </summary>
    [HttpPost("batch")]
    [ApiAuthorize(PermissionName = "ai.evaluation.execute")]
    public virtual async Task<ApiResult<BatchEvaluationResultDto>> RunBatch([FromBody] BatchEvaluationDto dto, CancellationToken ct)
    {
        var result = await EvaluationService.RunBatchAsync(dto, ct);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取评分趋势（最近 N 次运行）
    /// </summary>
    [HttpGet("agents/{agentId:guid}/trend")]
    public virtual async Task<ApiResult<EvaluationTrendDto>> GetTrend(Guid agentId, [FromQuery] int lastNRuns = 10, CancellationToken ct = default)
    {
        var result = await MetricsService.GetTrendAsync(agentId, lastNRuns, ct);
        return result.ToApiResult();
    }

    /// <summary>
    /// A/B 版本比较
    /// </summary>
    [HttpGet("agents/{agentId:guid}/compare")]
    public virtual async Task<ApiResult<VersionComparisonDto>> CompareVersions(Guid agentId, [FromQuery] int versionA, [FromQuery] int versionB, CancellationToken ct = default)
    {
        var result = await MetricsService.CompareVersionsAsync(agentId, versionA, versionB, ct);
        return result.ToApiResult();
    }
}
