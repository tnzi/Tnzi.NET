namespace Tnzi.Payment.Promotions;

/// <summary>
/// Payment 折扣子模块：把同一件东西<b>卖便宜给一部分买家</b>。
/// </summary>
/// <remarks>
/// <para>
/// <b>业务范围一句话</b>：促销活动与优惠券（百分比 / 固定额、总量与每人配额、生效窗口、
/// 最低订单额、适用产品类型与范围、可否叠加、是否公开）、兑换码（铸码、限量、限领）、
/// 用户券包（领到了什么、用掉了什么）、以及管理员的定向发券。与之相对的是父模块那一半 ——
/// <b>按标价收款</b>。
/// </para>
/// <para>
/// <b>谁会刻意只加载父模块</b>：任何从不打折的宿主 —— 内部结算、B2B 合同价、
/// 按用量出账的平台、以及"价格由别处决定，这里只负责把钱收上来"的集成方。
/// 对他们来说这四张表、十五个端点、三个权限码是纯粹的噪音：权限矩阵里多出一块
/// 永远不授予的功能面，菜单里多出一页永远是空的列表。
/// </para>
/// <para>
/// <b>缺席时退化成什么 —— 少一项能力，不改任何一条支付行为</b>：
/// <list type="bullet">
/// <item>两个控制器（<c>promotions</c> 与 <c>admin/promotions</c>，共 15 个端点）
///   <b>整个不注册</b>。两个 <c>[Route]</c> 模板在父模块里没有任何控制器共用，因此是整体搬来的 ——
///   不是在父模块已有的模板上新铸一个 <c>[DefaultController]</c>
///   （<c>[DefaultController]</c> 是 <c>Inherited=false</c> 而 <c>[Route]</c> 是 <c>Inherited=true</c>，
///   在父模板上新铸会让继承父默认控制器的消费方把本模块的端点一并继承走）。</item>
/// <item>收款、退款、绑卡、回调、对账<b>一个字节不差</b>。不带券码的建单一如既往。</item>
/// <item>带券码的建单被拒：400 <c>COUPON_INVALID</c>。那台宿主上不存在任何优惠券码，
///   所以「这个码无效」是<b>事实</b>而不是降级。服务端另记一条 Error 指名要加载哪个包，
///   免得一次部署疏漏被读成「用户老是输错码」。</item>
/// <item>促销效果分析端点回 <b>501</b> 并指名要加载哪个包（见
///   <see cref="IPromotionAnalyticsProvider"/>）。<b>不是空列表</b>：空列表是一个答案
///   （「有促销，只是这段时间没人用」），会把部署疏漏伪装成业务结论。
///   <b>也不是 503</b>：503 意味着暂时故障，会让监控和客户端不停重试一件永远不会恢复的事。
///   统计总览的其余部分（支付、退款）照常工作 —— 它们是父模块自己的。</item>
/// <item>权限码 <c>payment.promotion.*</c> 不 seed；管理端菜单经
///   <c>moduleGate: 'payment-promotions'</c> 一并隐藏，不会渲染死链。</item>
/// <item>配置中心里不再出现 "Promotion" 分组（分组是从**已加载模块的程序集**扫出来的）——
///   这一条比拆分前更准：<see cref="PromotionOptions"/> 留在父模块时，
///   不打折的宿主也会看到一个改了不生效的分组。</item>
/// </list>
/// </para>
/// <para>
/// <b>依赖方向：本模块 → 父模块，恒定单向</b>。拆分前父模块有<b>两处</b>反向依赖，
/// 都改成了向契约提问（契约在父模块，实现在本模块）：
/// <list type="number">
/// <item><see cref="ICouponService.ReleaseCouponForPaymentAsync"/>（<see cref="CouponService"/>）——
///   拆分前 <c>PaymentService</c> 自己持有 <c>IRepository&lt;CouponUsage&gt;</c>，
///   在支付失败/过期时按 <c>PaymentId</c> 查那条核销记录。那是父模块对本模块表的直接读取，
///   而且是它唯一的一处。</item>
/// <item><see cref="IPromotionAnalyticsProvider"/>（<c>PromotionAnalyticsProvider</c>）——
///   拆分前 <c>PaymentStatisticsService</c> 直接查核销记录与促销两张表。</item>
/// </list>
/// 父模块的 <see cref="ICouponService"/> 本来就是「可空 + 默认 null」的可选注入，
/// 拆分只是把它的实现搬走并把接口收窄到父模块真正调用的四个方法。
/// </para>
/// <para>
/// <b>★ 与续费包同时加载时的一条真红线</b>：<c>FirstSubscriptionOnly</c>（仅限首次订阅）的券
/// 靠父模块的 <see cref="ISubscriptionHistoryProbe"/> 判定，而实现住在
/// <c>Tnzi.Payment.Subscriptions</c>。<b>只装本包</b>时探针缺席，该券对所有人成立 ——
/// 那是<b>事实</b>（没有订阅表就没有人订阅过），不是守卫失效。
/// 但<b>两个包都装了、探针却没注册</b>，同一句放行就变成了纯粹的守卫失效：
/// 订阅表就在那里，老客户可以反复领首单折扣。守它的是续费包那侧的
/// <c>SubscriptionSeamRedLineTests</c>，本次拆分把那组断言扩到了本模块的
/// <see cref="PromotionService"/>（提问方从父模块换成了这里）。
/// </para>
/// <para>
/// <b>表名一个字不变</b>：<see cref="TableNamePrefix"/> 与父模块<b>逐字相同</b>。
/// 前缀是按<b>实体所在程序集</b>回查模块容器拿到的（<c>TableNamePrefixConfiguration</c>），
/// 实体换了程序集就得由新程序集的模块把同一个前缀再声明一遍。漏掉这一行，
/// <c>Payment_Promotion</c> / <c>Payment_CouponUsage</c> / <c>Payment_RedemptionCode</c> /
/// <c>Payment_UserCoupon</c> 会静默地掉掉前缀 —— 编译照过、业务测试照绿，
/// 损害要到下一次生成迁移（四条 rename，在有数据的库上等于四张空表加四张孤儿表）才显形。
/// 由 <c>Tnzi.Payment.Promotions.Tests/TableNamingTests</c> 守着，走的是框架真的用的那段解析代码。
/// <b>本次拆分不需要任何迁移</b>。
/// </para>
/// </remarks>
[DependsOn(typeof(PaymentModule))]
public class PaymentPromotionsModule : TnziApplicationModule
{
    /// <summary>
    /// 54：Payment(50) → 两个渠道包 Stripe / PayPal(51) → Payment.Billing(52) →
    /// Payment.Subscriptions(53) 之后。实际次序由 <c>[DependsOn]</c> 拓扑排序保证，
    /// 这个数字只决定同层模块之间的先后。
    /// </summary>
    public override int LoadOrder => 54;

    /// <summary>
    /// 表名前缀，<b>与父模块逐字相同</b>。见类注释「表名一个字不变」。
    /// </summary>
    public override string? TableNamePrefix => "Payment";

    /// <inheritdoc />
    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 配置节路径一字未变：PromotionOptions 的 [ConfigSection] 写的是绝对路径
        // Payment:Promotion，拆分前由父模块绑，现在由本模块绑。运维手里的 appsettings.json
        // 不需要改一个字符。那一条校验也跟着搬（PromotionOptionsValidator，文案逐字不变）。
        context.Services.AddTnziOptions<PromotionOptions, PromotionOptionsValidator>(context.Configuration);
        return base.PreConfigureServicesAsync(context);
    }

    /// <inheritdoc />
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 权限码随模块走：不打折的宿主不会 seed payment.promotion.* 这 3 个码。
        context.Services.AddTransient<IPermissionDefinitionProvider, PaymentPromotionsPermissions>();

        context.Services.AddScoped<IPromotionService, PromotionService>();

        // 一个实现类回答三个接口：父模块的支付流程面（ICouponService）+ 本模块的券包与发券面。
        // 三条注册指向同一个 Scoped 实例，否则同一次请求里会出现三份各自持有 DbContext 变更跟踪的副本。
        context.Services.AddScoped<CouponService>();
        context.Services.AddScoped<ICouponService>(sp => sp.GetRequiredService<CouponService>());
        context.Services.AddScoped<ICouponWalletService>(sp => sp.GetRequiredService<CouponService>());
        context.Services.AddScoped<ICouponIssuanceService>(sp => sp.GetRequiredService<CouponService>());

        // 父模块统计服务的促销那一块。★ 缺了它，端点回 501 而不是空列表 —— 见接口注释。
        context.Services.AddScoped<IPromotionAnalyticsProvider, PromotionAnalyticsProvider>();

        return base.ConfigureServicesAsync(context);
    }
}
