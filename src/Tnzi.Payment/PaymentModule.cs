namespace Tnzi.Payment;

/// <summary>
/// 支付模块：收款、退款、绑卡、渠道回调与对账。
/// 开票、周期性收款、折扣三块各自住在可选子模块里（见下）。
/// 配置路径：Payment
/// </summary>
/// <remarks>
/// <para>
/// 开票与收账（<c>Invoice</c> / <c>InvoiceLineItem</c> 两张表、<c>invoices</c> 与
/// <c>admin/invoices</c> 两组端点、<c>payment.invoice.*</c> 三个权限码）住在可选子模块
/// <c>Tnzi.Payment.Billing</c>：只在结账页一次性收款的宿主从不开票。不加载它就是**少一项能力**
/// —— 支付、退款、订阅续费、回调与对账一个字节不差，只是支付完成后不再自动生成发票。
/// PDF 渲染（<c>Tnzi.Template</c>）与成品落地（<c>Tnzi.Storage</c>）随发票一起搬去了那个包，
/// 它们在本模块里本来就只有发票服务一个使用者。
/// </para>
/// <para>
/// <b>周期性收款</b>（<c>Subscription</c> / <c>SubscriptionPlan</c> / <c>SubscriptionChange</c>
/// 三张表、<c>subscriptions</c> 与 <c>admin/subscriptions</c> 两组共 26 个端点、
/// <c>payment.subscription.*</c> 四个权限码）住在可选子模块 <c>Tnzi.Payment.Subscriptions</c>：
/// 本模块负责<b>执行一次收款</b>，那个包负责<b>对同一个客户按周期反复收款</b>。
/// 数字商品、打赏、一次性解锁这类宿主收完钱交易就结束了，不需要计划、试用、按比例补差价、
/// 宽限期重试与续费提醒这一整套状态机。
/// </para>
/// <para>
/// <b>折扣</b>（<c>Promotion</c> / <c>CouponUsage</c> / <c>RedemptionCode</c> / <c>UserCoupon</c>
/// 四张表、<c>promotions</c> 与 <c>admin/promotions</c> 两组共 15 个端点、
/// <c>payment.promotion.*</c> 三个权限码）住在可选子模块 <c>Tnzi.Payment.Promotions</c>：
/// 本模块负责<b>按标价收款</b>，那个包负责<b>把同一件东西卖便宜给一部分买家</b>。
/// 内部结算、B2B 合同价、按用量出账这类宿主从不打折。缺席时收款、退款、绑卡、回调、对账
/// 一个字节不差；带优惠券码的建单被拒（400 <c>COUPON_INVALID</c> —— 那台宿主上不存在任何券码，
/// 这是事实而非降级），促销效果分析端点回 <b>501</b> 并指名要加载哪个包。
/// <see cref="ICouponService"/> 的契约留在本模块（收窄到支付流程真正调用的四个方法），
/// 实现随子模块走。
/// </para>
/// <para>
/// 拆开之后本模块<b>不再认识订阅</b>，四处原本越界的读写改成向契约提问：
/// <list type="bullet">
/// <item><see cref="IStoredPaymentMethodBindingSink"/> —— 绑卡 / 解绑时同步或清理下游快照
///   （拆分前 <c>PaymentMethodService</c> 直接改订阅行）。</item>
/// <item><see cref="ISubscriptionHistoryProbe"/> —— <c>FirstSubscriptionOnly</c> 券的判据
///   （拆分前 <c>PromotionService</c> 直接查订阅表）。缺席时该券不拒绝任何人，
///   因为没有订阅表就没有人订阅过 —— 是事实，不是放宽。</item>
/// <item><see cref="IPaymentStatisticsContributor"/> —— 总览里的活跃订阅数与整块订阅指标。
///   缺席时前者是 <c>null</c>（前端渲染「不适用」，不是与「生意崩了」无法区分的 0），
///   后者回 <b>501</b> 并指名要加载哪个包。</item>
/// <item><see cref="IPaymentScheduledScan"/> —— 后台循环里的续费 / 转正 / 恢复 / 过期 / 提醒
///   五条扫描。缺席时本模块自己那两条（关过期支付、对账在途退款）照跑不误。</item>
/// </list>
/// 前两条与后两条的注入形态不同是刻意的：可能有多个供给方的用 <c>IEnumerable&lt;T&gt;</c>
/// （MS.DI 只对它有「解析成空集合」这条特例），单一问答的用「可空 + 默认 null」的可选构造参数。
/// </para>
/// <para>
/// 拆开折扣之后本模块<b>不再读促销的任何一张表</b>，两处原本越界的读取同样改成向契约提问：
/// <list type="bullet">
/// <item><see cref="ICouponService.ReleaseCouponForPaymentAsync"/> —— 支付失败/过期后按支付归还券
///   （拆分前 <c>PaymentService</c> 自己持有 <c>IRepository&lt;CouponUsage&gt;</c> 按 <c>PaymentId</c> 查）。</item>
/// <item><see cref="IPromotionAnalyticsProvider"/> —— 促销效果分析
///   （拆分前 <c>PaymentStatisticsService</c> 直接查核销记录与促销两张表）。
///   缺席时回 <b>501</b> 而不是空列表：空列表会把一次部署疏漏伪装成「这段时间没人用券」。</item>
/// </list>
/// </para>
/// </remarks>
[DependsOn(typeof(EFCoreModule))]
[DependsOn(typeof(EventBusModule))]
[DependsOn(typeof(CachingModule))]
// [OptionalDependsOn(NotificationModule)] 随续费域去了 Tnzi.Payment.Subscriptions：
// 续费提醒与扣款失败告警是订阅服务发的，本模块拆分后一处都不用通知模块了。
public class PaymentModule : TnziApplicationModule
{
    /// <summary>
    /// 支付模块加载顺序
    /// </summary>
    public override int LoadOrder => 50;

    /// <summary>
    /// 表名前缀
    /// </summary>
    public override string? TableNamePrefix => "Payment";

    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var configuration = context.Configuration;

        // 注册配置选项（统一走 AddTnziOptions：Bind + 启动期验证）
        context.Services.AddTnziOptions<PaymentOptions, PaymentOptionsValidator>(configuration);
        // 支付渠道各自的 Options 类随实现住在可选子模块里，由它们自己绑定与校验：
        // Payment:Stripe 在 Tnzi.Payment.Stripe，Payment:PayPal 在 Tnzi.Payment.PayPal。
        // 两个配置节路径都与拆分前一致 —— 拆的是程序集，不是配置面。
        // 子模块配置 - 虽为 PaymentOptions 嵌套属性，但服务中单独注入 IOptions<T>
        // Payment:Invoice 由可选子模块 Tnzi.Payment.Billing 自己绑（节路径一字不变）。
        // Payment:Subscription 由可选子模块 Tnzi.Payment.Subscriptions 自己绑（节路径一字不变）。
        // Payment:Promotion 由可选子模块 Tnzi.Payment.Promotions 自己绑（节路径一字不变）。
        context.Services.AddTnziOptions<TaxOptions>(configuration, "Payment:Tax");

        return Task.CompletedTask;
    }

    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // Code-declared permissions for this module's admin surfaces - the
        // Authorization module's PermissionDbSeeder picks every registered
        // provider up on startup (no-op when Authorization is not loaded).
        context.Services.AddTransient<IPermissionDefinitionProvider, PaymentPermissions>();

        // 注册服务
        context.Services.AddScoped<IPaymentService, PaymentService>();
        context.Services.AddScoped<IPaymentMethodService, PaymentMethodService>();
        context.Services.AddScoped<IRefundService, RefundService>();
        // ISubscriptionService 随续费域住在可选子模块 Tnzi.Payment.Subscriptions，由它自己注册。
        // IPaymentInvoiceService 随发票域住在可选子模块 Tnzi.Payment.Billing，由它自己注册。
        // IPromotionService / ICouponService 随折扣域住在可选子模块 Tnzi.Payment.Promotions，
        // 由它自己注册。ICouponService 的契约留在这里（支付流程要用它的四个方法），
        // 且一直是「可空 + 默认 null」的可选注入 —— 不加载那个包时带券码的建单被拒，
        // 不带券码的建单一个字节不差。
        context.Services.AddScoped<IPaymentStatisticsService, PaymentStatisticsService>();

        // 税额计算：默认按配置的单一税率；应用可在自己的模块里覆盖（TryAdd 让消费者的注册优先）
        context.Services.TryAddScoped<IPaymentTaxCalculator, DefaultPaymentTaxCalculator>();

        // 注册支付渠道。父模块只自带两个不依赖任何厂商包的渠道：
        // Offline（人工确认收款）与 Null（测试）。对接第三方的渠道各自住在可选子模块里 ——
        // Stripe 在 Tnzi.Payment.Stripe（Stripe.net 随它走），PayPal 在 Tnzi.Payment.PayPal。
        // 不加载就是少一个渠道，缺席的表现见 OnApplicationInitializationAsync 的启动期检查。
        context.Services.AddScoped<IPaymentProviderFactory, PaymentProviderFactory>();
        context.Services.AddScoped<IPaymentProvider, OfflineProvider>();
        context.Services.AddScoped<IPaymentProvider, NullProvider>();

        // 注册后台任务。本模块自带两条扫描（关闭过期支付 + 对账在途退款）；
        // 订阅侧的五条扫描由 Tnzi.Payment.Subscriptions 经 IPaymentScheduledScan 贡献，
        // 不加载它就是少五条扫描，这两条照跑。
        context.Services.AddHostedService<PaymentBackgroundService>();

        // 注册事件处理器
        context.Services.AddEventHandler<PaymentCompletedEvent, PaymentCompletedEventHandler>();
        context.Services.AddEventHandler<PaymentFailedEvent, PaymentFailedEventHandler>();
        context.Services.AddEventHandler<PaymentExpiredEvent, PaymentExpiredEventHandler>();
        context.Services.AddEventHandler<RefundProcessedEvent, RefundProcessedEventHandler>();
        // 订阅域的 9 个处理器（6 个日志型 + 3 个把支付完成/失败/过期路由回订阅状态机的回流处理器）
        // 随续费域搬去了 Tnzi.Payment.Subscriptions，由它自己注册。后三个订阅的是本模块的事件，
        // 事件总线按 IEnumerable<IEventHandler<TEvent>> 解析，与上面这几个互不覆盖。

        return Task.CompletedTask;
    }

    /// <summary>
    /// 启动期核对默认渠道是否真的可用。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 对接第三方的渠道全都住在可选子模块里（Stripe 在 <c>Tnzi.Payment.Stripe</c>，
    /// PayPal 在 <c>Tnzi.Payment.PayPal</c>）。<c>Payment:DefaultChannelCode</c> 的出厂值是本模块自带的
    /// <c>Offline</c>（它曾是 <c>Stripe</c>：什么都没配错的应用一启动就收到一条 Error，见
    /// <see cref="PaymentConstants.DefaultPaymentChannel"/>）。但把默认值指向 Stripe / PayPal 而不加载那个包，
    /// 或指向一个没启用的渠道，则每一笔没指名渠道的支付都会在 <see cref="PaymentProviderFactory"/> 那里拿到 null，
    /// 最终 400 <c>PAYMENT_CHANNEL_NOT_SUPPORTED</c>。那是正确的行为（少能力，不是错行为），
    /// 但让部署方按请求逐个去发现它太晚了 —— 症状是「支付全线不可用」，而配置本身一个字都没错，
    /// 排查会先怀疑凭据和网络。
    /// </para>
    /// <para>
    /// 因此这里在启动时把结论一次说清：默认渠道解析不出来就记 Error 并指名要么加载哪个包、
    /// 要么把默认值改成哪些已注册的渠道。<b>刻意不抛异常</b>：总是显式指定渠道的应用照常可用，
    /// 没有理由因为一个用不到的默认值而起不来。
    /// </para>
    /// <para>
    /// 「一个渠道都没启用、默认值也没动过」是另一回事：那不是配错，是<b>还没配</b> —— 只为发票、订阅目录、
    /// 促销这类不收款的能力而加载本模块是合法形态，每笔支付请求会被明确拒绝，没有东西在静默失效。
    /// 这种部署只记一条 Information。
    /// </para>
    /// <para>
    /// 判据与 <see cref="PaymentProviderFactory"/> 共用同一份 <see cref="PaymentProviderFactory.IsChannelEnabled"/>，
    /// 不另起一套「渠道可不可用」的判断，免得两处规则日后分叉；不直接调 <c>GetProvider</c> 是因为它每判一次不可用
    /// 就记一条 Warning。区分「没注册」与「注册了但没启用」只是为了把提示说准。
    /// </para>
    /// </remarks>
    public override Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        // IPaymentProvider / IPaymentProviderFactory 都是 Scoped，根容器解析不了，必须开一个作用域。
        using var scope = context.ServiceProvider.CreateScope();
        var serviceProvider = scope.ServiceProvider;

        var logger = serviceProvider.GetRequiredService<ILogger<PaymentModule>>();

        ReportCallbackDeduplicationScope(serviceProvider, logger);
        ReportDefaultChannel(serviceProvider, logger);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 启动期把 webhook 去重的**作用范围**说清楚。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 去重键写在 <see cref="ICache"/> 里。框架默认注册的是<b>进程内</b>的
    /// <see cref="MemoryCacheService"/>，于是多实例部署下每个实例各存一份：
    /// 同一条重投事件被路由到另一个实例时，去重命中不了。
    /// </para>
    /// <para>
    /// ★ 这条是 Information 不是 Warning，因为<b>去重不承载正确性</b> ——
    /// 支付状态推进走条件更新（CAS）、终态支付一律不再被回调改写，重复处理同一条事件
    /// 不会产生第二次状态变更。去重是一层短路，省掉重复工作并让日志干净。
    /// 单实例部署占绝大多数，在那里报警等于训练运维忽略警告。
    /// </para>
    /// <para>
    /// 但 <see cref="ICache"/> <b>完全没有注册</b>是另一回事：那说明有人主动移掉了
    /// <c>CachingModule</c> 的注册，短路整条不存在且没有任何迹象。这条记 Warning。
    /// </para>
    /// </remarks>
    private static void ReportCallbackDeduplicationScope(IServiceProvider serviceProvider, ILogger logger)
    {
        var cache = serviceProvider.GetService<ICache>();

        if (cache == null)
        {
            logger.LogWarning(
                "No ICache is registered, so channel webhook de-duplication is off: a redelivered event is "
                + "processed again from scratch. Payment state is still protected by the conditional update and "
                + "the terminal-status guard, so this does not corrupt state, but the short circuit is gone. "
                + "Register an ICache (CachingModule provides an in-process one) to get it back.");
            return;
        }

        if (cache is MemoryCacheService)
        {
            logger.LogInformation(
                "Channel webhook de-duplication uses the in-process cache, so it is scoped to this instance. "
                + "Running more than one instance means a redelivered event routed elsewhere is processed again "
                + "(state stays correct via the conditional update and the terminal-status guard). "
                + "Load a distributed ICache such as Tnzi.Redis to make de-duplication cover the deployment.");
        }
    }

    /// <summary>
    /// 启动期核对默认渠道是否真的可用（详见本类的 OnApplicationInitializationAsync 注释）。
    /// </summary>
    private static void ReportDefaultChannel(IServiceProvider serviceProvider, ILogger logger)
    {
        var options = serviceProvider.GetRequiredService<IOptionsMonitor<PaymentOptions>>().CurrentValue;
        var defaultChannel = options.DefaultChannelCode;
        var registered = serviceProvider.GetServices<IPaymentProvider>().Select(x => x.ChannelCode).ToList();
        var isRegistered = registered.Contains(defaultChannel, StringComparer.OrdinalIgnoreCase);

        if (isRegistered && PaymentProviderFactory.IsChannelEnabled(options, defaultChannel))
            return;

        var channels = registered.Count == 0 ? "(none)" : string.Join(", ", registered);

        // 一个渠道都没启用，默认值又是出厂值：支付在这台部署上还没开通，不是配错了。
        // 显式把默认值指向别处的部署不走这里 —— 那说明它打算收款，默认渠道解析不出来就该是 Error。
        var nothingEnabled = !registered.Any(code => PaymentProviderFactory.IsChannelEnabled(options, code));
        var defaultUntouched = string.Equals(defaultChannel, PaymentConstants.DefaultPaymentChannel, StringComparison.OrdinalIgnoreCase);

        if (nothingEnabled && defaultUntouched)
        {
            logger.LogInformation(
                "No payment channel is enabled, so every payment request will fail with {ErrorCode} until one is. "
                + "Registered channels: {RegisteredChannels}. Enable one with Payment:Channels:<code>:Enabled=true "
                + "(the test channel uses Payment:AllowTestProvider=true instead); Stripe and PayPal ship in the optional "
                + "Tnzi.Payment.Stripe and Tnzi.Payment.PayPal modules.",
                ErrorCodes.PaymentChannelNotSupported, channels);
            return;
        }

        if (isRegistered)
        {
            // 测试渠道走的是另一个开关（AllowTestProvider），指错开关比不指还费时间。
            var howToEnable = string.Equals(defaultChannel, PaymentConstants.NullChannelCode, StringComparison.OrdinalIgnoreCase)
                ? "Set Payment:AllowTestProvider=true (non-production only)"
                : $"Set Payment:Channels:{defaultChannel}:Enabled=true";

            logger.LogError(
                "Payment:DefaultChannelCode is '{DefaultChannel}', but that channel is not enabled. "
                + "Every payment that does not name a channel will fail with {ErrorCode}. "
                + "{HowToEnable}, or point Payment:DefaultChannelCode at another channel.",
                defaultChannel, ErrorCodes.PaymentChannelNotSupported, howToEnable);
            return;
        }

        var package = FindChannelPackage(defaultChannel);

        if (package != null)
        {
            logger.LogError(
                "Payment:DefaultChannelCode is '{DefaultChannel}', but no IPaymentProvider is registered for it. "
                + "Every payment that does not name a channel will fail with {ErrorCode}. "
                + "That channel ships in the optional {Package} module; load it, "
                + "or set Payment:DefaultChannelCode to one of the registered channels: {RegisteredChannels}.",
                defaultChannel, ErrorCodes.PaymentChannelNotSupported, package, channels);
        }
        else
        {
            logger.LogError(
                "Payment:DefaultChannelCode is '{DefaultChannel}', but no IPaymentProvider is registered for it. "
                + "Every payment that does not name a channel will fail with {ErrorCode}. "
                + "Register an IPaymentProvider whose ChannelCode matches, "
                + "or set Payment:DefaultChannelCode to one of the registered channels: {RegisteredChannels}.",
                defaultChannel, ErrorCodes.PaymentChannelNotSupported, channels);
        }
    }

    /// <summary>
    /// 框架自带、但住在可选子模块里的渠道 → 提供它的包名；不认识的渠道代码返回 null。
    /// </summary>
    /// <remarks>
    /// 包名写死在这里，而不是去探测「哪个程序集提供这个渠道」：包没加载时它的类型压根不在进程里，
    /// 没有任何东西可以问。这张表只影响一条日志的措辞 —— 认不出的渠道走通用文案，
    /// 消费方自定义的渠道因此不会收到一句莫名其妙的「请加载某个包」。
    /// </remarks>
    private static string? FindChannelPackage(string channelCode)
    {
        if (string.Equals(channelCode, PaymentConstants.StripeChannelCode, StringComparison.OrdinalIgnoreCase))
            return "Tnzi.Payment.Stripe ([DependsOn(typeof(PaymentStripeModule))])";

        if (string.Equals(channelCode, PaymentConstants.PayPalChannelCode, StringComparison.OrdinalIgnoreCase))
            return "Tnzi.Payment.PayPal ([DependsOn(typeof(PaymentPayPalModule))])";

        return null;
    }
}
