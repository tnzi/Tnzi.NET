namespace Tnzi.Finance.Entities;

/// <summary>
/// 目录项（服务/商品目录，无库存数量流转）
/// </summary>
/// <remarks>
/// 单据行引用目录项时按 <see cref="IncomeAccountId"/> / <see cref="ExpenseAccountId"/>
/// 取默认科目；未配置时回退系统科目角色解析。
/// </remarks>
public class Item : MultiTenantAuditedEntity<Guid>
{
    /// <summary>
    /// 编码（可空；非空时租户内唯一）
    /// </summary>
    public string? Code { get; set; }

    /// <summary>
    /// 名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 类型
    /// </summary>
    public ItemType Type { get; set; } = ItemType.Service;

    /// <summary>
    /// 描述（默认带入单据行）
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 默认销售单价
    /// </summary>
    public decimal? SalesPrice { get; set; }

    /// <summary>
    /// 默认采购单价
    /// </summary>
    public decimal? PurchasePrice { get; set; }

    /// <summary>
    /// 销售收入科目（null 回退角色解析）
    /// </summary>
    public Guid? IncomeAccountId { get; set; }

    /// <summary>
    /// 采购费用科目（null 回退角色解析）
    /// </summary>
    public Guid? ExpenseAccountId { get; set; }

    /// <summary>
    /// 默认税码
    /// </summary>
    public Guid? DefaultTaxCodeId { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsActive { get; set; } = true;
}
