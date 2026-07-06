namespace Tnzi.Finance.Entities;

/// <summary>
/// 税率（隶属税务机构；百分比，如 5% 存 5.0）
/// </summary>
public class TaxRate : MultiTenantAuditedEntity<Guid>
{
    /// <summary>
    /// 所属税务机构
    /// </summary>
    public Guid AgencyId { get; set; }

    /// <summary>
    /// 税务机构导航
    /// </summary>
    public virtual TaxAgency? Agency { get; set; }

    /// <summary>
    /// 税率名称（如 "GST 5%"）
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 税率百分比（5% 存 5.0）
    /// </summary>
    public decimal Rate { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsActive { get; set; } = true;
}
