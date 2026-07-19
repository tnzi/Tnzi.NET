namespace Tnzi.Finance.Controllers.Admin;

/// <summary>
/// 往来方银行账户（remit-to）管理控制器
/// </summary>
[Route("admin/finance/party-bank-accounts")]
[DefaultController]
[ApiAuthorize(PermissionName = "finance.partyBank.view")]
public class DefaultFinancePartyBankAccountAdminController : ApiAdminControllerBase
{
    private readonly IPartyBankAccountService _service;

    public DefaultFinancePartyBankAccountAdminController(IPartyBankAccountService service)
    {
        _service = Check.NotNull(service);
    }

    protected IPartyBankAccountService Service => _service;

    /// <summary>分页查询往来方银行账户</summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<PartyBankAccountDto>>> GetPaged([FromQuery] PartyBankAccountQueryDto query)
    {
        var result = await _service.GetPagedAsync(query);
        return result.ToApiResult();
    }

    /// <summary>按往来方列出其银行账户</summary>
    [HttpGet("by-party")]
    public virtual async Task<ApiResult<List<PartyBankAccountDto>>> GetByParty([FromQuery] FinancePartyType partyType, [FromQuery] Guid partyId)
    {
        var result = await _service.GetByPartyAsync(partyType, partyId);
        return result.ToApiResult();
    }

    /// <summary>获取往来方银行账户</summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<PartyBankAccountDto>> Get(Guid id)
    {
        var result = await _service.GetAsync(id);
        return result.ToApiResult();
    }

    /// <summary>创建往来方银行账户</summary>
    [HttpPost]
    [ApiAuthorize(PermissionName = "finance.partyBank.create")]
    public virtual async Task<ApiResult<PartyBankAccountDto>> Create([FromBody] SavePartyBankAccountDto request)
    {
        var result = await _service.CreateAsync(request);
        return result.ToApiResult();
    }

    /// <summary>更新往来方银行账户</summary>
    [HttpPut("{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.partyBank.update")]
    public virtual async Task<ApiResult<PartyBankAccountDto>> Update(Guid id, [FromBody] SavePartyBankAccountDto request)
    {
        var result = await _service.UpdateAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>设为默认账户</summary>
    [HttpPost("{id:guid}/default")]
    [ApiAuthorize(PermissionName = "finance.partyBank.update")]
    public virtual async Task<ApiResult<PartyBankAccountDto>> SetDefault(Guid id)
    {
        var result = await _service.SetDefaultAsync(id);
        return result.ToApiResult();
    }

    /// <summary>删除往来方银行账户</summary>
    [HttpDelete("{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.partyBank.delete")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await _service.DeleteAsync(id);
        return result.ToApiResult();
    }
}
