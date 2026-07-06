namespace Tnzi.Finance.Controllers.Admin;

/// <summary>
/// 采购账单管理控制器
/// </summary>
[Route("admin/finance/bills")]
[DefaultController]
[ApiAuthorize(PermissionName = "finance.document.view")]
public class DefaultFinanceBillAdminController : ApiAdminControllerBase
{
    private readonly IBillService _service;

    public DefaultFinanceBillAdminController(IBillService service)
    {
        _service = Check.NotNull(service);
    }

    protected IBillService Service => _service;

    /// <summary>
    /// 分页查询采购账单
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<BillDto>>> GetPaged([FromQuery] BillQueryDto query)
    {
        var result = await _service.GetPagedAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取采购账单
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<BillDto>> Get(Guid id)
    {
        var result = await _service.GetAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建采购账单草稿
    /// </summary>
    [HttpPost]
    public virtual async Task<ApiResult<BillDto>> Create([FromBody] CreateBillDto request)
    {
        var result = await _service.CreateDraftAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新采购账单草稿
    /// </summary>
    [HttpPut("{id:guid}")]
    public virtual async Task<ApiResult<BillDto>> Update(Guid id, [FromBody] CreateBillDto request)
    {
        var result = await _service.UpdateDraftAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除采购账单草稿
    /// </summary>
    [HttpDelete("{id:guid}")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await _service.DeleteDraftAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 过账采购账单
    /// </summary>
    [HttpPost("{id:guid}/post")]
    public virtual async Task<ApiResult<BillDto>> Post(Guid id)
    {
        var result = await _service.PostAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 作废采购账单
    /// </summary>
    [HttpPost("{id:guid}/void")]
    public virtual async Task<ApiResult<BillDto>> Void(Guid id)
    {
        var result = await _service.VoidAsync(id);
        return result.ToApiResult();
    }
}
