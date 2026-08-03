namespace Tnzi.Finance.Services;

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
    /// <param name="cancellationToken">取消令牌</param>
    Task<Result<JournalEntryDto>> ReverseAsync(Guid journalEntryId, ReverseJournalEntryDto? input = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按来源单据查询凭证（用于业务单据反查/防重复过账）
    /// </summary>
    Task<Result<List<JournalEntryDto>>> GetBySourceAsync(string sourceType, string sourceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 这张凭证现在能否冲销，不能的话卡在哪。<b>只读</b>，不产生任何写入。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 供呈现端在渲染时就决定按钮是禁用还是可点，并把原因显示出来——而不是让操作员点下去才吃一个 409。
    /// 判定口径与 <see cref="ReverseAsync"/> 实际执行的校验<b>同源</b>（期间封账 / 已完成对账 / 已匹配银行流水
    /// 三项共用冲销漏斗内的同一段守卫），避免两处规则漂移。
    /// </para>
    /// <para>
    /// 冲销日期按<b>原凭证记账日</b>判定——那是 <see cref="ReverseAsync"/> 不指定日期时的默认，
    /// 也是全部单据 <c>VoidAsync</c> 的实际取值。调用方若打算冲销到另一个日期，期间封账的结论可能不同。
    /// </para>
    /// <para>
    /// 判定失败（凭证不存在等）返回失败 <see cref="Result{T}"/>；判定成功但不可冲销时返回<b>成功</b>
    /// 且 <see cref="ReversibilityDto.CanReverse"/> 为 false——"查得到、答案是不行"不是错误。
    /// </para>
    /// <para>
    /// 默认实现返回 501：本方法晚于接口首版加入，自定义实现者不重写也不会编译失败，
    /// 但不应被误认为"可以冲销"（安全判定绝不 fail-open）。
    /// </para>
    /// </remarks>
    /// <param name="journalEntryId">要判定的凭证ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<Result<ReversibilityDto>> GetReversibilityAsync(Guid journalEntryId, CancellationToken cancellationToken = default)
        => Task.FromResult(Result<ReversibilityDto>.Failure(
            "This ILedgerPostingService implementation does not support reversibility checks.", 501));
}
