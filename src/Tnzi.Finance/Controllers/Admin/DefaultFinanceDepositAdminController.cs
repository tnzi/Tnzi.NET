namespace Tnzi.Finance.Controllers.Admin;

/// <summary>
/// 银行存款单管理控制器
/// </summary>
[Route("admin/finance/deposits")]
[DefaultController]
[ApiAuthorize(PermissionName = "finance.document.view")]
public class DefaultFinanceDepositAdminController : ApiAdminControllerBase
{
    private readonly IDepositService _service;

    public DefaultFinanceDepositAdminController(IDepositService service)
    {
        _service = Check.NotNull(service);
    }

    protected IDepositService Service => _service;

    /// <summary>
    /// 分页查询存款单
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<DepositDto>>> GetPaged([FromQuery] DepositQueryDto query)
    {
        var result = await _service.GetPagedAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 列出某科目上尚未进入任何存活存款单的已过账收款（存款单编辑器的候选清单）
    /// </summary>
    [HttpGet("undeposited")]
    public virtual async Task<ApiResult<List<UndepositedReceiptDto>>> GetUndeposited([FromQuery] UndepositedReceiptQueryDto query)
    {
        var result = await _service.GetUndepositedReceiptsAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取存款单（含行）
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<DepositDto>> Get(Guid id)
    {
        var result = await _service.GetAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建存款单草稿
    /// </summary>
    [HttpPost]
    [ApiAuthorize(PermissionName = "finance.document.create")]
    public virtual async Task<ApiResult<DepositDto>> Create([FromBody] CreateDepositDto request)
    {
        var result = await _service.CreateDraftAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新存款单草稿
    /// </summary>
    [HttpPut("{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.document.update")]
    public virtual async Task<ApiResult<DepositDto>> Update(Guid id, [FromBody] CreateDepositDto request)
    {
        var result = await _service.UpdateDraftAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除存款单草稿
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.document.delete")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await _service.DeleteDraftAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 过账存款单
    /// </summary>
    [HttpPost("{id:guid}/post")]
    [ApiAuthorize(PermissionName = "finance.document.update")]
    public virtual async Task<ApiResult<DepositDto>> Post(Guid id)
    {
        var result = await _service.PostAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 作废存款单
    /// </summary>
    [HttpPost("{id:guid}/void")]
    [ApiAuthorize(PermissionName = "finance.document.update")]
    public virtual async Task<ApiResult<DepositDto>> Void(Guid id)
    {
        var result = await _service.VoidAsync(id);
        return result.ToApiResult();
    }
}
