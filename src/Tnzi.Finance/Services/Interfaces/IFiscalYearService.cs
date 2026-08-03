namespace Tnzi.Finance.Services;

/// <summary>
/// 会计年度服务（锁定日期模型）
/// </summary>
public interface IFiscalYearService
{
    /// <summary>获取全部会计年度</summary>
    Task<Result<List<FiscalYearDto>>> GetListAsync(CancellationToken cancellationToken = default);

    /// <summary>创建会计年度（区间不得与既有年度重叠）</summary>
    Task<Result<FiscalYearDto>> CreateAsync(CreateFiscalYearDto input, CancellationToken cancellationToken = default);

    /// <summary>关闭会计年度（区间内禁止过账/冲销）</summary>
    Task<Result> CloseAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>重新打开会计年度</summary>
    Task<Result> ReopenAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>删除会计年度</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 校验日期是否可过账：落入已关闭年度返回失败；
    /// 当 Finance:RequireFiscalYearForPosting 为 true 且日期不在任何未关闭年度内时返回失败
    /// </summary>
    Task<Result> ValidatePostingDateAsync(DateTime postingDate, CancellationToken cancellationToken = default);
}
