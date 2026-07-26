namespace Tnzi.Finance.Controllers.Admin;

/// <summary>
/// 客户管理控制器
/// </summary>
[Route("admin/finance/customers")]
[DefaultController]
[ApiAuthorize(PermissionName = "finance.customer.view")]
public class DefaultFinanceCustomerAdminController : ApiAdminControllerBase
{
    private readonly ICustomerService _customerService;

    private readonly IPartyLedgerService _partyLedgerService;

    public DefaultFinanceCustomerAdminController(ICustomerService customerService, IPartyLedgerService partyLedgerService)
    {
        _partyLedgerService = Check.NotNull(partyLedgerService);
        _customerService = Check.NotNull(customerService);
    }

    protected ICustomerService CustomerService => _customerService;

    /// <summary>
    /// 分页查询客户
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<CustomerDto>>> GetPaged([FromQuery] CustomerQueryDto query)
    {
        var result = await _customerService.GetPagedAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取客户
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<CustomerDto>> Get(Guid id)
    {
        var result = await _customerService.GetAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建客户
    /// </summary>
    [HttpPost]
    [ApiAuthorize(PermissionName = "finance.customer.create")]
    public virtual async Task<ApiResult<CustomerDto>> Create([FromBody] CreateCustomerDto request)
    {
        var result = await _customerService.CreateAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新客户
    /// </summary>
    [HttpPut("{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.customer.update")]
    public virtual async Task<ApiResult<CustomerDto>> Update(Guid id, [FromBody] UpdateCustomerDto request)
    {
        var result = await _customerService.UpdateAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除客户
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.customer.delete")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await _customerService.DeleteAsync(id);
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
        var result = await _partyLedgerService.GetSummaryAsync(FinancePartyType.Customer, id, asOf, from, to);
        return result.ToApiResult();
    }

    /// <summary>交易流水（跨单据类型，按单据日期倒序）。</summary>
    [HttpGet("{id}/transactions")]
    public virtual async Task<ApiResult<IPagedList<PartyLedgerEntryDto>>> GetTransactions(
        Guid id, [FromQuery] PartyLedgerQueryDto query)
    {
        var result = await _partyLedgerService.GetTransactionsAsync(FinancePartyType.Customer, id, query);
        return result.ToApiResult();
    }
}
