namespace Tnzi.Finance.Services;

/// <summary>
/// 某条已过账分录行被账本之外的东西"持有"的事实。
/// </summary>
/// <param name="JournalLineId">被持有的分录行</param>
/// <param name="ReasonCode">wire 契约的原因代码（见 <see cref="Metadata.ReversalBlockReasons"/>）</param>
/// <param name="Detail">面向操作员的说明：**是什么**持有它、以及**怎么解开**</param>
public sealed record JournalLineHold(Guid JournalLineId, string ReasonCode, string Detail);

/// <summary>
/// 回答"这些已过账的分录行，有没有被账本之外的东西持有？"
/// </summary>
/// <remarks>
/// <b>为什么存在</b>：冲销守卫与对账工作区都需要知道"这行能不能动"，而**持有者不在会计内核里** ——
/// 目前唯一的持有者是银行流水（一条 <c>Matched</c> 的导入流水指向某条总账行）。让内核直接查
/// <c>BankTransaction</c>，等于会计内核反向依赖银行域，银行域就永远拆不出去。
/// <br/><br/>
/// <b>语义</b>：**只读、只回答、不解开**。守卫拒绝时一律指路让操作员自己去解除匹配 ——
/// 自动解除是在无声地丢弃别人的对账工作。
/// <br/><br/>
/// <b>可选</b>：未注册任何实现时（消费方没加载银行域）视为"无人持有"，全部路径回到引入本契约
/// 之前的行为。这是**唯一安全的缺省**：本契约只会**增加**拒绝，不会放宽任何既有守卫。
/// <br/><br/>
/// 多个实现可并存（<c>IEnumerable</c> 注入），例如消费应用自己的资产台账也锁住某些行。
/// </remarks>
public interface IJournalLineHoldProvider
{
    /// <summary>
    /// 在给定的分录行集合里，找出被持有的那些。
    /// </summary>
    /// <remarks>
    /// 入参是**有界**的（一张凭证的行 / 一页对账候选），因此实现可以放心用一条 IN 查询；
    /// 不要反过来把持有者全集物化出来再回填——那个集合随经营年限只增不减。
    /// </remarks>
    Task<IReadOnlyList<JournalLineHold>> GetHoldsAsync(IReadOnlyCollection<Guid> journalLineIds, CancellationToken cancellationToken = default);
}
