namespace Tnzi.Finance.Payroll.Dtos;

/// <summary>
/// 薪资组件 DTO
/// </summary>
public class SalaryComponentDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public SalaryComponentType Type { get; set; }
    public string? Formula { get; set; }
    public string? Condition { get; set; }
    public decimal? DefaultAmount { get; set; }
    public Guid? ExpenseAccountId { get; set; }
    public Guid? LiabilityAccountId { get; set; }
    public bool IsActive { get; set; }
    public string? Description { get; set; }
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 创建薪资组件请求
/// </summary>
public class CreateSalaryComponentDto
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public SalaryComponentType Type { get; set; }
    public string? Formula { get; set; }
    public string? Condition { get; set; }
    public decimal? DefaultAmount { get; set; }
    public Guid? ExpenseAccountId { get; set; }
    public Guid? LiabilityAccountId { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// 更新薪资组件请求（全量更新）
/// </summary>
public class UpdateSalaryComponentDto : CreateSalaryComponentDto
{
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// 薪资组件查询请求
/// </summary>
public class SalaryComponentQueryDto : PagedQueryDto
{
    /// <summary>关键字（编码/名称模糊匹配）</summary>
    public string? Keyword { get; set; }

    /// <summary>组件类型</summary>
    public SalaryComponentType? Type { get; set; }

    /// <summary>是否仅启用</summary>
    public bool? IsActive { get; set; }
}
