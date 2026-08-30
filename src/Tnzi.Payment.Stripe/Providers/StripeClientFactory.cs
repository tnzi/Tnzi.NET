namespace Tnzi.Payment.Stripe.Providers;

/// <summary>
/// 由 <see cref="StripeOptions"/> 构造 <see cref="StripeClient"/> 的单一出口。
/// </summary>
/// <remarks>
/// 拆分之前，需要 Stripe 客户端的第二处（促销同步）是从 <c>StripeProvider</c> 的 internal 访问器
/// 拿的 —— 那条路跨不过程序集边界，也把「优惠券同步」绑在了「支付渠道实例」上。
/// 现在两处各自从配置构造，「哪个配置项是 Stripe 凭据」这个问题只有这一个答案。
/// </remarks>
internal static class StripeClientFactory
{
    /// <summary>按配置构造一个 Stripe 客户端。</summary>
    internal static StripeClient Create(StripeOptions options)
    {
        Check.NotNull(options);
        return new StripeClient(options.SecretKey);
    }
}
