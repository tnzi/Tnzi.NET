namespace Tnzi.Finance.Controllers.Admin;

/// <summary>
/// 科目表管理控制器
/// </summary>
[Route("admin/finance/accounts")]
[DefaultController]
[ApiAuthorize(PermissionName = "finance.account.view")]
public class DefaultFinanceAccountAdminController : ApiAdminControllerBase
{
    private readonly IChartOfAccountsService _accountService;

    public DefaultFinanceAccountAdminController(IChartOfAccountsService accountService)
    {
        _accountService = Check.NotNull(accountService);
    }

    protected IChartOfAccountsService AccountService => _accountService;

    /// <summary>
    /// 分页查询科目
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<AccountDto>>> GetList([FromQuery] AccountQueryDto query)
    {
        var result = await _accountService.GetListAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取科目树
    /// </summary>
    [HttpGet("tree")]
    public virtual async Task<ApiResult<List<AccountTreeDto>>> GetTree([FromQuery] bool includeInactive = false)
    {
        var result = await _accountService.GetTreeAsync(includeInactive);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取科目
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<AccountDto>> Get(Guid id)
    {
        var result = await _accountService.GetAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量读取科目余额（本位币，截至基准日日终）
    /// </summary>
    /// <remarks>
    /// POST-读：科目集经请求体传递（一页科目的 GUID 列表会超出 URL 长度上限），
    /// 不改数据，仅由类级 <c>finance.account.view</c> 把守。
    /// </remarks>
    [HttpPost("balances")]
    public virtual async Task<ApiResult<List<AccountBalanceDto>>> GetBalances([FromBody] GetAccountBalancesDto request)
    {
        var result = await _accountService.GetBalancesAsync(
            request.AccountIds ?? [], request.AsOf ?? DateTime.UtcNow);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建科目
    /// </summary>
    [HttpPost]
    [ApiAuthorize(PermissionName = "finance.account.create")]
    public virtual async Task<ApiResult<AccountDto>> Create([FromBody] CreateAccountDto request)
    {
        var result = await _accountService.CreateAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新科目
    /// </summary>
    [HttpPut("{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.account.update")]
    public virtual async Task<ApiResult<AccountDto>> Update(Guid id, [FromBody] UpdateAccountDto request)
    {
        var result = await _accountService.UpdateAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除科目
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.account.delete")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await _accountService.DeleteAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 播种默认科目表（仅当科目表为空）
    /// </summary>
    [HttpPost("seed-default")]
    [ApiAuthorize(PermissionName = "finance.account.create")]
    public virtual async Task<ApiResult<int>> SeedDefault()
    {
        var result = await _accountService.SeedDefaultAsync();
        return result.ToApiResult();
    }
}
