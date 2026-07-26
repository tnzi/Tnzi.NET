namespace Tnzi.Finance.Controllers.Admin;

/// <summary>
/// 报价单管理控制器
/// </summary>
/// <remarks>
/// 生命周期动作（发出 / 接受 / 拒绝 / 关闭）走 <c>.update</c>：它们改变的是本单据
/// 的状态。**转换**额外叠加目标单据的 <c>finance.document.create</c>——转换会凭空
/// 造出一张发票草稿，只有报价权限的人不该能做到这件事。
/// </remarks>
[Route("admin/finance/estimates")]
[DefaultController]
[ApiAuthorize(PermissionName = "finance.estimate.view")]
public class DefaultFinanceEstimateAdminController : ApiAdminControllerBase
{
    private readonly IEstimateService _service;

    public DefaultFinanceEstimateAdminController(IEstimateService service)
    {
        _service = Check.NotNull(service);
    }

    protected IEstimateService Service => _service;

    /// <summary>
    /// 分页查询报价单
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<EstimateDto>>> GetPaged([FromQuery] EstimateQueryDto query)
    {
        var result = await _service.GetPagedAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取报价单
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<EstimateDto>> Get(Guid id)
    {
        var result = await _service.GetAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建报价单草稿
    /// </summary>
    [HttpPost]
    [ApiAuthorize(PermissionName = "finance.estimate.create")]
    public virtual async Task<ApiResult<EstimateDto>> Create([FromBody] CreateEstimateDto request)
    {
        var result = await _service.CreateDraftAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新报价单
    /// </summary>
    [HttpPut("{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.estimate.update")]
    public virtual async Task<ApiResult<EstimateDto>> Update(Guid id, [FromBody] CreateEstimateDto request)
    {
        var result = await _service.UpdateAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除报价单草稿
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.estimate.delete")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await _service.DeleteDraftAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 发出报价单（分配编号）
    /// </summary>
    [HttpPost("{id:guid}/send")]
    [ApiAuthorize(PermissionName = "finance.estimate.update")]
    public virtual async Task<ApiResult<EstimateDto>> Send(Guid id)
    {
        var result = await _service.SendAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 客户接受报价
    /// </summary>
    [HttpPost("{id:guid}/accept")]
    [ApiAuthorize(PermissionName = "finance.estimate.update")]
    public virtual async Task<ApiResult<EstimateDto>> Accept(Guid id)
    {
        var result = await _service.AcceptAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 客户拒绝报价
    /// </summary>
    [HttpPost("{id:guid}/decline")]
    [ApiAuthorize(PermissionName = "finance.estimate.update")]
    public virtual async Task<ApiResult<EstimateDto>> Decline(Guid id)
    {
        var result = await _service.DeclineAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 关闭报价单
    /// </summary>
    [HttpPost("{id:guid}/close")]
    [ApiAuthorize(PermissionName = "finance.estimate.update")]
    public virtual async Task<ApiResult<EstimateDto>> Close(Guid id)
    {
        var result = await _service.CloseAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 转为发票草稿
    /// </summary>
    [HttpPost("{id:guid}/convert-to-invoice")]
    [ApiAuthorize(PermissionName = "finance.estimate.update")]
    [ApiAuthorize(PermissionName = "finance.document.create")]
    public virtual async Task<ApiResult<ConvertOfferResultDto>> ConvertToInvoice(Guid id, [FromBody] ConvertOfferDto request)
    {
        var result = await _service.ConvertToInvoiceAsync(id, request);
        return result.ToApiResult();
    }
}
