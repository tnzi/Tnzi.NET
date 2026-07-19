namespace Tnzi.Finance.Payroll.Dtos;

/// <summary>
/// 员工 DTO
/// </summary>
public class EmployeeDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public DateTime? HireDate { get; set; }
    public DateTime? TerminationDate { get; set; }
    public Guid? VendorId { get; set; }
    public Guid? UserId { get; set; }
    public string? AttributesJson { get; set; }
    public bool IsActive { get; set; }
    public string? Notes { get; set; }
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 创建员工请求
/// </summary>
public class CreateEmployeeDto
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public DateTime? HireDate { get; set; }
    public DateTime? TerminationDate { get; set; }
    public Guid? UserId { get; set; }
    public string? AttributesJson { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// 更新员工请求（全量更新）
/// </summary>
public class UpdateEmployeeDto : CreateEmployeeDto
{
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// 员工查询请求
/// </summary>
public class EmployeeQueryDto : PagedQueryDto
{
    /// <summary>关键字（编码/姓名/邮箱模糊匹配）</summary>
    public string? Keyword { get; set; }

    /// <summary>是否仅在册</summary>
    public bool? IsActive { get; set; }
}

/// <summary>
/// 薪资分配 DTO
/// </summary>
public class SalaryAssignmentDto
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid StructureId { get; set; }

    /// <summary>结构名称（列表展示用）</summary>
    public string StructureName { get; set; } = string.Empty;

    public DateTime EffectiveFrom { get; set; }
    public decimal BaseAmount { get; set; }
    public string? Notes { get; set; }
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 创建薪资分配请求（修正 = 删除重建，无更新端点）
/// </summary>
public class CreateSalaryAssignmentDto
{
    public Guid StructureId { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public decimal BaseAmount { get; set; }
    public string? Notes { get; set; }
}
