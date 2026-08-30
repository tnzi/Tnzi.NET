namespace Tnzi.Payment.Services;

/// <summary>
/// 优惠券在<b>支付流程里</b>的那一面：试算、核销、归还。
/// </summary>
/// <remarks>
/// <para>
/// <b>契约在父模块、实现在可选子模块 <c>Tnzi.Payment.Promotions</c></b>。这里只留下父模块
/// 自己会调用的四个方法 —— 建单时试算折扣、拿到 paymentId 后核销、渠道下单失败时归还、
/// 支付失败/过期时按支付归还。折扣是「一次收款」内部的一步，所以问法留在父模块；
/// 而促销怎么配、券包里有什么、兑换码怎么发，是另一件事，随子模块走。
/// </para>
/// <para>
/// <b>拆分时从这个接口上摘掉了六个方法</b>（<c>GetUserAvailableCouponsAsync</c> /
/// <c>GetUserUsedCouponsAsync</c> / <c>RedeemAsync</c> / <c>CanUseFirstSubscriptionDiscountAsync</c> /
/// <c>CreateRedemptionCodeAsync</c> / <c>GrantAsync</c>）。判据是逐个查调用方：
/// 六个各自<b>只有一个调用方</b>，且都是随子模块整体搬走的那两个促销控制器。
/// 它们现在住在子模块的 <c>ICouponWalletService</c> 与 <c>ICouponIssuanceService</c> 上。
/// 留在这里会把 <c>UserCouponDto</c> 一起钉在父模块，而那个 DTO 只有券包端点在用。
/// </para>
/// <para>
/// <b>缺席时</b>：没有任何实现注册，<see cref="PaymentService"/> 的可选注入拿到 null。
/// 带优惠券码的建单请求被拒（400 <c>COUPON_INVALID</c>）—— 那台宿主上根本不存在任何优惠券码，
/// 所以「这个码无效」是<b>事实</b>而不是降级；不带优惠券码的建单一个字节不差。
/// 服务端同时记一条 Error 说明要加载哪个包，免得部署方把配置疏漏读成用户输错了码。
/// </para>
/// </remarks>
public interface ICouponService
{
    /// <summary>
    /// 试算优惠券折扣（只校验与计算，不产生核销记录）。
    /// 支付/订阅在向渠道下单前用它确定实收金额。
    /// </summary>
    Task<Result<CouponPreviewDto>> PreviewAsync(CouponApplyContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// 核销优惠券：写使用记录、原子递增总用量、消耗用户持券。
    /// </summary>
    /// <remarks>
    /// 幂等键为（促销 + 用户 + 业务单号），重复调用返回既有记录而不是报错，
    /// 使调用方在重试链路上无需自行去重。
    /// </remarks>
    Task<Result<CouponUsageDto>> ApplyCouponAsync(CouponApplyContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// 释放已核销的优惠券：回滚使用记录、递减总用量、把持券恢复为可用。
    /// 用于"券已核销但渠道下单失败"的补偿，否则用户的券会凭空消失。
    /// </summary>
    Task<Result> ReleaseCouponAsync(Guid couponUsageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按<b>支付</b>归还它占用的优惠券；这笔支付没占券时什么也不做并返回成功。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 支付失败与支付过期两条路径都要还券，而它们手上只有一笔支付，不知道核销记录的 Id。
    /// 拆分前父模块自己持有 <c>IRepository&lt;CouponUsage&gt;</c> 去查那一条记录 ——
    /// 那是父模块直接读子模块的表，也是它对促销域的<b>最后一处</b>实体级依赖。
    /// 把「按支付找到那条核销记录」这一步收进契约，父模块就只需要知道「有没有还成」。
    /// </para>
    /// <para>
    /// 由实现方保证幂等：重复调用不会把用量减两次（<c>ReleaseCouponAsync</c> 已经删掉了记录，
    /// 第二次查不到就直接成功返回）。
    /// </para>
    /// </remarks>
    Task<Result> ReleaseCouponForPaymentAsync(Guid paymentId, CancellationToken cancellationToken = default);
}
