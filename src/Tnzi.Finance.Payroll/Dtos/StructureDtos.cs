namespace Tnzi.Finance.Payroll.Dtos;

/// <summary>
/// 薪资结构 DTO（含行）
/// </summary>
public class SalaryStructureDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public PayFrequency Frequency { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreationTime { get; set; }
    public List<SalaryStructureLineDto> Lines { get; set; } = [];
}

/// <summary>
/// 薪资结构列表 DTO（分页列表用，不含行）
/// </summary>
public class SalaryStructureListDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public PayFrequency Frequency { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 薪资结构行 DTO
/// </summary>
public class SalaryStructureLineDto
{
    public Guid Id { get; set; }
    public Guid ComponentId { get; set; }
    public string ComponentCode { get; set; } = string.Empty;
    public string ComponentName { get; set; } = string.Empty;
    public SalaryComponentType ComponentType { get; set; }
    public int Sequence { get; set; }
    public string? FormulaOverride { get; set; }
    public decimal? AmountOverride { get; set; }
    public string? ConditionOverride { get; set; }
}

/// <summary>
/// 创建薪资结构请求（行随头全量提交）
/// </summary>
public class CreateSalaryStructureDto
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public PayFrequency Frequency { get; set; }
    public List<SalaryStructureLineInputDto> Lines { get; set; } = null!;
}

/// <summary>
/// 更新薪资结构请求（行全量重建）
/// </summary>
public class UpdateSalaryStructureDto : CreateSalaryStructureDto
{
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// 薪资结构行输入
/// </summary>
public class SalaryStructureLineInputDto
{
    public Guid ComponentId { get; set; }
    public int Sequence { get; set; }
    public string? FormulaOverride { get; set; }
    public decimal? AmountOverride { get; set; }
    public string? ConditionOverride { get; set; }
}

/// <summary>
/// 薪资结构查询请求
/// </summary>
public class SalaryStructureQueryDto : PagedQueryDto
{
    /// <summary>关键字（名称模糊匹配）</summary>
    public string? Keyword { get; set; }

    /// <summary>发薪频率</summary>
    public PayFrequency? Frequency { get; set; }

    /// <summary>是否仅启用</summary>
    public bool? IsActive { get; set; }
}
