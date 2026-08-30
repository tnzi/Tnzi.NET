using System.Reflection;
using Microsoft.Extensions.Options;
using Tnzi.Settings;
using Tnzi.Payment.Billing.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Tnzi.AspNetCore.Mvc;
using Tnzi.EventBus;
using Tnzi.Payment.Billing.Controllers;

namespace Tnzi.Payment.Billing.Tests;

/// <summary>
/// 本模块对外的三条契约：<b>权限码</b>、<b>配置节</b>、<b>路由与端点</b>。
/// 三条在拆分前后必须逐字相同 —— 它们分别是已授出去的角色行、运维手里的 appsettings.json
/// 和已经发出去的 URL，任何一条变了都不是「重构」而是一次迁移。
/// </summary>
public class BillingModuleContractTests
{
    private static async Task<ServiceConfigurationContext> ConfiguredAsync(IConfiguration? configuration = null)
    {
        var module = new PaymentBillingModule();
        var context = new ServiceConfigurationContext(
            new ServiceCollection(),
            configuration ?? new ConfigurationBuilder().Build());

        await module.PreConfigureServicesAsync(context);
        await module.ConfigureServicesAsync(context);
        return context;
    }

    // ── 权限码 ────────────────────────────────────────────────

    /// <summary>
    /// 三个码逐字节等于拆分前 <c>PaymentPermissions</c> 里那一行产生的码。
    /// </summary>
    /// <remarks>
    /// 权限码是持久化契约：已授出去的角色行按码串匹配，改名等于把所有人的授权静默清空；
    /// 管理端路由的 <c>meta.permission</c> 也是把它们当字面量写死的。
    /// </remarks>
    [Fact]
    public void PermissionCodes_AreUnchanged()
    {
        var context = new PermissionDefinitionContext();

        new PaymentBillingPermissions().Define(context);

        context.Permissions.Keys.ShouldBe(
            ["payment.invoice.view", "payment.invoice.create", "payment.invoice.update"],
            ignoreOrder: true);
    }

    /// <summary>
    /// 三个码都挂在父模块声明的 <c>payment</c> 组下，本模块**不重复 AddGroup**。
    /// </summary>
    /// <remarks>
    /// <c>AddGroup</c> 是 first-wins 的，重复声明不报错，但会让组的显示名取决于模块加载顺序 ——
    /// 一个只在某些宿主上出现的差异，最难查。
    /// </remarks>
    [Fact]
    public void PermissionCodes_HangUnderTheParentGroup_WithoutRedeclaringIt()
    {
        var context = new PermissionDefinitionContext();

        new PaymentBillingPermissions().Define(context);

        context.Groups.ShouldBeEmpty();
        context.Permissions.Values.Select(p => p.ParentName).Distinct().ShouldBe(["payment"]);
    }

    /// <summary>
    /// 没有 <c>payment.invoice.delete</c>：发票不删，作废走 <c>cancel</c>（<c>.update</c>）。
    /// </summary>
    [Fact]
    public void ThereIsNoDeletePermission()
    {
        var context = new PermissionDefinitionContext();

        new PaymentBillingPermissions().Define(context);

        context.Permissions.Keys.ShouldNotContain("payment.invoice.delete");
    }

    [Fact]
    public async Task ModuleRegistersItsPermissionProvider()
    {
        var context = await ConfiguredAsync();

        context.Services
            .Where(d => d.ServiceType == typeof(IPermissionDefinitionProvider))
            .Select(d => d.ImplementationType)
            .ShouldContain(typeof(PaymentBillingPermissions));
    }

    // ── 配置节 ────────────────────────────────────────────────

    /// <summary>
    /// 绑的还是 <c>Payment:Invoice</c> 那一节，运维手里的 appsettings.json 一个字符都不用改。
    /// </summary>
    /// <remarks>
    /// 拆分前这一节由父模块绑（<c>AddTnziOptions&lt;InvoiceOptions&gt;(config, "Payment:Invoice")</c>），
    /// 而 <c>InvoiceOptions</c> 同时还是 <c>PaymentOptions.Invoice</c> 嵌套属性。现在它由本模块
    /// 按类上的 <c>[ConfigSection("Payment:Invoice")]</c> 绝对路径自己绑 —— 走的是真实的
    /// <c>IConfiguration</c>，所以这条用例同时证明「节路径没变」与「绑定确实生效」。
    /// </remarks>
    [Fact]
    public async Task InvoiceOptions_StillBindTheSameAbsoluteSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Payment:Invoice:Enabled"] = "true",
                ["Payment:Invoice:DefaultTemplate"] = "CustomTemplate",
                ["Payment:Invoice:AutoSendOnPayment"] = "false",
                ["Payment:Invoice:CompanyName"] = "Acme",
            })
            .Build();

        var context = await ConfiguredAsync(configuration);
        var provider = context.Services.AddSingleton(configuration).BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<InvoiceOptions>>().Value;

        options.Enabled.ShouldBeTrue();
        options.DefaultTemplate.ShouldBe("CustomTemplate");
        options.AutoSendOnPayment.ShouldBeFalse();
        options.CompanyName.ShouldBe("Acme");
    }

    /// <summary>
    /// 配置中心里这一组仍叫 <c>payment-invoice</c>、仍归 <c>Payment</c> 模块、Order 仍是 520。
    /// </summary>
    /// <remarks>
    /// 分组元数据决定它在系统设置里出现在哪一格、用哪条 i18n 键。类换了程序集但这些没变，
    /// 所以界面上的位置和标签一模一样 —— 变的只是「不加载本包时它不再出现」。
    /// </remarks>
    [Fact]
    public void SettingsGroupMetadata_IsUnchanged()
    {
        var group = RuntimeSettingMetadataExtractor.Extract(typeof(InvoiceOptions));

        group.ShouldNotBeNull();
        group!.Key.ShouldBe("payment-invoice");
        group.ModuleName.ShouldBe("Payment");
        group.Order.ShouldBe(520);
        group.Fields.Select(f => f.Key).ShouldContain("Payment:Invoice:DefaultTemplate");
    }

    /// <summary>启用发票却不给模板名 → 启动期报错，而不是每张发票都悄悄落到内置 fallback。</summary>
    [Fact]
    public void Validator_RejectsAnEnabledInvoiceWithNoTemplate()
    {
        var result = new InvoiceOptionsValidator()
            .Validate(null, new InvoiceOptions { Enabled = true, DefaultTemplate = "  " });

        result.Failed.ShouldBeTrue();
    }

    [Fact]
    public void Validator_AcceptsADisabledInvoiceWithNoTemplate()
    {
        var result = new InvoiceOptionsValidator()
            .Validate(null, new InvoiceOptions { Enabled = false, DefaultTemplate = "" });

        result.Succeeded.ShouldBeTrue();
    }

    // ── 路由与端点 ────────────────────────────────────────────

    /// <summary>
    /// 两个控制器的 <c>[Route]</c> 模板与拆分前逐字相同，URL 一个字不变。
    /// </summary>
    [Fact]
    public void RouteTemplates_AreUnchanged()
    {
        RouteTemplateOf<DefaultInvoiceController>().ShouldBe("invoices");
        RouteTemplateOf<DefaultInvoiceAdminController>().ShouldBe("admin/invoices");
    }

    /// <summary>
    /// 两个控制器都带 <c>[DefaultController]</c>，因此加载本模块即自动激活。
    /// </summary>
    /// <remarks>
    /// 它们在父模块里没有任何控制器共用同一个模板，所以是整体搬过来的 ——
    /// 这正是允许铸 <c>[DefaultController]</c> 的前提：<c>[DefaultController]</c> 是
    /// <c>Inherited=false</c> 而 <c>[Route]</c> 是 <c>Inherited=true</c>，
    /// 在父模块仍占用的模板上新铸一个，会让继承父默认控制器的消费方把本模块的端点一并继承走。
    /// </remarks>
    [Fact]
    public void BothControllersAreDefaultControllers()
    {
        typeof(DefaultInvoiceController)
            .GetCustomAttributes(typeof(DefaultControllerAttribute), inherit: false).ShouldNotBeEmpty();
        typeof(DefaultInvoiceAdminController)
            .GetCustomAttributes(typeof(DefaultControllerAttribute), inherit: false).ShouldNotBeEmpty();
    }

    /// <summary>端点数量与拆分前一致：用户端 5 个，管理端 6 个。</summary>
    [Fact]
    public void EndpointCounts_AreUnchanged()
    {
        HttpEndpointCount<DefaultInvoiceController>().ShouldBe(5);
        HttpEndpointCount<DefaultInvoiceAdminController>().ShouldBe(6);
    }

    /// <summary>
    /// 管理端：类级 <c>.view</c> 门 + 每个写端点各自的操作码，与拆分前逐字相同。
    /// </summary>
    /// <remarks>
    /// 三层 AND 门里的另外一层（<c>Admin.Manage</c>）由 <c>ApiAdminControllerBase</c> 提供。
    /// 读端点只带类级 <c>.view</c>，写端点额外叠一个操作码 —— 这里把「哪几个方法是写端点」
    /// 也一并钉住：漏掉一个方法级码，等于给只有 <c>.view</c> 的人开了写权限。
    /// </remarks>
    [Fact]
    public void AdminControllerKeepsItsThreeLayerPermissionGate()
    {
        var classGate = typeof(DefaultInvoiceAdminController)
            .GetCustomAttributes(typeof(ApiAuthorizeAttribute), inherit: false)
            .Cast<ApiAuthorizeAttribute>()
            .Select(a => a.PermissionName)
            .ToList();

        classGate.ShouldBe(["payment.invoice.view"]);

        MethodGate<DefaultInvoiceAdminController>("CreateManual").ShouldBe("payment.invoice.create");
        MethodGate<DefaultInvoiceAdminController>("Send").ShouldBe("payment.invoice.update");
        MethodGate<DefaultInvoiceAdminController>("MarkAsPaid").ShouldBe("payment.invoice.update");
        MethodGate<DefaultInvoiceAdminController>("Cancel").ShouldBe("payment.invoice.update");

        // 读端点不带方法级码（只过类级 .view 那道门）
        MethodGate<DefaultInvoiceAdminController>("GetList").ShouldBeNull();
        MethodGate<DefaultInvoiceAdminController>("Get").ShouldBeNull();
    }

    // ── DI ────────────────────────────────────────────────────

    [Fact]
    public async Task ModuleRegistersTheInvoiceService()
    {
        var context = await ConfiguredAsync();

        context.Services
            .Where(d => d.ServiceType == typeof(IPaymentInvoiceService))
            .Select(d => d.ImplementationType)
            .ShouldBe([typeof(PaymentInvoiceService)]);
    }

    /// <summary>
    /// 开票器挂在父模块的 <see cref="PaymentCompletedEvent"/> 上，而且是**新增**的一个处理器。
    /// </summary>
    /// <remarks>
    /// 父模块自己在同一个事件上已经挂了两个（记日志 + 订阅计费回流）。事件总线按
    /// <c>IEnumerable&lt;IEventHandler&lt;TEvent&gt;&gt;</c> 解析，三个共存互不覆盖 ——
    /// 如果这里退化成 <c>TryAdd</c> 或某种「替换」，装了本包的宿主会静默地丢掉另外两个处理器之一。
    /// </remarks>
    [Fact]
    public async Task ModuleAddsAnInvoiceIssuerOnPaymentCompleted()
    {
        var context = await ConfiguredAsync();

        var handlers = context.Services
            .Where(d => d.ServiceType == typeof(IEventHandler<PaymentCompletedEvent>))
            .ToList();

        handlers.Select(d => d.ImplementationType).ShouldBe([typeof(InvoiceIssuingHandler)]);
        handlers.Single().Lifetime.ShouldBe(ServiceLifetime.Scoped);
    }

    /// <summary>依赖方向单向：本模块 <c>[DependsOn]</c> 父模块，父模块不认识本模块。</summary>
    [Fact]
    public void ModuleDependsOnItsParent()
    {
        typeof(PaymentBillingModule)
            .GetCustomAttributes(typeof(DependsOnAttribute), inherit: false)
            .Cast<DependsOnAttribute>()
            .SelectMany(a => a.DependedModuleTypes)
            .ShouldContain(typeof(PaymentModule));
    }

    private static string? RouteTemplateOf<T>()
        => typeof(T).GetCustomAttributes(typeof(RouteAttribute), inherit: false)
            .Cast<RouteAttribute>()
            .Select(a => a.Template)
            .SingleOrDefault();

    private static int HttpEndpointCount<T>()
        => typeof(T).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Count(m => m.GetCustomAttributes(typeof(HttpMethodAttribute), inherit: false).Length > 0);

    private static string? MethodGate<T>(string methodName)
        => typeof(T).GetMethod(methodName)!
            .GetCustomAttributes(typeof(ApiAuthorizeAttribute), inherit: false)
            .Cast<ApiAuthorizeAttribute>()
            .Select(a => a.PermissionName)
            .SingleOrDefault();
}
