namespace Tnzi.Finance.Controllers.Admin;

/// <summary>
/// 银行对账管理控制器
/// </summary>
[Route("admin/finance/reconciliations")]
[DefaultController]
[ApiAuthorize(PermissionName = "finance.reconciliation.view")]
public class DefaultFinanceReconciliationAdminController : ApiAdminControllerBase
{
    private readonly IReconciliationService _service;

    public DefaultFinanceReconciliationAdminController(IReconciliationService service)
    {
        _service = Check.NotNull(service);
    }

    protected IReconciliationService Service => _service;

    /// <summary>
    /// 分页查询对账
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<ReconciliationDto>>> GetPaged([FromQuery] ReconciliationQueryDto query)
    {
        var result = await _service.GetPagedAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取对账（含累计已勾选净额与差额）
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<ReconciliationDto>> Get(Guid id)
    {
        var result = await _service.GetAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建对账草稿
    /// </summary>
    [HttpPost]
    [ApiAuthorize(PermissionName = "finance.reconciliation.create")]
    public virtual async Task<ApiResult<ReconciliationDto>> Create([FromBody] CreateReconciliationDto request)
    {
        var result = await _service.CreateDraftAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新对账草稿头字段
    /// </summary>
    [HttpPut("{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.reconciliation.update")]
    public virtual async Task<ApiResult<ReconciliationDto>> Update(Guid id, [FromBody] CreateReconciliationDto request)
    {
        var result = await _service.UpdateDraftAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除对账草稿（勾选行级联硬删）
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.reconciliation.delete")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await _service.DeleteDraftAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 勾选工作区（已勾选 + 候选行 + 实时差额）
    /// </summary>
    [HttpGet("{id:guid}/worksheet")]
    public virtual async Task<ApiResult<ReconciliationWorksheetDto>> GetWorksheet(Guid id)
    {
        var result = await _service.GetWorksheetAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 全量替换勾选行
    /// </summary>
    [HttpPut("{id:guid}/lines")]
    [ApiAuthorize(PermissionName = "finance.reconciliation.update")]
    public virtual async Task<ApiResult<ReconciliationWorksheetDto>> SetLines(Guid id, [FromBody] SetReconciliationLinesDto request)
    {
        var result = await _service.SetLinesAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 完成对账（差额须为 0；完成后锁定）
    /// </summary>
    [HttpPost("{id:guid}/complete")]
    [ApiAuthorize(PermissionName = "finance.reconciliation.update")]
    public virtual async Task<ApiResult<ReconciliationDto>> Complete(Guid id)
    {
        var result = await _service.CompleteAsync(id);
        return result.ToApiResult();
    }
}
