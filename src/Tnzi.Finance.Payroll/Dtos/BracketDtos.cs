namespace Tnzi.Finance.Payroll.Dtos;

/// <summary>
/// 税级表 DTO（含行）
/// </summary>
public class BracketTableDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreationTime { get; set; }
    public List<BracketRowDto> Rows { get; set; } = [];
}

/// <summary>
/// 税级表列表 DTO（分页列表用，不含行）
/// </summary>
public class BracketTableListDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 税级行 DTO
/// </summary>
public class BracketRowDto
{
    public Guid Id { get; set; }
    public int Sequence { get; set; }
    public decimal LowerBound { get; set; }
    public decimal? UpperBound { get; set; }
    public decimal Rate { get; set; }
    public decimal? QuickDeduction { get; set; }
}

/// <summary>
/// 创建税级表请求（行随头全量提交）
/// </summary>
public class CreateBracketTableDto
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public List<BracketRowInputDto> Rows { get; set; } = null!;
}

/// <summary>
/// 更新税级表请求（行全量重建）
/// </summary>
public class UpdateBracketTableDto : CreateBracketTableDto
{
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// 税级行输入
/// </summary>
public class BracketRowInputDto
{
    public int Sequence { get; set; }
    public decimal LowerBound { get; set; }
    public decimal? UpperBound { get; set; }
    public decimal Rate { get; set; }
    public decimal? QuickDeduction { get; set; }
}

/// <summary>
/// 税级表查询请求
/// </summary>
public class BracketTableQueryDto : PagedQueryDto
{
    /// <summary>关键字（编码/名称模糊匹配）</summary>
    public string? Keyword { get; set; }

    /// <summary>精确编码（列出某编码的全部版本）</summary>
    public string? Code { get; set; }

    /// <summary>是否仅启用</summary>
    public bool? IsActive { get; set; }
}
