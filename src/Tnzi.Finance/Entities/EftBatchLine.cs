namespace Tnzi.Finance.Entities;

/// <summary>
/// EFT 批次行（一笔付款 → 一条转账明细）
/// </summary>
/// <remarks>
/// 无软删除：批次作废时行硬删，释放付款可重入其它批次。
/// <c>(TenantId, PaymentEntryId)</c> 唯一保证一笔付款至多在一个存活批次内。
/// </remarks>
public class EftBatchLine : EntityBase<Guid>, IMultiTenant
{
    /// <summary>租户ID</summary>
    public Guid? TenantId { get; set; }

    /// <summary>所属批次</summary>
    public Guid EftBatchId { get; set; }

    /// <summary>付款单</summary>
    public Guid PaymentEntryId { get; set; }

    /// <summary>收款方银行账户（remit-to）</summary>
    public Guid PartyBankAccountId { get; set; }

    /// <summary>金额（交易币）</summary>
    public decimal Amount { get; set; }

    /// <summary>收款人名称（快照）</summary>
    public string? PayeeName { get; set; }
}
