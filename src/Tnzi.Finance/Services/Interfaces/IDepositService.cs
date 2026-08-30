namespace Tnzi.Finance.Services;

/// <summary>
/// 银行存款单服务（单据范式：草稿工作流 + 过账 + 作废冲销）
/// </summary>
/// <remarks>
/// 把待存款项科目上的 N 张收款一次性带进银行：总账上是<b>一张凭证、借方一行总额</b>，
/// 银行流水那一行于是 1 : 1 配得上；而「这一笔存款是哪几张收款组成的」由单据行留存。
/// 详见 <see cref="Entities.Deposit"/>。
/// </remarks>
public interface IDepositService
{
    /// <summary>分页查询存款单</summary>
    Task<Result<IPagedList<DepositDto>>> GetPagedAsync(DepositQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>获取存款单（含行）</summary>
    Task<Result<DepositDto>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出某科目上尚未被任何存活存款单收走的已过账 Inbound 收款（存款单编辑器的候选清单）
    /// </summary>
    Task<Result<List<UndepositedReceiptDto>>> GetUndepositedReceiptsAsync(UndepositedReceiptQueryDto query, CancellationToken cancellationToken = default);

    /// <summary>
    /// 创建草稿（收款行自创建起即独占持有对应收款，由唯一索引保证；重复收款返回 409）
    /// </summary>
    Task<Result<DepositDto>> CreateDraftAsync(CreateDepositDto input, CancellationToken cancellationToken = default);

    /// <summary>更新草稿（整体替换行；被移出的收款即刻释放）</summary>
    Task<Result<DepositDto>> UpdateDraftAsync(Guid id, CreateDepositDto input, CancellationToken cancellationToken = default);

    /// <summary>删除草稿（释放其持有的全部收款）</summary>
    Task<Result> DeleteDraftAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>过账（借 目标银行科目一行总额 / 贷 来源科目逐张收款 + 各其它款项科目）</summary>
    Task<Result<DepositDto>> PostAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>作废（冲销过账凭证，并释放其收走的全部收款）</summary>
    Task<Result<DepositDto>> VoidAsync(Guid id, CancellationToken cancellationToken = default);
}
