namespace Tnzi.Finance.Services.Interfaces;

/// <summary>
/// 单个往来方的账面视图：概览数字 + 跨单据类型的交易流水。
/// </summary>
/// <remarks>
/// <b>为什么是一个服务而不是让呈现端拼</b>：客户/供应商详情页要回答的三件事 ——「他欠我多少」
/// 「逾期多少」「这期做了多少生意」—— 每一件都是**全量口径**，拿分页列表在前端求和只会加总
/// 当前这一页，得出一个看起来像数字的错误答案。
/// <br/><br/>
/// <b>口径同源</b>：未清与账龄分桶直接复用 <see cref="IFinancialReportService.GetArAgingAsync"/> /
/// <see cref="IFinancialReportService.GetApAgingAsync"/> 的结果，因此**客户页显示的余额与账龄报表
/// 逐分相等**，也就与总账 AR/AP 控制科目对得上。自己再写一遍"未清额怎么算"必然漂移。
/// <br/><br/>
/// 客户与供应商共用本服务：两侧的差别只是单据类型集合与符号方向。
/// </remarks>
public interface IPartyLedgerService
{
    /// <summary>
    /// 往来方概览。
    /// </summary>
    /// <param name="partyType">客户或供应商</param>
    /// <param name="partyId">往来方 id</param>
    /// <param name="asOf">未清与账龄的时点（默认今天）</param>
    /// <param name="from">期间发生额的起始（默认本年年初）</param>
    /// <param name="to">期间发生额的截止（默认 <paramref name="asOf"/>）</param>
    Task<Result<PartyLedgerSummaryDto>> GetSummaryAsync(
        FinancePartyType partyType,
        Guid partyId,
        DateTime? asOf = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 往来方交易流水（发票/贷项/收款 或 账单/费用/付款，按单据日期倒序）。
    /// </summary>
    Task<Result<IPagedList<PartyLedgerEntryDto>>> GetTransactionsAsync(
        FinancePartyType partyType,
        Guid partyId,
        PartyLedgerQueryDto query,
        CancellationToken cancellationToken = default);
}
