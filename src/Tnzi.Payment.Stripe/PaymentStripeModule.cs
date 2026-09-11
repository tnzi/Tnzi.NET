namespace Tnzi.Payment.Stripe;

/// <summary>
/// Stripe 支付渠道子模块：把 Stripe 的实现从 <c>Tnzi.Payment</c> 里分出来。
/// </summary>
/// <remarks>
/// <para>
/// <b>业务范围</b>：经 Stripe 收款、退款、绑卡与无人值守扣款，外加把促销同步成 Stripe 侧优惠券。
/// 只走 PayPal、只走线下收款、或者只用本模块的订阅与发票能力而不接任何在线渠道的应用不加载它，
/// 于是 <c>Stripe.net</c> 不进入依赖闭包。
/// </para>
/// <para>
/// <b>无实体无表</b>，故用 <see cref="TnziCustomModule"/> 且不声明表前缀；无控制器、无权限码，
/// 加载与否不改变任何 HTTP 面。两条相关路由都不在本模块里：
/// <c>POST /payments/callback/stripe</c> 由父模块的 <c>DefaultPaymentCallbackController</c> 提供，
/// 本模块只决定它背后有没有实现（没有 = 渠道解析不到，调用方拿 400）；
/// <c>POST /admin/promotions/{id}/sync-stripe</c> 则住在 <c>Tnzi.Payment.Promotions</c> ——
/// 只装本包而不装促销包的宿主上那条路由<b>根本不存在</b>（404，不是 501），
/// 因为它属于促销域，只是恰好要经 <c>IPaymentChannelCouponSync</c> 打到渠道。
/// </para>
/// <para>
/// <b>配置节路径不变</b>：仍是 <c>Payment:Stripe</c>，绑定与校验只是挪到了这里。
/// 拆分不改配置面，既有部署的 appsettings 一字不用动。
/// </para>
/// <para>
/// <b>缺席时的行为</b>是「少一个渠道」，不是「行为出错」：
/// </para>
/// <list type="bullet">
///   <item>建单指名 <c>Stripe</c> —— <c>PaymentProviderFactory</c> 找不到注册，返回 null，
///     调用方一律 <c>PAYMENT_CHANNEL_NOT_SUPPORTED</c> / 400。这条路径本就存在，不新增机制；</item>
///   <item>没指名渠道而 <c>Payment:DefaultChannelCode</c> 指向 <c>Stripe</c> ——
///     每一笔支付都会撞上同一个 400。这种「配置没错、只是少装了包」的失效不该按请求逐个去发现，
///     故父模块在启动期就把结论说清楚，见 <c>PaymentModule.OnApplicationInitializationAsync</c>；</item>
///   <item>促销同步 —— <c>PromotionService.SyncToStripeAsync</c> 返回 501 并指名要加载哪个包，
///     绝不静默跳过。静默跳过等于让运营以为渠道侧已经有这张券了。</item>
/// </list>
/// </remarks>
[DependsOn(typeof(PaymentModule))]
public class PaymentStripeModule : TnziCustomModule
{
    /// <summary>Payment(50) 之后；实际次序由 <c>[DependsOn]</c> 拓扑排序保证，此值仅为同级 tiebreak。</summary>
    public override int LoadOrder => 51;

    /// <inheritdoc />
    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 配置节路径与拆分前逐字一致：Payment:Stripe。绑定与校验随实现走，配置面不动。
        context.Services.AddTnziOptions<StripeOptions, StripeOptionsValidator>(context.Configuration, "Payment:Stripe");

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 渠道是多注册服务：父模块注册 PayPal / Offline / Null，本模块再添一条，
        // 由 PaymentProviderFactory 按 ChannelCode 分发，所以这里是 Add 而不是 TryAdd。
        context.Services.AddScoped<IPaymentProvider, StripeProvider>();

        // 优惠券同步是单实现契约：用 TryAdd，让先注册的消费方实现胜出（同 ICheckDocumentRenderer 的口径）。
        context.Services.TryAddScoped<IPaymentChannelCouponSync, StripeCouponSync>();

        return Task.CompletedTask;
    }
}
