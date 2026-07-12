namespace Tnzi.Finance.Controllers.Admin;

/// <summary>
/// 结算管理控制器（核销 / 未清单据 / 撤销）
/// </summary>
[Route("admin/finance/settlements")]
[DefaultController]
[ApiAuthorize(PermissionName = "finance.document.view")]
public class DefaultFinanceSettlementAdminController : ApiAdminControllerBase
{
    private readonly ISettlementService _settlementService;

    public DefaultFinanceSettlementAdminController(ISettlementService settlementService)
    {
        _settlementService = Check.NotNull(settlementService);
    }

    protected ISettlementService SettlementService => _settlementService;

    /// <summary>
    /// 查询某单据的核销记录
    /// </summary>
    [HttpGet("applications")]
    public virtual async Task<ApiResult<List<PaymentApplicationDto>>> GetApplications([FromQuery] SettlementDocType docType, [FromQuery] Guid docId)
    {
        var result = await _settlementService.GetApplicationsAsync(docType, docId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取往来方可核销的未清单据
    /// </summary>
    [HttpGet("open-documents")]
    public virtual async Task<ApiResult<List<OpenDocumentDto>>> GetOpenDocuments([FromQuery] FinancePartyType partyType, [FromQuery] Guid partyId)
    {
        var result = await _settlementService.GetOpenDocumentsAsync(partyType, partyId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 核销
    /// </summary>
    [HttpPost("apply")]
    [ApiAuthorize(PermissionName = "finance.document.update")]
    public virtual async Task<ApiResult<List<PaymentApplicationDto>>> Apply([FromBody] ApplySettlementDto request)
    {
        var result = await _settlementService.ApplyAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 撤销核销
    /// </summary>
    [HttpDelete("applications/{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.document.update")]
    public virtual async Task<ApiResult> Unapply(Guid id)
    {
        var result = await _settlementService.UnapplyAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量结算（Pay Bills / Receive Payments）：一组未清单据 → 按（往来方 + 币种）
    /// 生成收付款单并过账 + 自动核销，整体一个事务
    /// </summary>
    [HttpPost("pay")]
    [ApiAuthorize(PermissionName = "finance.document.create")]
    public virtual async Task<ApiResult<BatchPaymentResultDto>> Pay([FromBody] BatchPaymentDto request)
    {
        var result = await _settlementService.PayAsync(request);
        return result.ToApiResult();
    }
}
