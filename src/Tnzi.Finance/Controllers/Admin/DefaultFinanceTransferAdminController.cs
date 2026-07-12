namespace Tnzi.Finance.Controllers.Admin;

/// <summary>
/// 资金划转单管理控制器
/// </summary>
[Route("admin/finance/transfers")]
[DefaultController]
[ApiAuthorize(PermissionName = "finance.document.view")]
public class DefaultFinanceTransferAdminController : ApiAdminControllerBase
{
    private readonly ITransferService _service;

    public DefaultFinanceTransferAdminController(ITransferService service)
    {
        _service = Check.NotNull(service);
    }

    protected ITransferService Service => _service;

    /// <summary>
    /// 分页查询划转单
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<TransferDto>>> GetPaged([FromQuery] TransferQueryDto query)
    {
        var result = await _service.GetPagedAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取划转单
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<TransferDto>> Get(Guid id)
    {
        var result = await _service.GetAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建划转单草稿
    /// </summary>
    [HttpPost]
    [ApiAuthorize(PermissionName = "finance.document.create")]
    public virtual async Task<ApiResult<TransferDto>> Create([FromBody] CreateTransferDto request)
    {
        var result = await _service.CreateDraftAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新划转单草稿
    /// </summary>
    [HttpPut("{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.document.update")]
    public virtual async Task<ApiResult<TransferDto>> Update(Guid id, [FromBody] CreateTransferDto request)
    {
        var result = await _service.UpdateDraftAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除划转单草稿
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.document.delete")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await _service.DeleteDraftAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 过账划转单
    /// </summary>
    [HttpPost("{id:guid}/post")]
    [ApiAuthorize(PermissionName = "finance.document.update")]
    public virtual async Task<ApiResult<TransferDto>> Post(Guid id)
    {
        var result = await _service.PostAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 作废划转单
    /// </summary>
    [HttpPost("{id:guid}/void")]
    [ApiAuthorize(PermissionName = "finance.document.update")]
    public virtual async Task<ApiResult<TransferDto>> Void(Guid id)
    {
        var result = await _service.VoidAsync(id);
        return result.ToApiResult();
    }
}
