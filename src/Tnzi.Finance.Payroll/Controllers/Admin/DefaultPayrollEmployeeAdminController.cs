namespace Tnzi.Finance.Payroll.Controllers.Admin;

/// <summary>
/// 员工管理控制器（含薪资分配子资源）
/// </summary>
[Route("admin/payroll/employees")]
[DefaultController]
[ApiAuthorize(PermissionName = "payroll.employee.view")]
public class DefaultPayrollEmployeeAdminController : ApiAdminControllerBase
{
    private readonly IEmployeeService _employeeService;

    public DefaultPayrollEmployeeAdminController(IEmployeeService employeeService)
    {
        _employeeService = Check.NotNull(employeeService);
    }

    protected IEmployeeService EmployeeService => _employeeService;

    /// <summary>
    /// 分页查询员工
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IPagedList<EmployeeDto>>> GetPaged([FromQuery] EmployeeQueryDto query)
    {
        var result = await _employeeService.GetPagedAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取员工
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<EmployeeDto>> Get(Guid id)
    {
        var result = await _employeeService.GetAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建员工
    /// </summary>
    [HttpPost]
    [ApiAuthorize(PermissionName = "payroll.employee.create")]
    public virtual async Task<ApiResult<EmployeeDto>> Create([FromBody] CreateEmployeeDto request)
    {
        var result = await _employeeService.CreateAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新员工
    /// </summary>
    [HttpPut("{id:guid}")]
    [ApiAuthorize(PermissionName = "payroll.employee.update")]
    public virtual async Task<ApiResult<EmployeeDto>> Update(Guid id, [FromBody] UpdateEmployeeDto request)
    {
        var result = await _employeeService.UpdateAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除员工
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ApiAuthorize(PermissionName = "payroll.employee.delete")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await _employeeService.DeleteAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 幂等确保员工拥有影子供应商（报销等真 A/P 流的 payee）
    /// </summary>
    [HttpPost("{id:guid}/ensure-vendor")]
    [ApiAuthorize(PermissionName = "payroll.employee.update")]
    public virtual async Task<ApiResult<EmployeeDto>> EnsureVendor(Guid id)
    {
        var result = await _employeeService.EnsurePayeeVendorAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 列出员工的薪资分配
    /// </summary>
    [HttpGet("{id:guid}/assignments")]
    public virtual async Task<ApiResult<List<SalaryAssignmentDto>>> GetAssignments(Guid id)
    {
        var result = await _employeeService.GetAssignmentsAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建薪资分配（子资源变更走 .update）
    /// </summary>
    [HttpPost("{id:guid}/assignments")]
    [ApiAuthorize(PermissionName = "payroll.employee.update")]
    public virtual async Task<ApiResult<SalaryAssignmentDto>> CreateAssignment(Guid id, [FromBody] CreateSalaryAssignmentDto request)
    {
        var result = await _employeeService.CreateAssignmentAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除薪资分配
    /// </summary>
    [HttpDelete("{id:guid}/assignments/{assignmentId:guid}")]
    [ApiAuthorize(PermissionName = "payroll.employee.update")]
    public virtual async Task<ApiResult> DeleteAssignment(Guid id, Guid assignmentId)
    {
        var result = await _employeeService.DeleteAssignmentAsync(id, assignmentId);
        return result.ToApiResult();
    }
}
