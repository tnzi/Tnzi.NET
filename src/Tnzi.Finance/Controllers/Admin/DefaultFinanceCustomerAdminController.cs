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

    public DefaultFinanceCustomerAdminController(ICustomerService customerService)
    {
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
    public virtual async Task<ApiResult<CustomerDto>> Create([FromBody] CreateCustomerDto request)
    {
        var result = await _customerService.CreateAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新客户
    /// </summary>
    [HttpPut("{id:guid}")]
    public virtual async Task<ApiResult<CustomerDto>> Update(Guid id, [FromBody] UpdateCustomerDto request)
    {
        var result = await _customerService.UpdateAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除客户
    /// </summary>
    [HttpDelete("{id:guid}")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await _customerService.DeleteAsync(id);
        return result.ToApiResult();
    }
}
