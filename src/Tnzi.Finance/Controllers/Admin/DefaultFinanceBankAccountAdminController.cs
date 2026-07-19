namespace Tnzi.Finance.Controllers.Admin;

/// <summary>
/// 银行账户档案管理控制器
/// </summary>
[Route("admin/finance/bank-accounts")]
[DefaultController]
[ApiAuthorize(PermissionName = "finance.bankAccount.view")]
public class DefaultFinanceBankAccountAdminController : ApiAdminControllerBase
{
    private readonly IBankAccountService _service;

    public DefaultFinanceBankAccountAdminController(IBankAccountService service)
    {
        _service = Check.NotNull(service);
    }

    protected IBankAccountService Service => _service;

    /// <summary>读取本面的部署能力（能否存储账号明文）</summary>
    [HttpGet("capabilities")]
    public virtual async Task<ApiResult<BankAccountCapabilitiesDto>> GetCapabilities()
    {
        var result = await _service.GetCapabilitiesAsync();
        return result.ToApiResult();
    }

    /// <summary>分页查询银行账户档案</summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<BankAccountDto>>> GetPaged([FromQuery] BankAccountQueryDto query)
    {
        var result = await _service.GetPagedAsync(query);
        return result.ToApiResult();
    }

    /// <summary>获取银行账户档案</summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<BankAccountDto>> Get(Guid id)
    {
        var result = await _service.GetAsync(id);
        return result.ToApiResult();
    }

    /// <summary>创建银行账户档案</summary>
    [HttpPost]
    [ApiAuthorize(PermissionName = "finance.bankAccount.create")]
    public virtual async Task<ApiResult<BankAccountDto>> Create([FromBody] CreateBankAccountDto request)
    {
        var result = await _service.CreateAsync(request);
        return result.ToApiResult();
    }

    /// <summary>更新银行账户档案</summary>
    [HttpPut("{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.bankAccount.update")]
    public virtual async Task<ApiResult<BankAccountDto>> Update(Guid id, [FromBody] UpdateBankAccountDto request)
    {
        var result = await _service.UpdateAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>设置下一张支票号</summary>
    [HttpPut("{id:guid}/next-check-number")]
    [ApiAuthorize(PermissionName = "finance.bankAccount.update")]
    public virtual async Task<ApiResult<BankAccountDto>> SetNextCheckNumber(Guid id, [FromBody] SetNextCheckNumberDto request)
    {
        var result = await _service.SetNextCheckNumberAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>删除银行账户档案</summary>
    [HttpDelete("{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.bankAccount.delete")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await _service.DeleteAsync(id);
        return result.ToApiResult();
    }
}
