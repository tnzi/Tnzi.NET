namespace Tnzi.Payment.Services;

/// <summary>
/// 支付统计里<b>促销那一块</b>的供给方。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么存在</b>：<see cref="PaymentStatisticsService"/> 拆分前直接查核销记录与促销两张表，
/// 算出 Top N 促销的使用量、唯一用户数、总折扣与兑换率。那是父 → 子依赖。
/// 促销域搬去可选子模块 <c>Tnzi.Payment.Promotions</c> 之后，统计服务只拥有它真正拥有的
/// 支付与退款，促销那一块向本契约提问。
/// </para>
/// <para>
/// <b>为什么不并进 <see cref="IPaymentStatisticsContributor"/></b>：那个契约的语义是
/// 「订阅那一半的供给方」，实现它的是续费包。促销与续费是两个各自可选、互不相干的包，
/// 合成一个接口就等于逼只做促销的宿主去实现两个订阅方法（然后返回 null），
/// 也逼只做续费的宿主去回答促销问题。
/// </para>
/// <para>
/// <b>缺席时端点回 501 而不是空列表</b>：<c>GET /admin/payment-statistics/promotion-analytics</c>
/// <b>整个</b>只讲促销，没有任何一半是父模块的。返回空列表是<b>错行为</b> ——
/// 「这台宿主不做促销」和「这段时间一张券也没人用」在看板上长得一模一样，
/// 后者是运营最需要立刻看见的信号。也<b>不是 503</b>：503 意味着暂时故障，
/// 会让监控和客户端不停重试一件永远不会恢复的事。
/// </para>
/// <para>
/// 以「可空 + 默认 null」的可选构造参数注入（与同一个服务里的
/// <see cref="IPaymentStatisticsContributor"/> 写法一致）。依赖审计器会跳过带默认值的构造参数，
/// 因此不需要 <c>[SuppressDependencyAudit]</c>。
/// </para>
/// </remarks>
public interface IPromotionAnalyticsProvider
{
    /// <summary>
    /// 按使用量排名的前 <paramref name="topN"/> 条促销效果分析。
    /// </summary>
    /// <remarks>
    /// 返回 <c>null</c> 表示本供给方给不出，调用方据此回 501；返回<b>空列表</b>是一个答案 ——
    /// 「有促销这回事，只是这段时间没人用」。两者不能混为一谈。
    /// 入参校验（<paramref name="topN"/> 必须为正）在调用方完成，实现方可以假定它已经成立。
    /// </remarks>
    Task<List<PromotionAnalyticsDto>?> GetTopPromotionsAsync(
        int topN, DateTime? startDate, DateTime? endDate, CancellationToken cancellationToken = default);
}
