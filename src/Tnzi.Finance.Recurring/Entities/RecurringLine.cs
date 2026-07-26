namespace Tnzi.Finance.Recurring.Entities;

/// <summary>
/// 周期性单据模板行
/// </summary>
/// <remarks>
/// 三种单据的行结构逐字相同（目录项 / 摘要 / 科目 / 数量 / 单价 / 税码），故用**同一张表**
/// 承载 —— 分三张表只会让"改一处漏两处"成为迟早的事。
///
/// 存的是**单价而非金额**：涨价时改的是这里，已生成的历史单据是既成事实、不受影响。
/// </remarks>
public class RecurringLine : EntityBase<Guid>, IMultiTenant
{
    /// <summary>租户</summary>
    public Guid? TenantId { get; set; }

    /// <summary>所属模板</summary>
    public Guid RecurringDocumentId { get; set; }

    /// <summary>行号（决定生成单据的行序）</summary>
    public int LineNumber { get; set; }

    /// <summary>目录项</summary>
    public Guid? ItemId { get; set; }

    /// <summary>摘要</summary>
    public string? Description { get; set; }

    /// <summary>科目覆盖（null 回退目录项默认科目）</summary>
    public Guid? AccountId { get; set; }

    /// <summary>数量</summary>
    public decimal Quantity { get; set; } = 1m;

    /// <summary>单价</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>税码</summary>
    public Guid? TaxCodeId { get; set; }
}
