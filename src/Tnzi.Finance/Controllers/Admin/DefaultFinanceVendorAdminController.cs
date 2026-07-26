namespace Tnzi.Finance.Controllers.Admin;

/// <summary>
/// 供应商管理控制器
/// </summary>
[Route("admin/finance/vendors")]
[DefaultController]
[ApiAuthorize(PermissionName = "finance.vendor.view")]
public class DefaultFinanceVendorAdminController : ApiAdminControllerBase
{
    private readonly IVendorService _vendorService;

    private readonly IPartyLedgerService _partyLedgerService;

    public DefaultFinanceVendorAdminController(IVendorService vendorService, IPartyLedgerService partyLedgerService)
    {
        _partyLedgerService = Check.NotNull(partyLedgerService);
        _vendorService = Check.NotNull(vendorService);
    }

    protected IVendorService VendorService => _vendorService;

    /// <summary>
    /// 分页查询供应商
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<VendorDto>>> GetPaged([FromQuery] VendorQueryDto query)
    {
        var result = await _vendorService.GetPagedAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取供应商
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<VendorDto>> Get(Guid id)
    {
        var result = await _vendorService.GetAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建供应商
    /// </summary>
    [HttpPost]
    [ApiAuthorize(PermissionName = "finance.vendor.create")]
    public virtual async Task<ApiResult<VendorDto>> Create([FromBody] CreateVendorDto request)
    {
        var result = await _vendorService.CreateAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新供应商
    /// </summary>
    [HttpPut("{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.vendor.update")]
    public virtual async Task<ApiResult<VendorDto>> Update(Guid id, [FromBody] UpdateVendorDto request)
    {
        var result = await _vendorService.UpdateAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除供应商
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.vendor.delete")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await _vendorService.DeleteAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 概览数字：未清 / 逾期 / 账龄分桶 / 期间发生额。
    /// </summary>
    /// <remarks>
    /// 未清与分桶与账龄报表**同源**，因此本页显示的余额与账龄报表逐分相等；
    /// 呈现端不要再拿分页列表自己求和（那只加得到当前一页）。
    /// </remarks>
    [HttpGet("{id}/summary")]
    public virtual async Task<ApiResult<PartyLedgerSummaryDto>> GetSummary(
        Guid id, [FromQuery] DateTime? asOf, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var result = await _partyLedgerService.GetSummaryAsync(FinancePartyType.Vendor, id, asOf, from, to);
        return result.ToApiResult();
    }

    /// <summary>交易流水（跨单据类型，按单据日期倒序）。</summary>
    [HttpGet("{id}/transactions")]
    public virtual async Task<ApiResult<IPagedList<PartyLedgerEntryDto>>> GetTransactions(
        Guid id, [FromQuery] PartyLedgerQueryDto query)
    {
        var result = await _partyLedgerService.GetTransactionsAsync(FinancePartyType.Vendor, id, query);
        return result.ToApiResult();
    }
}
