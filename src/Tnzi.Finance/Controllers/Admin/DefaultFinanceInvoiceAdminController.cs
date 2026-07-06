namespace Tnzi.Finance.Controllers.Admin;

/// <summary>
/// 销售发票管理控制器
/// </summary>
[Route("admin/finance/invoices")]
[DefaultController]
[ApiAuthorize(PermissionName = "finance.document.view")]
public class DefaultFinanceInvoiceAdminController : ApiAdminControllerBase
{
    private readonly IInvoiceService _service;

    public DefaultFinanceInvoiceAdminController(IInvoiceService service)
    {
        _service = Check.NotNull(service);
    }

    protected IInvoiceService Service => _service;

    /// <summary>
    /// 分页查询销售发票
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<InvoiceDto>>> GetPaged([FromQuery] InvoiceQueryDto query)
    {
        var result = await _service.GetPagedAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取销售发票
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<InvoiceDto>> Get(Guid id)
    {
        var result = await _service.GetAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建销售发票草稿
    /// </summary>
    [HttpPost]
    public virtual async Task<ApiResult<InvoiceDto>> Create([FromBody] CreateInvoiceDto request)
    {
        var result = await _service.CreateDraftAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新销售发票草稿
    /// </summary>
    [HttpPut("{id:guid}")]
    public virtual async Task<ApiResult<InvoiceDto>> Update(Guid id, [FromBody] CreateInvoiceDto request)
    {
        var result = await _service.UpdateDraftAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除销售发票草稿
    /// </summary>
    [HttpDelete("{id:guid}")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await _service.DeleteDraftAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 过账销售发票
    /// </summary>
    [HttpPost("{id:guid}/post")]
    public virtual async Task<ApiResult<InvoiceDto>> Post(Guid id)
    {
        var result = await _service.PostAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 作废销售发票
    /// </summary>
    [HttpPost("{id:guid}/void")]
    public virtual async Task<ApiResult<InvoiceDto>> Void(Guid id)
    {
        var result = await _service.VoidAsync(id);
        return result.ToApiResult();
    }
}
