namespace Tnzi.Finance.Dtos;

/// <summary>
/// 税务机构 DTO
/// </summary>
public class TaxAgencyDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>
/// 创建/更新税务机构请求
/// </summary>
public class UpsertTaxAgencyDto
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// 税率 DTO
/// </summary>
public class TaxRateDto
{
    public Guid Id { get; set; }
    public Guid AgencyId { get; set; }
    public string? AgencyName { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>
/// 创建/更新税率请求
/// </summary>
public class UpsertTaxRateDto
{
    public Guid AgencyId { get; set; }
    public string Name { get; set; } = null!;
    public decimal Rate { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// 税码 DTO（含组件）
/// </summary>
public class TaxCodeDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public List<TaxCodeComponentDto> Components { get; set; } = new();
}

/// <summary>
/// 税码组件 DTO
/// </summary>
public class TaxCodeComponentDto
{
    public Guid TaxRateId { get; set; }
    public string? RateName { get; set; }
    public decimal Rate { get; set; }
    public int Order { get; set; }
    public bool IsCompound { get; set; }
}

/// <summary>
/// 创建/更新税码请求（components 全量替换）
/// </summary>
public class UpsertTaxCodeDto
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public List<UpsertTaxCodeComponentDto> Components { get; set; } = null!;
}

/// <summary>
/// 税码组件请求
/// </summary>
public class UpsertTaxCodeComponentDto
{
    public Guid TaxRateId { get; set; }
    public int Order { get; set; }
    public bool IsCompound { get; set; }
}

// ── 税额计算契约（ITaxCalculator，可整体替换默认实现）────────────

/// <summary>
/// 税额计算请求行
/// </summary>
public class TaxCalculationLine
{
    /// <summary>行金额（税基，交易币）</summary>
    public decimal Amount { get; set; }

    /// <summary>税码（null 表示该行免税）</summary>
    public Guid? TaxCodeId { get; set; }
}

/// <summary>
/// 税额计算请求
/// </summary>
public class TaxCalculationRequest
{
    /// <summary>参与计税的行</summary>
    public List<TaxCalculationLine> Lines { get; set; } = null!;

    /// <summary>舍入小数位（缺省取 BaseCurrencyDecimals）</summary>
    public int? Decimals { get; set; }
}

/// <summary>
/// 税额计算结果的税率维度汇总
/// </summary>
public class TaxComponentAmount
{
    public Guid TaxRateId { get; set; }
    public Guid AgencyId { get; set; }
    public string RateName { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public decimal TaxAmount { get; set; }
}

/// <summary>
/// 税额计算结果（行级计算、税率维度聚合）
/// </summary>
public class TaxCalculationResult
{
    public decimal TaxTotal { get; set; }
    public List<TaxComponentAmount> Components { get; set; } = new();
}
