namespace Tnzi.Finance.Services;

/// <summary>
/// 单个往来方的账面视图：概览数字 + 跨单据类型的交易流水。
/// </summary>
/// <remarks>
/// <b>为什么是一个服务而不是让呈现端拼</b>：客户/供应商详情页要回答的三件事 ——「他欠我多少」
/// 「逾期多少」「这期做了多少生意」—— 每一件都是**全量口径**，拿分页列表在前端求和只会加总
/// 当前这一页，得出一个看起来像数字的错误答案。
/// <br/><br/>
/// <b>口径同源</b>：未清与账龄分桶直接复用 <see cref="IFinancialReportService.GetPartyAgingAsync"/> 的结果，
/// 它与 <see cref="IFinancialReportService.GetArAgingAsync"/> / <see cref="IFinancialReportService.GetApAgingAsync"/>
/// 是同一段计算（只是只算这一个往来方），因此**客户页显示的余额与账龄报表逐分相等**，也就与总账 AR/AP
/// 控制科目对得上。自己再写一遍"未清额怎么算"必然漂移。
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
    /// <param name="cancellationToken">取消令牌</param>
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

    /// <summary>
    /// 不分页的往来方交易流水（单据日期在 [<paramref name="from"/>, <paramref name="to"/>] 内，均可为空），
    /// 按单据日期倒序。
    /// </summary>
    /// <remarks>
    /// 给要「这段时间里的全部单据」的调用方（对账单）用。分页接口的每页条数会被
    /// <c>PagedQueryDto.MaxPageSize</c>（100）静默钳住：拿一个大 <c>PageSize</c> 当全量读，
    /// 第 101 条起无声消失，而 <c>TotalCount</c> 照样报全数。
    /// 默认实现按页读完，让自行实现本接口的消费方无需改动即可编译。
    /// </remarks>
    async Task<Result<List<PartyLedgerEntryDto>>> GetAllTransactionsAsync(
        FinancePartyType partyType,
        Guid partyId,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken cancellationToken = default)
    {
        var all = new List<PartyLedgerEntryDto>();
        for (var pageIndex = 1; ; pageIndex++)
        {
            var page = await GetTransactionsAsync(partyType, partyId,
                new PartyLedgerQueryDto { PageIndex = pageIndex, PageSize = 100, From = from, To = to }, cancellationToken);
            if (!page.Succeeded)
                return Result<List<PartyLedgerEntryDto>>.Failure(page.Message ?? "Failed to read the party ledger.", page.Code ?? 400);

            all.AddRange(page.Data!.Items);
            if (page.Data.Items.Count == 0 || all.Count >= page.Data.TotalCount)
                return Result<List<PartyLedgerEntryDto>>.Success(all);
        }
    }
}
