namespace Tnzi.Payment.Providers;

/// <summary>
/// 渠道侧优惠券同步能力：把一条本地促销登记成支付渠道自己的优惠券。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么与 <see cref="IPaymentProvider"/> 分开。</b> 收款、退款、绑卡是每个渠道都要回答的问题；
/// 「渠道侧也有一套优惠券」只是少数渠道有的附加设施。把它并进 <see cref="IPaymentProvider"/>，
/// 等于让每个自定义渠道都背上一个与收款无关的方法。
/// </para>
/// <para>
/// <b>契约在父模块、实现在渠道包。</b> 目前唯一的实现是 <c>Tnzi.Payment.Stripe</c> 里的
/// <c>StripeCouponSync</c>。未注册任何实现时 <c>PromotionService.SyncToStripeAsync</c> 返回 501
/// 并指名要加载的包 —— <b>不是静默跳过</b>：静默跳过会让运营以为渠道侧已经有这张券了，
/// 而真相要等到某个用户在渠道结账页输码被拒时才暴露。
/// </para>
/// <para>
/// <b><see cref="ChannelCode"/> 不是装饰。</b> 调用方按它确认拿到的实现确实是自己要同步的那个渠道；
/// 少了这一步，某个应用注册了别的渠道的实现时，同步会打到另一家去而本地照样把返回的标识
/// 写进 <c>Promotion.StripeCouponId</c>。
/// </para>
/// </remarks>
public interface IPaymentChannelCouponSync
{
    /// <summary>本实现对应的渠道代码，取值与 <see cref="IPaymentProvider.ChannelCode"/> 同一套。</summary>
    string ChannelCode { get; }

    /// <summary>
    /// 把一条促销同步为渠道侧优惠券。
    /// </summary>
    /// <param name="coupon">要同步的促销快照。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功时携带渠道侧优惠券标识，由调用方落库。</returns>
    Task<Result<string>> SyncCouponAsync(PaymentChannelCouponDto coupon, CancellationToken cancellationToken = default);
}
