namespace Tnzi.Finance.Entities;

/// <summary>
/// 核销记录（收付款单/贷项单 → 发票/账单）
/// </summary>
/// <remarks>
/// 结算独立于 GL：两侧单据已各自过账进同一控制科目（AR/AP），核销只建立对应关系、
/// 驱动未清余额与派生状态。唯一例外是 realized FX：源与目标汇率不同的外币核销
/// 追加一张汇兑损益凭证（<see cref="RealizedFxJournalEntryId"/> 回链），撤销核销时冲销。
/// 硬删除（撤销核销 = 删除记录并回滚两侧 AppliedTotal）。
/// </remarks>
public class PaymentApplication : CreationAuditedEntity<Guid>, IMultiTenant
{
    /// <summary>租户ID</summary>
    public Guid? TenantId { get; set; }

    /// <summary>核销源类型（PaymentEntry / CreditMemo）</summary>
    public SettlementDocType SourceType { get; set; }

    /// <summary>核销源ID</summary>
    public Guid SourceId { get; set; }

    /// <summary>核销目标类型（Invoice / Bill）</summary>
    public SettlementDocType TargetType { get; set; }

    /// <summary>核销目标ID</summary>
    public Guid TargetId { get; set; }

    /// <summary>核销金额（交易币；源与目标须同交易币）</summary>
    public decimal AppliedAmount { get; set; }

    /// <summary>realized FX 凭证（源/目标汇率不同的外币核销时产生）</summary>
    public Guid? RealizedFxJournalEntryId { get; set; }
}
