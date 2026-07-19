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
    /// 采购税是否可抵扣（进项税抵扣，如加拿大 GST/HST ITC）。默认 true。
    /// 为 false 时（如美国销售税作为采购成本、不可抵扣的 VAT），采购单据的该税不进
    /// <see cref="Metadata.AccountSystemRole.TaxReceivable"/>、不计入进项申报口径，而作为成本过入
    /// <see cref="Metadata.AccountSystemRole.NonRecoverableTaxExpense"/> 费用科目。销项（发票）不受此影响。
    /// </summary>
    public bool IsRecoverable { get; set; } = true;

    /// <summary>
    /// 税率组件（按 Order 依次计算）
    /// </summary>
    public virtual ICollection<TaxCodeComponent> Components { get; set; } = new List<TaxCodeComponent>();
}
