namespace Tnzi.Finance.Controllers.Admin;

/// <summary>
/// 销售贷项单管理控制器
/// </summary>
[Route("admin/finance/credit-memos")]
[DefaultController]
[ApiAuthorize(PermissionName = "finance.document.view")]
public class DefaultFinanceCreditMemoAdminController : ApiAdminControllerBase
{
    private readonly ICreditMemoService _service;

    public DefaultFinanceCreditMemoAdminController(ICreditMemoService service)
    {
        _service = Check.NotNull(service);
    }

    protected ICreditMemoService Service => _service;

    /// <summary>
    /// 分页查询销售贷项单
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<CreditMemoDto>>> GetPaged([FromQuery] CreditMemoQueryDto query)
    {
        var result = await _service.GetPagedAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取销售贷项单
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<CreditMemoDto>> Get(Guid id)
    {
        var result = await _service.GetAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建销售贷项单草稿
    /// </summary>
    [HttpPost]
    [ApiAuthorize(PermissionName = "finance.document.create")]
    public virtual async Task<ApiResult<CreditMemoDto>> Create([FromBody] CreateCreditMemoDto request)
    {
        var result = await _service.CreateDraftAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新销售贷项单草稿
    /// </summary>
    [HttpPut("{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.document.update")]
    public virtual async Task<ApiResult<CreditMemoDto>> Update(Guid id, [FromBody] CreateCreditMemoDto request)
    {
        var result = await _service.UpdateDraftAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除销售贷项单草稿
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.document.delete")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await _service.DeleteDraftAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 过账销售贷项单
    /// </summary>
    [HttpPost("{id:guid}/post")]
    [ApiAuthorize(PermissionName = "finance.document.update")]
    public virtual async Task<ApiResult<CreditMemoDto>> Post(Guid id)
    {
        var result = await _service.PostAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 作废销售贷项单
    /// </summary>
    [HttpPost("{id:guid}/void")]
    [ApiAuthorize(PermissionName = "finance.document.update")]
    public virtual async Task<ApiResult<CreditMemoDto>> Void(Guid id)
    {
        var result = await _service.VoidAsync(id);
        return result.ToApiResult();
    }
}
