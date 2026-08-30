namespace Tnzi.Payment.PayPal;

/// <summary>
/// PayPal 支付渠道子模块：把 PayPal 的实现从 <c>Tnzi.Payment</c> 里分出来。
/// </summary>
/// <remarks>
/// <para>
/// <b>业务范围</b>：经 PayPal 收款、退款、回调验签，以及 Vault v3 账户保存 + 商户发起扣款
/// （自动续费）。只走 Stripe、只走线下人工确认收款、或者只用本模块的订阅与发票能力
/// 而不接 PayPal 的应用不加载它。
/// </para>
/// <para>
/// <b>无实体无表</b>，故用 <see cref="TnziCustomModule"/> 且不声明表前缀；无控制器、无权限码，
/// 加载与否不改变任何 HTTP 面 —— <c>POST /payments/callback/paypal</c> 与整套支付方式端点
/// 都由父模块的默认控制器提供，本模块只决定它们背后有没有 PayPal 这个渠道。
/// </para>
/// <para>
/// <b>配置节路径不变</b>：仍是 <c>Payment:PayPal</c>，绑定与校验只是挪到了这里。
/// 拆分不改配置面，既有部署的 appsettings 一字不用动。
/// </para>
/// <para>
/// <b>本包不带任何厂商 SDK。</b> 父模块此前引着 <c>PayPalCheckoutSdk</c>（连带 <c>PayPalHttp</c>），
/// 而整个渠道实现走的是 <c>HttpClient</c> + <c>System.Text.Json</c> 直连 REST API，对那个 SDK
/// <b>零引用</b> —— 拆包时核实过，所以它是被<b>删掉</b>而不是搬进本包的：把一个没人用的包搬过来
/// 只是换个地方背着它。这次拆分对依赖闭包的净效果因此是「父模块少两个包，子模块不新增包」。
/// </para>
/// <para>
/// <b>缺席时的行为</b>是「少一个渠道」，不是「行为出错」：
/// </para>
/// <list type="bullet">
///   <item>建单 / 退款 / 绑卡指名 <c>PayPal</c> —— <c>PaymentProviderFactory</c> 找不到注册返回 null，
///     调用方一律 <c>PAYMENT_CHANNEL_NOT_SUPPORTED</c> / 400。这条路径本就存在，不新增机制；</item>
///   <item>PayPal 的 webhook 打进来 —— 同样 400。渠道会把它记为投递失败并重投，
///     这正是想要的：<b>没有实现却回 200</b> 等于告诉 PayPal「这笔事件我处理过了」，
///     而本地状态机一步都没走，那笔支付会永远停在处理中；</item>
///   <item>已存在的 PayPal 订阅到期续费 —— 走既有的「渠道不可用」分支降级 <c>PastDue</c> 并进入催款，
///     不会被静默跳过，也不会被记成一次成功的扣款；</item>
///   <item>解绑一个 PayPal 支付方式 —— 渠道侧 detach 跳过，本地记录照常置失效（既有行为，
///     与「用户已在 PayPal 后台自行撤销」同一条路径）；</item>
///   <item>没指名渠道而 <c>Payment:DefaultChannelCode</c> 指向 <c>PayPal</c> —— 每一笔支付都会撞上同一个 400。
///     这种「配置没错、只是少装了包」的失效不该按请求逐个去发现，故父模块在启动期就把结论说清楚，
///     见 <c>PaymentModule.OnApplicationInitializationAsync</c>。</item>
/// </list>
/// </remarks>
[DependsOn(typeof(PaymentModule))]
public class PaymentPayPalModule : TnziCustomModule
{
    /// <summary>Payment(50) 之后；实际次序由 <c>[DependsOn]</c> 拓扑排序保证，此值仅为同级 tiebreak。</summary>
    public override int LoadOrder => 51;

    /// <inheritdoc />
    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 配置节路径与拆分前逐字一致：Payment:PayPal。绑定与校验随实现走，配置面不动。
        context.Services.AddTnziOptions<PayPalOptions, PayPalOptionsValidator>(context.Configuration, "Payment:PayPal");

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 渠道是多注册服务：父模块注册 Offline / Null，兄弟包注册各自的渠道，
        // 由 PaymentProviderFactory 按 ChannelCode 分发，所以这里是 Add 而不是 TryAdd。
        context.Services.AddScoped<IPaymentProvider, PayPalProvider>();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <see cref="PayPalProvider"/> 注入 <c>IHttpClientFactory</c>，而它不是框架的必然注册项。
    /// 这一步随实现从父模块搬来：拆分后父模块里再没有任何 <c>HttpClient</c> 消费方，
    /// 把「谁需要它谁去补」留在父模块，等于让不接 PayPal 的应用也凭空多一份注册。
    /// <para>
    /// 放在 <b>PostConfigure</b> 阶段是因为这里要看的是「所有模块都配完之后到底有没有人注册过」。
    /// 放在 Configure 阶段，这个判断会取决于模块加载顺序，可能抢在应用自己的
    /// <c>AddHttpClient(...)</c>（带具体配置）之前先补一份默认注册。
    /// </para>
    /// </remarks>
    public override Task PostConfigureServicesAsync(ServiceConfigurationContext context)
    {
        if (!context.Services.Any(s => s.ServiceType == typeof(IHttpClientFactory)))
        {
            context.Services.AddHttpClient();
        }

        return Task.CompletedTask;
    }
}
