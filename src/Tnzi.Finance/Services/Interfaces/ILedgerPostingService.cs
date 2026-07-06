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
/// </remarks>
public interface ILedgerPostingService
{
    /// <summary>
    /// 直接过账（创建并立即过账一张凭证）
    /// </summary>
    Task<Result<JournalEntryDto>> PostAsync(LedgerPostingRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按来源单据查询凭证（用于业务单据反查/防重复过账）
    /// </summary>
    Task<Result<List<JournalEntryDto>>> GetBySourceAsync(string sourceType, string sourceId, CancellationToken cancellationToken = default);
}
