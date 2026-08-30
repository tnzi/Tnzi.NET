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
    /// 该用户名下是否存在（任何状态的）订阅记录。
    /// </summary>
    /// <remarks>
    /// 判据刻意包含已取消 / 已过期的订阅：拆分前就是无状态过滤的 <c>AnyAsync(s =&gt; s.UserId == …)</c>，
    /// 语义是「他不是新客」而不是「他现在是订户」。
    /// </remarks>
    Task<bool> HasAnySubscriptionAsync(Guid userId, CancellationToken cancellationToken = default);
}
