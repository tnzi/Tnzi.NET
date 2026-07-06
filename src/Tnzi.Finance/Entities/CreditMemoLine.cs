namespace Tnzi.Finance.Entities;

/// <summary>
/// 销售贷项单行
/// </summary>
public class CreditMemoLine : EntityBase<Guid>, IMultiTenant
{
    /// <summary>租户ID</summary>
    public Guid? TenantId { get; set; }

    /// <summary>所属贷项单</summary>
    public Guid CreditMemoId { get; set; }

    /// <summary>行号</summary>
    public int LineNumber { get; set; }

    /// <summary>目录项</summary>
    public Guid? ItemId { get; set; }

    /// <summary>描述</summary>
    public string? Description { get; set; }

    /// <summary>收入科目覆盖</summary>
    public Guid? AccountId { get; set; }

    /// <summary>数量</summary>
    public decimal Quantity { get; set; } = 1m;

    /// <summary>单价</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>行金额</summary>
    public decimal Amount { get; set; }

    /// <summary>税码</summary>
    public Guid? TaxCodeId { get; set; }
}
