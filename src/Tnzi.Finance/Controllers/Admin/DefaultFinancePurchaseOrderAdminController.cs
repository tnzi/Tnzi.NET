namespace Tnzi.Finance.Controllers.Admin;

/// <summary>
/// 采购订单管理控制器
/// </summary>
/// <remarks>
/// 生命周期动作（发出 / 接受 / 拒绝 / 关闭）走 <c>.update</c>：它们改变的是本单据
/// 的状态。**转换**额外叠加目标单据的 <c>finance.document.create</c>——转换会凭空
/// 造出一张发票草稿，只有报价权限的人不该能做到这件事。
/// </remarks>
[Route("admin/finance/purchase-orders")]
[DefaultController]
[ApiAuthorize(PermissionName = "finance.purchaseOrder.view")]
public class DefaultFinancePurchaseOrderAdminController : ApiAdminControllerBase
{
    private readonly IPurchaseOrderService _service;

    public DefaultFinancePurchaseOrderAdminController(IPurchaseOrderService service)
    {
        _service = Check.NotNull(service);
    }

    protected IPurchaseOrderService Service => _service;

    /// <summary>
    /// 分页查询采购订单
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<PurchaseOrderDto>>> GetPaged([FromQuery] PurchaseOrderQueryDto query)
    {
        var result = await _service.GetPagedAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取采购订单
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<PurchaseOrderDto>> Get(Guid id)
    {
        var result = await _service.GetAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建采购订单草稿
    /// </summary>
    [HttpPost]
    [ApiAuthorize(PermissionName = "finance.purchaseOrder.create")]
    public virtual async Task<ApiResult<PurchaseOrderDto>> Create([FromBody] CreatePurchaseOrderDto request)
    {
        var result = await _service.CreateDraftAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新采购订单
    /// </summary>
    [HttpPut("{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.purchaseOrder.update")]
    public virtual async Task<ApiResult<PurchaseOrderDto>> Update(Guid id, [FromBody] CreatePurchaseOrderDto request)
    {
        var result = await _service.UpdateAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除采购订单草稿
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.purchaseOrder.delete")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await _service.DeleteDraftAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 发出采购订单（分配编号）
    /// </summary>
    [HttpPost("{id:guid}/send")]
    [ApiAuthorize(PermissionName = "finance.purchaseOrder.update")]
    public virtual async Task<ApiResult<PurchaseOrderDto>> Send(Guid id)
    {
        var result = await _service.SendAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 供应商确认订单
    /// </summary>
    [HttpPost("{id:guid}/accept")]
    [ApiAuthorize(PermissionName = "finance.purchaseOrder.update")]
    public virtual async Task<ApiResult<PurchaseOrderDto>> Accept(Guid id)
    {
        var result = await _service.AcceptAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 供应商拒绝订单
    /// </summary>
    [HttpPost("{id:guid}/decline")]
    [ApiAuthorize(PermissionName = "finance.purchaseOrder.update")]
    public virtual async Task<ApiResult<PurchaseOrderDto>> Decline(Guid id)
    {
        var result = await _service.DeclineAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 关闭采购订单
    /// </summary>
    [HttpPost("{id:guid}/close")]
    [ApiAuthorize(PermissionName = "finance.purchaseOrder.update")]
    public virtual async Task<ApiResult<PurchaseOrderDto>> Close(Guid id)
    {
        var result = await _service.CloseAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 转为账单草稿
    /// </summary>
    [HttpPost("{id:guid}/convert-to-bill")]
    [ApiAuthorize(PermissionName = "finance.purchaseOrder.update")]
    [ApiAuthorize(PermissionName = "finance.document.create")]
    public virtual async Task<ApiResult<ConvertOfferResultDto>> ConvertToBill(Guid id, [FromBody] ConvertOfferDto request)
    {
        var result = await _service.ConvertToBillAsync(id, request);
        return result.ToApiResult();
    }
}
