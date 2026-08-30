using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.EventBus;
using Tnzi.Modules;
using Tnzi.Payment.Events;
using Tnzi.Payment.Events.Handlers;
using Tnzi.Payment.Options;
using Tnzi.Payment.Permissions;
using Tnzi.Payment.Tests.Integration;
using Tnzi.Security.Authorization;
using Tnzi.Security.Claims;

namespace Tnzi.Payment.Tests;

/// <summary>
/// 「宿主没有加载 <c>Tnzi.Payment.Billing</c>」时，本模块该是什么样子。
/// </summary>
/// <remarks>
/// <para>
/// ★ 这批用例住在<b>父测试项目</b>是刻意的：本项目<b>不引用</b>那个可选包，所以它演的是
/// 真实的「没装开票包」现场，而不是把某个服务设成 null 模拟出来的现场。整个
/// <c>Integration/</c> 目录也一并是证据 —— 那套集成库里根本没有发票的两张表，
/// 支付 / 退款 / 订阅 / 促销的全部用例照常通过。
/// </para>
/// <para>
/// 本模块的缺席面与工作区那次不同：发票的两组路由（<c>invoices</c> / <c>admin/invoices</c>）
/// 在父模块里<b>没有任何控制器共用</b>，因此两个控制器是整体搬走的，父模块<b>不留</b>任何
/// 需要 501 兜底的端点。缺席的表现是这两条路由压根不存在（404 来自路由表，不是某个端点
/// 自己回的），管理端菜单经 <c>moduleGate</c> 一并隐藏，不会渲染死链。所以这里钉的是另外三件事：
/// </para>
/// <list type="number">
/// <item><b>支付本身一个字节不差</b> —— 支付完成事件在没有开票器的宿主上照常处理完，
///   不抛、不失败、不留一句「发票服务解析不到」的噪音。</item>
/// <item><b>父模块不再认识发票</b> —— DI 里没有开票处理器，EF 模型里没有发票实体也没有
///   指向它的导航，权限目录里没有 <c>payment.invoice.*</c>，配置中心里没有 Invoice 分组。
///   任何一条回流都意味着父 → 子的依赖又长回来了。</item>
/// <item><b>两条路由确实是子模块独占的</b> —— 父模块的控制器一个都没占用它们，
///   子模块才可以整体拿走，而不是在父模板上新铸一个 <c>[DefaultController]</c>。</item>
/// </list>
/// </remarks>
public class BillingPackageAbsenceTests
{
    /// <summary>
    /// 只建模、不连库的 <see cref="PaymentTestDbContext"/> —— 集成测试用的就是这个 DbContext，
    /// 它的实体清单即「没装开票包的宿主会建出哪些表」。
    /// </summary>
    private static PaymentTestDbContext ModelOnlyContext()
    {
        var options = new DbContextOptionsBuilder<PaymentTestDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        return new PaymentTestDbContext(options, Mock.Of<ICurrentUser>());
    }

    private static async Task<ServiceConfigurationContext> ConfiguredParentModuleAsync()
    {
        var module = new PaymentModule();
        var context = new ServiceConfigurationContext(new ServiceCollection(), new ConfigurationBuilder().Build());
        await module.PreConfigureServicesAsync(context);
        await module.ConfigureServicesAsync(context);
        return context;
    }

    /// <summary>
    /// 支付完成事件在没有开票包的宿主上照常处理完 —— 「缺席 = 少一项能力」最直接的一条。
    /// </summary>
    /// <remarks>
    /// 拆分前这个处理器的后半段是开票，靠两个可选注入参数为 null 来跳过。现在开票整段搬去了
    /// 子模块的另一个处理器，这里只剩记日志。抛出去的后果不是「没开票」，而是事件总线重投 → DLQ：
    /// 一台不开票的宿主每收一笔款就往死信队列里丢一条。
    /// </remarks>
    [Fact]
    public async Task PaymentCompleted_IsHandled_WithoutTheBillingPackage()
    {
        var handler = new PaymentCompletedEventHandler(NullLogger<PaymentCompletedEventHandler>.Instance);

        await handler.HandleAsync(new PaymentCompletedEvent
        {
            PaymentId = Guid.NewGuid(),
            TradeNo = "T-1",
            Amount = 12.34m,
            Currency = "USD"
        });
    }

    /// <summary>
    /// 父模块在 <see cref="PaymentCompletedEvent"/> 上注册的处理器<b>恰好</b>是它自己那一个。
    /// </summary>
    /// <remarks>
    /// 一个事件挂 N 个处理器是既有做法：子模块的开票器（<c>Tnzi.Payment.Billing</c>）与订阅计费
    /// 回流器（<c>Tnzi.Payment.Subscriptions</c>）各由自己的模块注册。这条断言证明它们确实不在
    /// 父模块里 —— 任何一个悄悄回来，不装那个包的宿主都会在每笔支付完成时去解析一个不存在的服务。
    /// </remarks>
    [Fact]
    public async Task ParentRegistersOnlyItsOwnPaymentCompletedHandlers()
    {
        var handlers = (await ConfiguredParentModuleAsync()).Services
            .Where(x => x.ServiceType == typeof(IEventHandler<PaymentCompletedEvent>))
            .Select(x => x.ImplementationType)
            .ToList();

        handlers.ShouldBe([typeof(PaymentCompletedEventHandler)]);
    }

    /// <summary>
    /// 父模块的 DI 注册里没有任何来自 <c>Tnzi.Payment.*</c> 子包的类型。
    /// </summary>
    /// <remarks>
    /// 服务、事件处理器、选项、权限 provider 一律算数。任何一条都是父 → 子依赖，
    /// 不加载该子包的宿主会在启动或首次解析时炸在容器里 —— 那是错行为，不是少能力。
    /// </remarks>
    [Fact]
    public async Task ParentRegistersNothingFromASubPackage()
    {
        var context = await ConfiguredParentModuleAsync();

        var leaked = context.Services
            .SelectMany(d => new[] { d.ServiceType, d.ImplementationType })
            .Where(t => t != null)
            .Select(t => t!.Assembly.GetName().Name)
            .Where(n => n != null && n.StartsWith("Tnzi.Payment.", StringComparison.Ordinal))
            .Distinct()
            .ToList();

        leaked.ShouldBeEmpty();
    }

    /// <summary>
    /// 权限目录里没有 <c>payment.invoice.*</c>，但 <c>payment</c> 这个组还在。
    /// </summary>
    /// <remarks>
    /// 两半都要：码搬走了（不开票的宿主不 seed 这三个码），组必须留下 ——
    /// 子模块的 provider 用 <c>parentName: "payment"</c> 往里挂码而<b>不</b>重复 <c>AddGroup</c>，
    /// 组没了它就挂空。
    /// </remarks>
    [Fact]
    public void ParentDeclaresThePaymentGroupButNotTheInvoiceCodes()
    {
        var context = new PermissionDefinitionContext();

        new PaymentPermissions().Define(context);

        context.Groups.Keys.ShouldContain("payment");
        context.Permissions.Keys
            .Where(c => c.StartsWith("payment.invoice", StringComparison.Ordinal))
            .ShouldBeEmpty();
    }

    /// <summary>
    /// 配置中心里不再出现 Invoice 分组 —— 分组是从**已加载模块的程序集**扫出来的。
    /// </summary>
    /// <remarks>
    /// 这一条比拆分前更准：<c>InvoiceOptions</c> 留在父模块时，不开票的宿主也会看到一个
    /// "Invoice" 分组，改它一个字段都不生效。渲染出来却控制不了任何东西的设置项，
    /// 比没有这个设置项更糟。
    /// </remarks>
    [Fact]
    public void ParentAssemblyDeclaresNoInvoiceSettingsGroup()
    {
        var groupKeys = typeof(PaymentOptions).Assembly.GetTypes()
            .Select(RuntimeSettingMetadataExtractor.Extract)
            .Where(g => g != null)
            .Select(g => g!.Key)
            .ToList();

        groupKeys.ShouldContain("payment-general");
        groupKeys.ShouldNotContain("payment-invoice");
    }

    /// <summary>
    /// <see cref="PaymentOptions"/> 不再持有 <c>Invoice</c> 嵌套属性。
    /// </summary>
    /// <remarks>
    /// 留着它就得留着 <c>InvoiceOptions</c> 这个类型，那是父 → 子的编译期依赖。
    /// 配置 JSON 的形状不受影响：<c>Payment:Invoice</c> 这一节由子模块用绝对节路径自己绑。
    /// </remarks>
    [Fact]
    public void PaymentOptions_NoLongerCarriesTheInvoiceSection()
    {
        typeof(PaymentOptions).GetProperty("Invoice").ShouldBeNull();
    }

    /// <summary>
    /// 父模块的 EF 模型里没有发票实体，<c>Payment</c> 也没有指向它的导航或外键。
    /// </summary>
    /// <remarks>
    /// 拆分前 <c>PaymentConfiguration</c> 里有一段 <c>HasOne(p =&gt; p.Invoice).WithOne(i =&gt; i.Payment)</c>，
    /// 那是父 → 子的导航。删掉之后关系由依赖端（子模块的 <c>InvoiceConfiguration</c>）原样重建 ——
    /// 外键列、主键列、约束名与基数都不变，因此不产生迁移。这条守的是父模块这一侧确实断干净了。
    /// </remarks>
    [Fact]
    public void ParentModelHasNoInvoiceEntityAndNoEdgeToOne()
    {
        using var db = ModelOnlyContext();

        var entityNames = db.Model.GetEntityTypes().Select(e => e.ClrType.Name).ToList();
        entityNames.ShouldNotContain("Invoice");
        entityNames.ShouldNotContain("InvoiceLineItem");

        var payment = db.Model.FindEntityType(typeof(Entities.Payment))!;
        payment.GetNavigations().Select(n => n.Name).ShouldNotContain("Invoice");
        payment.GetForeignKeys().Select(fk => fk.PrincipalEntityType.ClrType.Name).ShouldNotContain("Invoice");
        payment.GetReferencingForeignKeys().Select(fk => fk.DeclaringEntityType.ClrType.Name).ShouldNotContain("Invoice");
    }

    /// <summary>
    /// 父模块没有任何控制器占用 <c>invoices</c> / <c>admin/invoices</c> 两条路由模板。
    /// </summary>
    /// <remarks>
    /// 这是「子模块可以把整个控制器拿走」的前提。反过来，若父模块还有控制器挂在同一个
    /// <c>[Route]</c> 上，子模块就只能把端点加在父控制器上 —— 因为 <c>[DefaultController]</c> 是
    /// <c>Inherited=false</c> 而 <c>[Route]</c> 是 <c>Inherited=true</c>，在父模板上新铸一个默认控制器
    /// 会让继承父默认控制器的消费方把子模块的端点一并继承走。
    /// </remarks>
    [Fact]
    public void ParentOwnsNeitherInvoiceRouteTemplate()
    {
        var templates = typeof(PaymentModule).Assembly.GetTypes()
            .SelectMany(t => t.GetCustomAttributes(typeof(RouteAttribute), inherit: false))
            .Cast<RouteAttribute>()
            .Select(a => a.Template)
            .ToList();

        templates.ShouldNotContain("invoices");
        templates.ShouldNotContain("admin/invoices");
    }
}
