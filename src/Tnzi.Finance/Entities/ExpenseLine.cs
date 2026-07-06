namespace Tnzi.Finance.Entities;

/// <summary>
/// 费用支出行（AccountId 必填 = 费用科目）
/// </summary>
public class ExpenseLine : EntityBase<Guid>, IMultiTenant
{
    /// <summary>租户ID</summary>
    public Guid? TenantId { get; set; }

    /// <summary>所属支出单</summary>
    public Guid ExpenseId { get; set; }

    /// <summary>行号</summary>
    public int LineNumber { get; set; }

    /// <summary>描述</summary>
    public string? Description { get; set; }

    /// <summary>费用科目（必填）</summary>
    public Guid AccountId { get; set; }

    /// <summary>行金额（交易币）</summary>
    public decimal Amount { get; set; }

    /// <summary>税码</summary>
    public Guid? TaxCodeId { get; set; }
}
