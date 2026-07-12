namespace Tnzi.Finance.Services.Interfaces;

/// <summary>
/// 总账过账服务 —— 财务核心对外的唯一扩展点
/// </summary>
/// <remarks>
/// 任意业务单据（框架内模块或消费应用自定义单据）通过
/// <see cref="PostAsync"/> 将平衡的借贷分录投影到总账，
/// 无需修改财务核心。凭证以来源多态引用（SourceType + SourceId）回链业务单据。
/// 过账在工作单元事务内执行：校验平衡（多币种换算尾差自动生成舍入行）、
/// 科目可过账性、期间锁定，并分配连续凭证号。
/// <para>
/// 自定义单据完整生命周期：过账前先 <see cref="GetBySourceAsync"/> 防重复过账；
/// 作废单据时按来源反查凭证后 <see cref="ReverseAsync"/> 冲销
/// （原凭证保留在账中，由等额反向凭证抵消）。
/// </para>
/// </remarks>
public interface ILedgerPostingService
{
    /// <summary>
    /// 直接过账（创建并立即过账一张凭证）
    /// </summary>
    Task<Result<JournalEntryDto>> PostAsync(LedgerPostingRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 冲销已过账凭证（生成等额反向凭证；用于自定义单据的作废/更正）。
    /// 期间锁定、乐观并发（409）语义与凭证服务一致
    /// </summary>
    /// <param name="journalEntryId">要冲销的凭证ID（通常经 <see cref="GetBySourceAsync"/> 反查获得）</param>
    /// <param name="input">冲销选项（null 表示与原凭证同日、自动生成摘要）</param>
    Task<Result<JournalEntryDto>> ReverseAsync(Guid journalEntryId, ReverseJournalEntryDto? input = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按来源单据查询凭证（用于业务单据反查/防重复过账）
    /// </summary>
    Task<Result<List<JournalEntryDto>>> GetBySourceAsync(string sourceType, string sourceId, CancellationToken cancellationToken = default);
}
