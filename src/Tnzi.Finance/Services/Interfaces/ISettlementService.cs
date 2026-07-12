namespace Tnzi.Finance.Services.Interfaces;

/// <summary>
/// 结算服务（核销 = 收付款单/贷项单 → 发票/账单的对应关系，独立于 GL）
/// </summary>
public interface ISettlementService
{
    /// <summary>查询某单据的核销记录（作为源或目标）</summary>
    Task<Result<List<PaymentApplicationDto>>> GetApplicationsAsync(SettlementDocType docType, Guid docId, CancellationToken cancellationToken = default);

    /// <summary>获取往来方的可核销未清单据（Posted/PartiallyPaid 且未清 &gt; 0）</summary>
    Task<Result<List<OpenDocumentDto>>> GetOpenDocumentsAsync(FinancePartyType partyType, Guid partyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 核销：把一个源（收付款单/贷项单）分配到一个或多个目标（发票/账单）。
    /// 源目标须同交易币；分配不得超过源剩余与目标未清；
    /// 源与目标汇率不同的外币核销自动追加 realized FX 凭证
    /// </summary>
    Task<Result<List<PaymentApplicationDto>>> ApplyAsync(ApplySettlementDto input, CancellationToken cancellationToken = default);

    /// <summary>撤销核销（回滚两侧 AppliedTotal 与派生状态；冲销 realized FX 凭证）</summary>
    Task<Result> UnapplyAsync(Guid applicationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量结算（Pay Bills / Receive Payments）：选定一组未清单据（同为 Invoice 或同为 Bill），
    /// 按（往来方 + 币种）分组各生成一张收付款单，过账后立即核销到对应单据。
    /// 整体一个事务——任一环节失败（含过账前钩子否决）全部回滚。
    /// 汇率按付款日期从汇率表解析；与目标捕获汇率不同的外币结算自动产生 realized FX 凭证
    /// </summary>
    Task<Result<BatchPaymentResultDto>> PayAsync(BatchPaymentDto input, CancellationToken cancellationToken = default);
}
