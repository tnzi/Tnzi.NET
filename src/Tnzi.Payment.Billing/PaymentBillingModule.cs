namespace Tnzi.Payment.Billing;

/// <summary>
/// Payment 开票与收账子模块：向客户**开出一张待付的单据**，再对着它收款。
/// </summary>
/// <remarks>
/// <para>
/// <b>名字为什么是 Billing 而实体仍叫 Invoice</b>：本框架里有两个都叫 Invoice 的聚合，
/// 而且是两样东西。本模块的 <see cref="Invoice"/> 是一件**收款工具** —— 一份「请把这笔钱付给我」
/// 的请求，它的生命周期是发出、催、收到、作废；<c>Tnzi.Finance</c> 的 <c>Invoice</c> 是一份
/// **已过账的应收单据**，它进总账、参与账龄与对账、可以冲销。两者同名到 <c>SwaggerModule</c>
/// 必须给它们生成带命名空间限定的 schemaId 才不冲突。所以：**程序集与模块叫 Billing，
/// 实体仍叫 <c>Invoice</c>、表仍叫 <c>Payment_Invoice</c>、权限码仍是 <c>payment.invoice.*</c>**。
/// 改名会动表、动权限、动 URL，全都是持久化契约，收益只是好听。
/// </para>
/// <para>
/// <b>业务范围一句话</b>：把一次收款从「向客户开出一张待付的单据」走到「这张单据被付清或作废」。
/// 与之相对的是父模块那一半 —— **执行一次收款**（选渠道、下单、回调、退款、订阅续费）。
/// 只做一次性收款的商户从不开票：结账页收完钱，交易就结束了。
/// </para>
/// <para>
/// <b>谁会刻意只加载父模块</b>：任何在结账页一次性收款的宿主（数字商品、打赏、一次性解锁、
/// 游戏内购）。对他们来说这两张表、十一个端点和三个权限码是纯粹的噪音。
/// 反过来，向企业客户按月出账、需要一份可下载可重发的 PDF 凭据的宿主才要加载它。
/// </para>
/// <para>
/// <b>缺席时退化成什么 —— 少一项能力，不改任何一条支付行为</b>：
/// <list type="bullet">
/// <item>两个控制器（<c>invoices</c> 与 <c>admin/invoices</c>）<b>整个不注册</b>，
///   两个 <c>[Route]</c> 模板在父模块里没有任何控制器共用，因此是整体搬过来的 ——
///   不是在父模块已有的模板上新铸一个 <c>[DefaultController]</c>
///   （<c>[DefaultController]</c> 是 <c>Inherited=false</c> 而 <c>[Route]</c> 是 <c>Inherited=true</c>，
///   在父模板上新铸会让继承父默认控制器的消费方把本模块的端点一并继承走）。</item>
/// <item>支付完成后<b>不自动开票</b>：本模块的 <see cref="InvoiceIssuingHandler"/> 不存在，
///   父模块自己那个 <c>PaymentCompletedEventHandler</c> 照常记日志。
///   支付、退款、订阅续费、回调、对账<b>一个字节不差</b>。</item>
/// <item>权限码 <c>payment.invoice.*</c> 不 seed；管理端菜单经
///   <c>moduleGate: 'payment-billing'</c> 一并隐藏，不会渲染死链。</item>
/// <item>配置中心里不再出现 "Invoice" 分组（分组是从**已加载模块的程序集**扫出来的）——
///   这一条比拆分前更准：<c>InvoiceOptions</c> 留在父模块时，不开票的宿主也会看到一个
///   改了不生效的分组。</item>
/// <item><c>Payment.InvoiceId</c> 这一列留在父模块并恒为 null —— 它拆分前<b>也</b>从未被赋值过
///   （外键一直在 <c>Invoice.PaymentId</c> 那一侧）。</item>
/// </list>
/// </para>
/// <para>
/// <b>依赖方向：本模块 → 父模块，恒定单向</b>。<see cref="PaymentInvoiceService"/> 读
/// <c>IRepository&lt;PaymentEntity&gt;</c>（从支付快照取客户身份与金额拆分），
/// <see cref="InvoiceIssuingHandler"/> 订父模块的 <c>PaymentCompletedEvent</c>，
/// 错误码与常量也读父模块的 <c>ErrorCodes</c> / <c>PaymentConstants</c>。反方向一条都没有：
/// 父模块删掉了 <c>Payment.Invoice</c> 导航与 <c>PaymentConfiguration</c> 里那段一对一声明。
/// </para>
/// <para>
/// <b>顺带收窄了父模块的依赖闭包</b>：<c>Tnzi.Template</c> 与 <c>Tnzi.Storage</c> 在父模块里
/// <b>只有发票服务一个使用者</b>，两条 <c>[OptionalDependsOn]</c> 因此随发票一起搬来这里，
/// 父模块的 <c>Tnzi.Storage</c> 项目引用被删掉。其中 <c>Tnzi.Template</c> 顺手补了一个真的缺陷：
/// 父模块一直 <c>global using Tnzi.Template</c> 却<b>从未声明过</b>这条项目引用，
/// 靠 <c>Tnzi.Notification</c> 的传递引用编译 —— 只要通知模块哪天不再引用模板模块，
/// 支付模块就会在一次与它无关的改动里编译失败。按同一条道理，本模块把它真正消费的三条
/// （Template / Storage / Notification）<b>全部显式声明</b>，即使 <c>Tnzi.Notification</c>
/// 经父模块传递可达 —— 它可达只是因为父模块碰巧还要用它发续费提醒。
/// </para>
/// <para>
/// <b>表名一个字不变</b>：<see cref="TableNamePrefix"/> 与父模块<b>逐字相同</b>。
/// 前缀是按<b>实体所在程序集</b>回查模块容器拿到的（<c>TableNamePrefixConfiguration</c>），
/// 实体换了程序集就得由新程序集的模块把同一个前缀再声明一遍。漏掉这一行，
/// <c>Payment_Invoice</c> / <c>Payment_InvoiceLineItem</c> 会静默地变成 <c>Invoice</c> /
/// <c>InvoiceLineItem</c> —— 编译照过、业务测试照绿，损害要到下一次生成迁移
/// （两条 rename，在有数据的库上等于两张空表加两张孤儿表）才显形。
/// 由 <c>Tnzi.Payment.Billing.Tests/TableNamingTests</c> 守着，走的是框架真的用的那段解析代码。
/// </para>
/// </remarks>
[DependsOn(typeof(PaymentModule))]
[OptionalDependsOn(typeof(TemplateModule))]
[OptionalDependsOn(typeof(NotificationModule))]
[OptionalDependsOn(typeof(StorageModule))]
public class PaymentBillingModule : TnziApplicationModule
{
    /// <summary>
    /// 52：Payment(50) 与两个渠道包 Stripe / PayPal(51) 之后。实际次序由 <c>[DependsOn]</c>
    /// 拓扑排序保证，这个数字只决定同层模块之间的先后。
    /// </summary>
    public override int LoadOrder => 52;

    /// <summary>
    /// 表名前缀，<b>与父模块逐字相同</b>。见类注释「表名一个字不变」。
    /// </summary>
    public override string? TableNamePrefix => "Payment";

    /// <inheritdoc />
    public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 配置节路径一字未变：InvoiceOptions 的 [ConfigSection] 写的是绝对路径 Payment:Invoice，
        // 拆分前由父模块绑（那时它同时还是 PaymentOptions.Invoice 嵌套属性），现在由本模块绑。
        // 运维手里的 appsettings.json 不需要改一个字符。校验也跟着搬（InvoiceOptionsValidator）。
        context.Services.AddTnziOptions<InvoiceOptions, InvoiceOptionsValidator>(context.Configuration);
        return base.PreConfigureServicesAsync(context);
    }

    /// <inheritdoc />
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        // 权限码随模块走：不开票的宿主不会 seed payment.invoice.* 这 3 个码。
        context.Services.AddTransient<IPermissionDefinitionProvider, PaymentBillingPermissions>();

        context.Services.AddScoped<IPaymentInvoiceService, PaymentInvoiceService>();

        // 支付完成 → 自动开票。父模块在同一个事件上已经挂了两个处理器（记日志 + 订阅计费回流），
        // 这里是第三个：事件总线按 IEnumerable<IEventHandler<TEvent>> 解析，互不覆盖。
        context.Services.AddEventHandler<PaymentCompletedEvent, InvoiceIssuingHandler>();

        return base.ConfigureServicesAsync(context);
    }
}
