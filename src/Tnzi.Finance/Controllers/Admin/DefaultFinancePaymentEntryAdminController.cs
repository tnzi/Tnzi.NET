namespace Tnzi.Finance.Controllers.Admin;

/// <summary>
/// 收付款单管理控制器
/// </summary>
[Route("admin/finance/payments")]
[DefaultController]
[ApiAuthorize(PermissionName = "finance.document.view")]
public class DefaultFinancePaymentEntryAdminController : ApiAdminControllerBase
{
    private readonly IPaymentEntryService _service;

    public DefaultFinancePaymentEntryAdminController(IPaymentEntryService service)
    {
        _service = Check.NotNull(service);
    }

    protected IPaymentEntryService Service => _service;

    /// <summary>
    /// 分页查询收付款单
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<PaymentEntryDto>>> GetPaged([FromQuery] PaymentEntryQueryDto query)
    {
        var result = await _service.GetPagedAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取收付款单
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<PaymentEntryDto>> Get(Guid id)
    {
        var result = await _service.GetAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建收付款单草稿
    /// </summary>
    [HttpPost]
    [ApiAuthorize(PermissionName = "finance.document.create")]
    public virtual async Task<ApiResult<PaymentEntryDto>> Create([FromBody] CreatePaymentEntryDto request)
    {
        var result = await _service.CreateDraftAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新收付款单草稿
    /// </summary>
    [HttpPut("{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.document.update")]
    public virtual async Task<ApiResult<PaymentEntryDto>> Update(Guid id, [FromBody] CreatePaymentEntryDto request)
    {
        var result = await _service.UpdateDraftAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除收付款单草稿
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.document.delete")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await _service.DeleteDraftAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 过账收付款单
    /// </summary>
    [HttpPost("{id:guid}/post")]
    [ApiAuthorize(PermissionName = "finance.document.update")]
    public virtual async Task<ApiResult<PaymentEntryDto>> Post(Guid id)
    {
        var result = await _service.PostAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 外部收款摄取（幂等；网关收款 → 收款单）
    /// </summary>
    /// <remarks>
    /// <c>AutoPost</c>（默认 true）时直接过账，且对同来源的既有草稿补投过账；过账在
    /// <c>{id}/post</c> 上要 <c>.update</c>，故两个码叠加。一个端点一种授权，不随请求体里的
    /// <c>AutoPost</c> 变化，接入凭据按它能做到的最大效果授权。
    /// </remarks>
    [HttpPost("external")]
    [ApiAuthorize(PermissionName = "finance.document.create")]
    [ApiAuthorize(PermissionName = "finance.document.update")]
    public virtual async Task<ApiResult<PaymentEntryDto>> CreateFromExternal([FromBody] ExternalPaymentIngestDto request)
    {
        var result = await _service.CreateFromExternalAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 作废收付款单
    /// </summary>
    [HttpPost("{id:guid}/void")]
    [ApiAuthorize(PermissionName = "finance.document.update")]
    public virtual async Task<ApiResult<PaymentEntryDto>> Void(Guid id)
    {
        var result = await _service.VoidAsync(id);
        return result.ToApiResult();
    }
}
