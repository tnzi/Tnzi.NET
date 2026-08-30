namespace Tnzi.Payment.Services;

/// <summary>
/// 「一张已保存的支付方式被绑上 / 被解绑」的下游接收方。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么存在</b>：拆分前 <see cref="PaymentMethodService"/> 直接持有
/// <c>IRepository&lt;Subscription&gt;</c>，在四个地方越过边界改订阅行 —— 绑定默认卡后把它同步给
/// 尚未绑卡的订阅（两处），以及解绑 / 渠道吊销时清掉订阅上的支付方式快照（两处）。
/// 那是一条**父 → 子**的依赖，也是续费域拆不出去的直接原因。
/// 现在父模块只负责「说出发生了什么」，谁关心谁自己接。
/// </para>
/// <para>
/// <b>为什么解绑那半必须返回受影响条数</b>：调用方要把它写进
/// <c>PaymentMethodRevokedEvent.AffectedSubscriptionCount</c> 与运维日志。
/// 没有实现时求和为 0，而那正是**事实**：这台宿主根本没有订阅，也就没有任何一条被影响。
/// </para>
/// <para>
/// <b>事务契约</b>：<see cref="OnUnboundAsync"/> 由调用方在**已经开启的物理事务里**调用
/// （<c>PaymentMethodService</c> 先 <c>EnsureTransactionStartedAsync</c> 再进来），
/// 实现方因此<b>不得</b>自己开事务或自行提交 —— 「置支付方式失效」与「清订阅快照」必须同生共死：
/// 只落一半的后果是订阅没了卡，而支付方式却还显示可用。
/// </para>
/// <para>
/// <b>缺席时</b>：以 <c>IEnumerable&lt;T&gt;</c> 注入，没有任何实现就是空集合
/// —— MS.DI 只对 <c>IEnumerable&lt;T&gt;</c> 有「解析成空集合」这条特例。
/// 什么都不发生，而这是正确的：没有订阅表，就没有要同步或要清理的行。
/// 少一项能力，不是一次错误的绑卡。
/// </para>
/// </remarks>
public interface IStoredPaymentMethodBindingSink
{
    /// <summary>
    /// 用户绑定了一张（默认）支付方式。
    /// </summary>
    /// <remarks>
    /// 只在这张卡成为该用户该渠道的**默认卡**时调用 —— 与拆分前的触发条件逐字相同。
    /// 实现方应当只补齐「还没有卡」的记录，不要覆盖用户已经明确选过的绑定。
    /// </remarks>
    /// <param name="userId">支付方式归属的用户。</param>
    /// <param name="method">刚刚绑定或被设为默认的支付方式。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task OnBoundAsync(Guid userId, StoredPaymentMethod method, CancellationToken cancellationToken = default);

    /// <summary>
    /// 一张支付方式失效了（用户主动解绑，或渠道侧吊销回调）。
    /// </summary>
    /// <param name="paymentMethodId">已失效的支付方式 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本实现清理掉的记录条数；无实现时调用方求和得 0。</returns>
    Task<int> OnUnboundAsync(Guid paymentMethodId, CancellationToken cancellationToken = default);
}
