namespace Tnzi.Payment.Services;

/// <summary>
/// 回答「这个用户以前订阅过吗」——<c>FirstSubscriptionOnly</c> 促销的唯一判据。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么存在</b>：<c>PromotionService</c> 校验一张标了「仅限首次订阅」的券时，
/// 拆分前直接查 <c>IRepository&lt;Subscription&gt;</c>。促销域与续费域是两件事
/// （一次性商品也可以发券），那条依赖是续费域拆不出去的另一个原因。
/// 促销域随后也搬进了可选子模块 <c>Tnzi.Payment.Promotions</c>，提问方因此从父模块
/// 换成了那个包里的 <c>PromotionService</c> —— 契约与本注释所述的语义一字未变。
/// </para>
/// <para>
/// <b>缺席时为什么不是「守卫失效」</b>：这个探针只会让校验<b>多拒绝</b>，从不让它少拒绝。
/// 没有实现 = 没有订阅表 = <b>没有任何用户订阅过</b>，于是「不是老订户」这个答案是
/// <i>事实</i>，不是放宽后的默认值。换句话说，缺席时这张券对所有人都成立 ——
/// 而在一台没有订阅的宿主上，「首次订阅专属」本来就退化成了「所有人」。
/// 同形先例：<c>Tnzi.Finance</c> 的 <c>IMasterDataUsageProvider</c>。
/// </para>
/// <para>
/// 以「可空 + 默认 null」的可选构造参数注入（与同一个服务里的
/// <c>IPaymentChannelCouponSync</c> 写法一致）。依赖审计器会跳过带默认值的构造参数，
/// 因此不需要 <c>[SuppressDependencyAudit]</c>。
/// </para>
/// </remarks>
public interface ISubscriptionHistoryProbe
{
    /// <summary>
    /// 这个计费账户是否成为过订户：账户下有一条真正开始过的订阅（开过试用或付过订阅款），无论它现在是什么状态。
    /// </summary>
    /// <remarks>
    /// 判据刻意包含已取消 / 已过期的订阅：语义是「他不是新客」而不是「他现在是订户」。
    /// 但从没开始过的订阅（待付首期、或没付首期就被取消的）不算：开通链路先落库订阅、再在建首期支付时核销券，
    /// 把那一条算进来，首单券就对每个新用户都核销不掉。
    /// <para>
    /// ★ 按账户问，不按经办人问：机构账户下换一位管理者去开通，仍是同一个老客户，不该再拿一次首单折扣。
    /// 默认的 User 范围下账户就是这个用户，与此前逐字相同。
    /// </para>
    /// </remarks>
    Task<bool> HasAnySubscriptionAsync(BillingAccountRef account, CancellationToken cancellationToken = default);
}
