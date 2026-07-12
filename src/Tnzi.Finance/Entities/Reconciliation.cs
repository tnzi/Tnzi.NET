namespace Tnzi.Finance.Entities;

/// <summary>
/// 银行对账（对账单头）
/// </summary>
/// <remarks>
/// join 表方案：勾选行由 <see cref="ReconciliationLine"/> 引用已过账的
/// <see cref="JournalLine"/>，不修改总账行本身；cleared = 存在关联行。
/// Draft 可编辑/勾选/删除，Completed 锁定（勾选与头字段不可再改）。
/// 完成条件：对账单期末余额 = 该科目全部已勾选行（含历史已完成对账）的累计净额。
/// 同一科目同时只允许一张 Draft 对账。首版限本位币科目。
/// 冲销对（原行 + 冲销行净额为 0）可同时勾选互抵，无需特殊处理。
/// P3 银行流水导入的匹配引擎落地后，匹配结果即自动生成 ReconciliationLine。
/// </remarks>
public class Reconciliation : MultiTenantAuditedEntity<Guid>, IConcurrencyStamp
{
    /// <summary>并发标记</summary>
    public string ConcurrencyStamp { get; set; } = string.Empty;

    /// <summary>对账科目（可过账资金叶子）</summary>
    public Guid AccountId { get; set; }

    /// <summary>对账单日期</summary>
    public DateTime StatementDate { get; set; }

    /// <summary>对账单期末余额（本位币）</summary>
    public decimal StatementEndingBalance { get; set; }

    /// <summary>状态</summary>
    public ReconciliationStatus Status { get; set; } = ReconciliationStatus.Draft;

    /// <summary>完成时间</summary>
    public DateTime? CompletedTime { get; set; }

    /// <summary>备注</summary>
    public string? Note { get; set; }
}
