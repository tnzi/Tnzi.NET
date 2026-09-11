namespace Tnzi.Payment.Subscriptions;

/// <summary>
/// Payment 周期性收款子模块：对**同一个客户按周期反复收款**。
/// </summary>
/// <remarks>
/// <para>
/// <b>业务范围一句话</b>：把「这位客户每月付我 29 元」这件事变成一台状态机 —— 计划与价目、
/// 开通与试用、按比例计费的升降级、无人值守的 off-session 续费扣款、扣款失败后的宽限期重试
/// 与逾期过期、扣款前的续费提醒、暂停与恢复。与之相对的是父模块那一半 ——
/// <b>执行一次收款</b>（选渠道、下单、回调、退款、对账）。
/// </para>
/// <para>
/// <b>谁会刻意只加载父模块</b>：任何在结账页一次性收款的宿主 —— 数字商品、打赏、一次性解锁、
/// 游戏内购、按次计费的 API。收完钱交易就结束了，没有「下一期」这个概念。
/// 对他们来说这三张表、二十六个端点、四个权限码和一整套后台扫描是纯粹的噪音：
/// 权限矩阵里多出一块永远不授予的功能面，菜单里多出一页永远是空的列表，
/// 后台每隔几分钟醒来五次去扫一张永远为空的表。
/// </para>
/// <para>
/// <b>缺席时退化成什么 —— 少一项能力，不改任何一条支付行为</b>：
/// <list type="bullet">
/// <item>两个控制器（<c>subscriptions</c> 与 <c>admin/subscriptions</c>，共 26 个端点）
///   <b>整个不注册</b>。两个 <c>[Route]</c> 模板在父模块里没有任何控制器共用，因此是整体搬来的 ——
///   不是在父模块已有的模板上新铸一个 <c>[DefaultController]</c>
///   （<c>[DefaultController]</c> 是 <c>Inherited=false</c> 而 <c>[Route]</c> 是 <c>Inherited=true</c>，
///   在父模板上新铸会让继承父默认控制器的消费方把本模块的端点一并继承走）。</item>
/// <item>支付、退款、促销、优惠券、回调、对账<b>一个字节不差</b>。一笔标着
///   <c>BusinessType.Subscription</c> 的支付照常收得成 —— 只是没有任何订阅状态机需要被推进。</item>
/// <item>后台循环少五条扫描（续费 / 转正 / 恢复 / 过期 / 提醒），父模块自己那两条
///   （关闭过期支付、对账在途退款）照跑不误。</item>
/// <item>管理端统计总览的<b>活跃订阅数变成 <c>null</c></b>（前端渲染「不适用」），
///   而不是一个与「所有订阅一夜之间全没了」无法区分的 0；整块订阅指标端点回 <b>501</b>
///   并指名要加载哪个包。<b>不是 503</b>：503 意味着暂时故障，会让监控和客户端不停重试
///   一件永远不会恢复的事。</item>
/// <item>权限码 <c>payment.subscription.*</c> 不 seed；管理端菜单经
///   <c>moduleGate: 'payment-subscriptions'</c> 一并隐藏，不会渲染死链。</item>
/// <item>配置中心里不再出现 "Subscription" 分组（分组是从**已加载模块的程序集**扫出来的）——
///   这一条比拆分前更准：<see cref="SubscriptionOptions"/> 留在父模块时，
///   不做续费的宿主也会看到一个改了不生效的分组。</item>
/// </list>
/// </para>
/// <para>
/// <b>依赖方向：本模块 → 父模块，恒定单向</b>。拆分前父模块有<b>四处</b>反向依赖，
/// 全部改成了向契约提问（契约在父模块，实现在本模块）：
/// <list type="number">
/// <item><see cref="IStoredPaymentMethodBindingSink"/>（<see cref="SubscriptionBindingSink"/>）——
///   拆分前 <c>PaymentMethodService</c> 直接持有 <c>IRepository&lt;Subscription&gt;</c>，
///   在四个地方改订阅行。</item>
/// <item><see cref="ISubscriptionHistoryProbe"/>（<see cref="SubscriptionHistoryProbe"/>）——
///   拆分前 <c>PromotionService</c> 直接查订阅表判「是不是首次订阅」。</item>
/// <item><see cref="IPaymentStatisticsContributor"/>（<see cref="SubscriptionStatisticsContributor"/>）——
///   拆分前 <c>PaymentStatisticsService</c> 直接查订阅与计划两张表。</item>
/// <item><see cref="IPaymentScheduledScan"/>（<see cref="SubscriptionScheduledScans"/>）——
///   拆分前 <c>PaymentBackgroundService</c> 直接认识 <c>ISubscriptionService</c>。</item>
/// </list>
/// 前两条是<b>不能少的那两条</b>：少了它们，缺席就不再是「少一项能力」而是「错行为」
/// —— 绑卡不再同步、解绑不再清理（后台拿一张作废的卡反复扣款），或者一张「仅限首次订阅」的券
/// 被安静地当成无限制。由 <c>Tnzi.Payment.Subscriptions.Tests/Architecture/SubscriptionSeamRedLineTests</c>
/// 守着，那组测试是先写红再写实现的。
/// </para>
/// <para>
/// <b>表名一个字不变</b>：<see cref="TableNamePrefix"/> 与父模块<b>逐字相同</b>。
/// 前缀是按<b>实体所在程序集</b>回查模块容器拿到的（<c>TableNamePrefixConfiguration</c>），
/// 实体换了程序集就得由新程序集的模块把同一个前缀再声明一遍。漏掉这一行，
/// <c>Payment_Subscription</c> / <c>Payment_SubscriptionPlan</c> / <c>Payment_SubscriptionChange</c>
/// 会静默地掉掉前缀 —— 编译照过、业务测试照绿，损害要到下一次生成迁移
/// （三条 rename，在有数据的库上等于三张空表加三张孤儿表）才显形。
/// 由 <c>Tnzi.Payment.Subscriptions.Tests/TableNamingTests</c> 守着，走的是框架真的用的那段解析代码。
/// </para>
/// <para>
/// <b>★ 本次拆分需要一条迁移</b>，而且只有一条：<c>Subscription.Payments</c> 这条
/// <b>死导航</b>（没有反向、两侧配置里都没提过、全仓没有一行往里加过东西）被删掉了。
/// 它在 EF 约定下会在**父模块的** <c>Payment_Payment</c> 表上生成一个影子列
/// <c>SubscriptionId</c>、一条外键 <c>FK_Payment_Payment_Subscription_SubscriptionId</c> 与一条索引。
/// 留着它，这三样东西的存在与否就取决于「有没有加载本包」，父模块的表形状会随模块集变化。
/// 删掉是 <c>DropForeignKey</c> + <c>DropIndex</c> + <c>DropColumn</c>，
/// <b>无数据损失</b>（那一列从来没有被写过），消费方需要生成并应用一条迁移。
/// </para>
/// </remarks>
[DependsOn(typeof(PaymentModule))]
[OptionalDependsOn(typeof(NotificationModule))]
public class PaymentSubscriptionsModule : TnziApplicationModule
{
    /// <summary>
    /// 53：Payment(50) → 两个渠道包 Stripe / PayPal(51) → Payment.Billing(52) 之后。
    /// 实际次序由 <c>[DependsOn]</c> 拓扑排序保证，这个数字只决定同层模块之间的先后。
    /// </summary>
    public override int LoadOrder => 53;

    /// <summary>
    /// 表名前缀，<b>与父模块逐字相同</b>。见类注释「表名一个字不变」。
    /// </summary>
    public override string? TableNamePrefix => "Payment";

    /// <inheritdoc />
    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 配置节路径一字未变：SubscriptionOptions 的 [ConfigSection] 写的是绝对路径
        // Payment:Subscription，拆分前由父模块绑（那时它同时还是 PaymentOptions.Subscription
        // 嵌套属性），现在由本模块绑。运维手里的 appsettings.json 不需要改一个字符。
        // 五条校验也跟着搬（SubscriptionOptionsValidator，文案逐字不变）。
        context.Services.AddTnziOptions<SubscriptionOptions, SubscriptionOptionsValidator>(context.Configuration);
        return base.PreConfigureServicesAsync(context);
    }

    /// <inheritdoc />
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 权限码随模块走：不做续费的宿主不会 seed payment.subscription.* 这 4 个码。
        context.Services.AddTransient<IPermissionDefinitionProvider, PaymentSubscriptionsPermissions>();

        context.Services.AddScoped<ISubscriptionService, SubscriptionService>();

        // 父模块四个扩展点的实现。★ 前两条是这条缝合线的红线：少了它们，
        //   缺席就从「少一项能力」变成「错行为」（绑卡不再同步 / 一张限首单的券变成无限制）。
        context.Services.AddScoped<IStoredPaymentMethodBindingSink, SubscriptionBindingSink>();
        context.Services.AddScoped<ISubscriptionHistoryProbe, SubscriptionHistoryProbe>();
        context.Services.AddScoped<IPaymentStatisticsContributor, SubscriptionStatisticsContributor>();
        context.Services.AddScoped<IPaymentScheduledScan, SubscriptionScheduledScans.RenewDueSubscriptions>();
        context.Services.AddScoped<IPaymentScheduledScan, SubscriptionScheduledScans.ConvertDueTrials>();
        context.Services.AddScoped<IPaymentScheduledScan, SubscriptionScheduledScans.ResumeDuePausedSubscriptions>();
        context.Services.AddScoped<IPaymentScheduledScan, SubscriptionScheduledScans.ExpireOverdueSubscriptions>();
        context.Services.AddScoped<IPaymentScheduledScan, SubscriptionScheduledScans.ApplyDuePlanChanges>();
        context.Services.AddScoped<IPaymentScheduledScan, SubscriptionScheduledScans.SendRenewalReminders>();

        // 订阅域自己的 6 个日志型事件处理器。
        context.Services.AddEventHandler<SubscriptionCreatedEvent, SubscriptionCreatedEventHandler>();
        context.Services.AddEventHandler<SubscriptionCancelledEvent, SubscriptionCancelledEventHandler>();
        context.Services.AddEventHandler<SubscriptionExpiredEvent, SubscriptionExpiredEventHandler>();
        context.Services.AddEventHandler<SubscriptionRenewedEvent, SubscriptionRenewedEventHandler>();
        context.Services.AddEventHandler<SubscriptionPlanChangedEvent, SubscriptionPlanChangedEventHandler>();
        context.Services.AddEventHandler<SubscriptionTrialConvertedEvent, SubscriptionTrialConvertedEventHandler>();

        // 订阅计费状态机回流处理器：订的是**父模块的**支付事件（子 → 父，合法方向）。
        // 父模块在这三个事件上已经各挂了一个记日志的处理器，事件总线按
        // IEnumerable<IEventHandler<TEvent>> 解析，互不覆盖。
        context.Services.AddEventHandler<PaymentCompletedEvent, SubscriptionPaymentCompletedHandler>();
        context.Services.AddEventHandler<PaymentFailedEvent, SubscriptionPaymentFailedHandler>();
        context.Services.AddEventHandler<PaymentExpiredEvent, SubscriptionPaymentExpiredHandler>();

        return base.ConfigureServicesAsync(context);
    }
}
