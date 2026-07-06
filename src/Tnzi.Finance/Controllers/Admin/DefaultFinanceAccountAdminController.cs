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
    /// 创建科目
    /// </summary>
    [HttpPost]
    public virtual async Task<ApiResult<AccountDto>> Create([FromBody] CreateAccountDto request)
    {
        var result = await _accountService.CreateAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新科目
    /// </summary>
    [HttpPut("{id:guid}")]
    public virtual async Task<ApiResult<AccountDto>> Update(Guid id, [FromBody] UpdateAccountDto request)
    {
        var result = await _accountService.UpdateAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除科目
    /// </summary>
    [HttpDelete("{id:guid}")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await _accountService.DeleteAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 播种默认科目表（仅当科目表为空）
    /// </summary>
    [HttpPost("seed-default")]
    public virtual async Task<ApiResult<int>> SeedDefault()
    {
        var result = await _accountService.SeedDefaultAsync();
        return result.ToApiResult();
    }
}
