namespace Tnzi.Finance.Entities;

/// <summary>
/// 银行对账勾选行（对账 → 总账行的引用；一行总账至多被一张对账勾选）
/// </summary>
/// <remarks>
/// 无软删除：撤销勾选即硬删；对账草稿删除时行级联硬删。
/// <see cref="JournalLineId"/> 全局唯一索引保证同一总账行不会被两张对账重复 clear。
/// </remarks>
public class ReconciliationLine : EntityBase<Guid>, IMultiTenant
{
    /// <summary>租户ID</summary>
    public Guid? TenantId { get; set; }

    /// <summary>所属对账</summary>
    public Guid ReconciliationId { get; set; }

    /// <summary>勾选的总账行</summary>
    public Guid JournalLineId { get; set; }
}
