namespace Tnzi.Finance.Entities;

/// <summary>
/// 税码组件（税码 → 税率的有序关联；随税码整体重建，硬删除）
/// </summary>
public class TaxCodeComponent : EntityBase<Guid>, IMultiTenant
{
    /// <summary>
    /// 租户ID
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 所属税码
    /// </summary>
    public Guid TaxCodeId { get; set; }

    /// <summary>
    /// 税率
    /// </summary>
    public Guid TaxRateId { get; set; }

    /// <summary>
    /// 税率导航
    /// </summary>
    public virtual TaxRate? Rate { get; set; }

    /// <summary>
    /// 计算顺序（复合税依赖前序组件的税额）
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// 是否复合税（税基 = 行金额 + 前序组件税额；否则税基 = 行金额）
    /// </summary>
    public bool IsCompound { get; set; }
}
