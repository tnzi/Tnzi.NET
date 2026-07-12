namespace Tnzi.Finance.Controllers.Admin;

/// <summary>
/// 费用支出管理控制器
/// </summary>
[Route("admin/finance/expenses")]
[DefaultController]
[ApiAuthorize(PermissionName = "finance.document.view")]
public class DefaultFinanceExpenseAdminController : ApiAdminControllerBase
{
    private readonly IExpenseService _service;

    public DefaultFinanceExpenseAdminController(IExpenseService service)
    {
        _service = Check.NotNull(service);
    }

    protected IExpenseService Service => _service;

    /// <summary>
    /// 分页查询费用支出
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<ExpenseDto>>> GetPaged([FromQuery] ExpenseQueryDto query)
    {
        var result = await _service.GetPagedAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取费用支出
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<ExpenseDto>> Get(Guid id)
    {
        var result = await _service.GetAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建费用支出草稿
    /// </summary>
    [HttpPost]
    [ApiAuthorize(PermissionName = "finance.document.create")]
    public virtual async Task<ApiResult<ExpenseDto>> Create([FromBody] CreateExpenseDto request)
    {
        var result = await _service.CreateDraftAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新费用支出草稿
    /// </summary>
    [HttpPut("{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.document.update")]
    public virtual async Task<ApiResult<ExpenseDto>> Update(Guid id, [FromBody] CreateExpenseDto request)
    {
        var result = await _service.UpdateDraftAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除费用支出草稿
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ApiAuthorize(PermissionName = "finance.document.delete")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await _service.DeleteDraftAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 过账费用支出
    /// </summary>
    [HttpPost("{id:guid}/post")]
    [ApiAuthorize(PermissionName = "finance.document.update")]
    public virtual async Task<ApiResult<ExpenseDto>> Post(Guid id)
    {
        var result = await _service.PostAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 作废费用支出
    /// </summary>
    [HttpPost("{id:guid}/void")]
    [ApiAuthorize(PermissionName = "finance.document.update")]
    public virtual async Task<ApiResult<ExpenseDto>> Void(Guid id)
    {
        var result = await _service.VoidAsync(id);
        return result.ToApiResult();
    }
}
