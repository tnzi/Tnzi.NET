namespace Tnzi.Payment.Services;

/// <summary>
/// 支付统计里<b>订阅那一半</b>的供给方。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么存在</b>：<see cref="PaymentStatisticsService"/> 拆分前直接查订阅与计划两张表，
/// 既算总览里的「活跃订阅数」，也算整块订阅指标（MRR / 流失率 / ARPU / 计划分布）。
/// 那是父 → 子依赖。现在统计服务只拥有它真正拥有的东西 —— 支付、退款、促销 ——
/// 订阅那一半向本契约提问。
/// </para>
/// <para>
/// <b>★ 为什么活跃订阅数是 <c>int?</c> 而不是 <c>int</c></b>：
/// 总览接口 <c>GET /admin/payment-statistics</c> 的支付与退款那一半是父模块自己的，
/// 缺席续费包时<b>必须照常工作</b>，所以整个端点不能一起 501。
/// 但那样一来「活跃订阅数」就得填一个值 —— 而填 0 是**错行为**：
/// 「这台宿主不做订阅」和「所有订阅一夜之间全没了」在界面上长得一模一样，
/// 后者是每一个订阅制生意最需要立刻看见的灾难。<c>null</c> 让前端渲染「不适用」，
/// 与「0」是两句不同的话。<c>@tnzi/core</c> 那侧的 <c>activeSubscriptions</c> 同步放宽为
/// <c>number | null</c>，管理端的 KPI 卡本来就把 <c>null</c> 渲染成占位符。
/// </para>
/// <para>
/// <b>整块订阅指标则相反，缺席时回 501</b>：<c>GET /admin/payment-statistics/subscription-metrics</c>
/// 这个端点<b>整个</b>只讲订阅，没有任何一半是父模块的，因此「本服务器不提供此功能」
/// 才是准确答复。不是 503：503 意味着暂时故障，会让监控和客户端不停重试一件永远不会恢复的事。
/// </para>
/// </remarks>
public interface IPaymentStatisticsContributor
{
    /// <summary>
    /// 当前活跃（含试用中）的订阅数；本供给方无法回答时返回 <c>null</c>。
    /// </summary>
    /// <remarks>
    /// 口径与拆分前一致：<c>Status == Active || Status == Trial</c>，不按时间窗过滤 ——
    /// 它回答的是「此刻有多少人在订」，而不是「这段时间里有多少人订过」。
    /// </remarks>
    Task<int?> GetActiveSubscriptionCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 整块订阅指标（MRR / 活跃 / 试用 / 本月新增与取消 / 流失率 / ARPU / 计划分布）。
    /// </summary>
    /// <remarks>返回 <c>null</c> 表示本供给方给不出，调用方据此回 501。</remarks>
    Task<SubscriptionMetricsDto?> GetSubscriptionMetricsAsync(CancellationToken cancellationToken = default);
}
