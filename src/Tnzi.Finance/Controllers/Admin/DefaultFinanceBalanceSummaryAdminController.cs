namespace Tnzi.Finance.Controllers.Admin;

/// <summary>
/// 科目期间余额汇总运维控制器
/// </summary>
/// <remarks>
/// verify（只读校验）走类级 <c>finance.balanceSummary.view</c>；rebuild（全量重建）叠加方法级
/// <c>finance.balanceSummary.execute</c>。二者均 POST（有副作用：verify 取行锁、rebuild 改写桶）。
/// </remarks>
[Route("admin/finance/balance-summary")]
[DefaultController]
[ApiAuthorize(PermissionName = "finance.balanceSummary.view")]
public class DefaultFinanceBalanceSummaryAdminController : ApiAdminControllerBase
{
    private readonly IBalanceSummaryService _service;

    public DefaultFinanceBalanceSummaryAdminController(IBalanceSummaryService service)
    {
        _service = Check.NotNull(service);
    }

    protected IBalanceSummaryService Service => _service;

    /// <summary>
    /// 校验汇总桶与总账一致性（只读诊断，返回 Missing/Extra/Mismatch 差异，不修复）
    /// </summary>
    [HttpPost("verify")]
    public virtual async Task<ApiResult<BalanceSummaryVerifyDto>> Verify()
    {
        var result = await _service.VerifyAsync();
        return result.ToApiResult();
    }

    /// <summary>
    /// 全量重建当前租户的汇总桶（存量账本启用 UseBalanceSummary 前/verify 出差异后运行）
    /// </summary>
    [HttpPost("rebuild")]
    [ApiAuthorize(PermissionName = "finance.balanceSummary.execute")]
    public virtual async Task<ApiResult<BalanceSummaryRebuildDto>> Rebuild()
    {
        var result = await _service.RebuildAsync();
        return result.ToApiResult();
    }
}
