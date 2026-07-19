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
    /// <summary>采购税是否可抵扣（false = 不可抵扣、作为成本，见 <see cref="Entities.TaxCode.IsRecoverable"/>）</summary>
    public bool IsRecoverable { get; set; } = true;
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
    /// <summary>采购税是否可抵扣（默认 true；false = 不可抵扣采购税作为成本）</summary>
    public bool IsRecoverable { get; set; } = true;
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

    /// <summary>
    /// 手动税额覆盖（null = 按税率计算）。仅在行有税码时合法（覆盖额仍按税率维度聚合进申报口径）；
    /// 生效时行税额 = 覆盖额，按正常口径比例分摊到各组件，舍入尾差归最后一个组件。须 >= 0（0 表示该行确实无税）
    /// </summary>
    public decimal? TaxAmount { get; set; }
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

    /// <summary>
    /// 是否为采购单据（账单/费用）。仅采购侧应用税码的 IsRecoverable 抵扣判定：不可抵扣税进
    /// <see cref="TaxCalculationResult.NonRecoverableTotal"/> 作为成本。销售侧（发票/贷项，默认 false）
    /// 税为销项，全额进 <see cref="TaxCalculationResult.Components"/>（TaxPayable），忽略 IsRecoverable。
    /// </summary>
    public bool IsPurchase { get; set; }
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
    /// <summary>行税额合计（含可抵扣 + 不可抵扣，即单据向往来方收/付的全部税）</summary>
    public decimal TaxTotal { get; set; }

    /// <summary>
    /// 不可抵扣税合计（税码 IsRecoverable=false 的行税额）。采购过账时作为成本过入
    /// NonRecoverableTaxExpense 科目而非 TaxReceivable；不进 <see cref="Components"/>、不计入进项申报口径。
    /// TaxTotal = Σ Components.TaxAmount + NonRecoverableTotal。
    /// </summary>
    public decimal NonRecoverableTotal { get; set; }

    /// <summary>税率维度汇总（仅可抵扣税；进 TaxReceivable + 申报口径）</summary>
    public List<TaxComponentAmount> Components { get; set; } = new();
}
