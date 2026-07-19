namespace Tnzi.Finance.Controllers.Admin;

/// <summary>
/// 期末汇兑重估管理控制器
/// </summary>
/// <remarks>
/// preview（只读预览）走类级 <c>finance.revaluation.view</c>；run（过账）叠加方法级
/// <c>finance.revaluation.execute</c>。历史 = journal-entries 按 SourceType = "Revaluation" 过滤。
/// </remarks>
[Route("admin/finance/revaluations")]
[DefaultController]
[ApiAuthorize(PermissionName = "finance.revaluation.view")]
public class DefaultFinanceRevaluationAdminController : ApiAdminControllerBase
{
    private readonly IRevaluationService _service;

    public DefaultFinanceRevaluationAdminController(IRevaluationService service)
    {
        _service = Check.NotNull(service);
    }

    protected IRevaluationService Service => _service;

    /// <summary>
    /// 预览期末重估（不过账；逐科目调整与净额）
    /// </summary>
    [HttpPost("preview")]
    public virtual async Task<ApiResult<RevaluationPreviewDto>> Preview([FromBody] RunRevaluationDto request)
    {
        var result = await _service.PreviewAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 运行期末重估（过账一张汇总凭证；增量全 0 时幂等 no-op）
    /// </summary>
    [HttpPost("run")]
    [ApiAuthorize(PermissionName = "finance.revaluation.execute")]
    public virtual async Task<ApiResult<RevaluationPreviewDto>> Run([FromBody] RunRevaluationDto request)
    {
        var result = await _service.RunAsync(request);
        return result.ToApiResult();
    }
}
