namespace Tnzi.Finance.Banking.Controllers.Admin;

/// <summary>
/// 银行规则管理控制器
/// </summary>
[Route("admin/finance/bank-rules")]
[DefaultController]
[ApiAuthorize(PermissionName = "finance.bankRule.view")]
public class DefaultFinanceBankRuleAdminController : ApiAdminControllerBase
{
    private readonly IBankRuleService _service;

    public DefaultFinanceBankRuleAdminController(IBankRuleService service)
    {
        _service = Check.NotNull(service);
    }

    protected IBankRuleService Service => _service;

    /// <summary>
    /// 分页查询银行规则
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<BankRuleDto>>> GetPaged([FromQuery] BankRuleQueryDto query)
    {
        var result = await _service.GetPagedAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取银行规则
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<BankRuleDto>> Get(Guid id)
    {
        var result = await _service.GetAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建银行规则
    /// </summary>
    [HttpPost]
    [ApiAuthorize(PermissionName = "finance.bankRule.create")]
    public virtual async Task<ApiResult<BankRuleDto>> Create([FromBody] CreateBankRuleDto request)
    {
        var result = await _service.CreateAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新银行规则
    /// </summary>
    [HttpPut("{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.bankRule.update")]
    public virtual async Task<ApiResult<BankRuleDto>> Update(Guid id, [FromBody] CreateBankRuleDto request)
    {
        var result = await _service.UpdateAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除银行规则
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.bankRule.delete")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await _service.DeleteAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 重排规则优先级
    /// </summary>
    [HttpPost("reorder")]
    [ApiAuthorize(PermissionName = "finance.bankRule.update")]
    public virtual async Task<ApiResult> Reorder([FromBody] ReorderBankRulesDto request)
    {
        var result = await _service.ReorderAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 试跑规则（只读；POST 仅因入参是一组筛选条件）
    /// </summary>
    [HttpPost("{id:guid}/test")]
    public virtual async Task<ApiResult<BankRuleTestResultDto>> Test(Guid id, [FromBody] TestBankRuleDto request)
    {
        var result = await _service.TestAsync(id, request);
        return result.ToApiResult();
    }
}
