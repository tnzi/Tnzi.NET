namespace Tnzi.Finance.Payroll.Services;

/// <summary>
/// 员工服务（含薪资分配子资源——分配按员工聚合管理，修正 = 删除重建）
/// </summary>
public interface IEmployeeService
{
    /// <summary>分页查询员工</summary>
    Task<Result<IPagedList<EmployeeDto>>> GetPagedAsync(EmployeeQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>获取员工</summary>
    Task<Result<EmployeeDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>创建员工</summary>
    Task<Result<EmployeeDto>> CreateAsync(CreateEmployeeDto input, CancellationToken cancellationToken = default);

    /// <summary>更新员工（已链接影子供应商时单向同步名称）</summary>
    Task<Result<EmployeeDto>> UpdateAsync(Guid id, UpdateEmployeeDto input, CancellationToken cancellationToken = default);

    /// <summary>删除员工（软删除；存在薪资分配时拒绝——请改用离职日期）</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 幂等确保员工拥有影子供应商（报销等真 A/P 流的 payee）：
    /// 未链接则创建 Vendor 并回填 VendorId，已链接直接返回
    /// </summary>
    Task<Result<EmployeeDto>> EnsurePayeeVendorAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>按编码查找（内部/导入用）</summary>
    Task<Employee?> FindByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>列出员工的薪资分配（按生效日倒序）</summary>
    Task<Result<List<SalaryAssignmentDto>>> GetAssignmentsAsync(Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>创建薪资分配</summary>
    Task<Result<SalaryAssignmentDto>> CreateAssignmentAsync(Guid employeeId, CreateSalaryAssignmentDto input, CancellationToken cancellationToken = default);

    /// <summary>删除薪资分配（硬语义为软删除行；同日重建即修正）</summary>
    Task<Result> DeleteAssignmentAsync(Guid employeeId, Guid assignmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 解析某日期生效的薪资分配（EffectiveFrom ≤ asOf 的最大者；无则 null。内部/计算用）
    /// </summary>
    Task<SalaryAssignment?> ResolveAssignmentAsync(Guid employeeId, DateTime asOf, CancellationToken cancellationToken = default);
}
