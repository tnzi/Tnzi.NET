namespace Tnzi.Finance.Services.Interfaces;

/// <summary>
/// 客户/供应商对账单
/// </summary>
public interface ICustomerStatementService
{
    /// <summary>
    /// 出一张对账单。
    /// </summary>
    /// <remarks>
    /// 余额与账龄**复用账龄报表的同一段计算**：对账单上的数字与账龄报表逐分相等，
    /// 否则寄出去的那张纸和自己账上的数对不上，比不寄更糟。
    /// </remarks>
    Task<Result<CustomerStatementDto>> GetAsync(
        FinancePartyType partyType, Guid partyId, CustomerStatementQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>
    /// 该催谁：按逾期程度倒序列出候选。
    /// </summary>
    Task<Result<List<DunningCandidateDto>>> GetDunningCandidatesAsync(
        FinancePartyType partyType, DateTime? asOf = null, CancellationToken cancellationToken = default);
}
