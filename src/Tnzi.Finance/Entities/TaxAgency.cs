namespace Tnzi.Finance.Entities;

/// <summary>
/// 税务机构（税率的归属主体；申报周期等合规内容不进框架）
/// </summary>
public class TaxAgency : MultiTenantAuditedEntity<Guid>
{
    /// <summary>
    /// 机构名称（租户内唯一）
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 描述
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsActive { get; set; } = true;
}
