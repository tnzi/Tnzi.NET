namespace Tnzi.Finance.Entities;

/// <summary>
/// 税码（单据行引用的税设定；组合税由 <see cref="Components"/> 表达，无需单独 TaxGroup）
/// </summary>
public class TaxCode : MultiTenantAuditedEntity<Guid>
{
    /// <summary>
    /// 税码名称（租户内唯一，如 "GST+PST"）
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

    /// <summary>
    /// 税率组件（按 Order 依次计算）
    /// </summary>
    public virtual ICollection<TaxCodeComponent> Components { get; set; } = new List<TaxCodeComponent>();
}
