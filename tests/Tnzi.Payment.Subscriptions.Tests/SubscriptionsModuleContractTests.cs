using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Configuration;
using Tnzi.EventBus;

namespace Tnzi.Payment.Subscriptions.Tests;

/// <summary>
/// 拆分不改契约：权限码、配置节、路由模板、枚举取值、事件订阅一律逐字不变。
/// </summary>
/// <remarks>
/// 这批断言全部对着「消费方看得见的东西」，而不是内部结构 —— 它们才是拆包真正可能弄坏的那一层。
/// </remarks>
public class SubscriptionsModuleContractTests
{
    private static IServiceCollection ConfiguredModule()
    {
        var services = new ServiceCollection();
        var context = new ServiceConfigurationContext(services, new ConfigurationBuilder().Build());
        var module = new PaymentSubscriptionsModule();
        module.PreConfigureServicesAsync(context).GetAwaiter().GetResult();
        module.ConfigureServicesAsync(context).GetAwaiter().GetResult();
        return services;
    }

    // ───────────────────────── 权限码 ─────────────────────────

    /// <summary>
    /// 四个权限码逐字节不变，且<b>不重复声明</b> <c>payment</c> 这个组。
    /// </summary>
    /// <remarks>
    /// 码串是持久化契约：已授出去的角色行按码串匹配，改名等于把所有人的授权静默清空；
    /// 管理端路由的 <c>meta.permission</c> 也把它们当字面量写死。
    /// 组由父模块声明、子模块只往里挂码，两块在授权界面里合并成一棵树 ——
    /// <c>AddGroup</c> 是 first-wins 的，重复声明会让组的显示名取决于模块加载顺序。
    /// </remarks>
    [Fact]
    public void PermissionCodes_AreUnchanged_AndTheGroupIsNotRedeclared()
    {
        var context = new PermissionDefinitionContext();

        new PaymentSubscriptionsPermissions().Define(context);

        context.Permissions.Keys.OrderBy(k => k, StringComparer.Ordinal).ShouldBe(
        [
            "payment.subscription.create",
            "payment.subscription.delete",
            "payment.subscription.update",
            "payment.subscription.view",
        ]);

        context.Groups.ShouldBeEmpty("组由父模块的 PaymentPermissions 声明，子模块只挂码");
    }

    /// <summary>权限 provider 真的注册了 —— 不注册的话这四个码没人 seed，端点永远拒绝。</summary>
    [Fact]
    public void TheModuleRegistersItsPermissionProvider()
    {
        ConfiguredModule()
            .Where(d => d.ServiceType == typeof(IPermissionDefinitionProvider))
            .Select(d => d.ImplementationType)
            .ShouldContain(typeof(PaymentSubscriptionsPermissions));
    }

    // ───────────────────────── 配置节 ─────────────────────────

    /// <summary>
    /// 配置节路径仍是绝对的 <c>Payment:Subscription</c>，运维手上的 appsettings.json 一个字符都不用改。
    /// </summary>
    [Fact]
    public void ConfigSectionPathIsUnchanged()
    {
        typeof(SubscriptionOptions).GetCustomAttributes(typeof(ConfigSectionAttribute), false)
            .Cast<ConfigSectionAttribute>()
            .Single()
            .Section.ShouldBe("Payment:Subscription");
    }

    /// <summary>
    /// 配置中心分组的 key / 模块名 / 图标 / 排序都不变 —— 它们决定设置页上这一块长什么样、排在哪。
    /// </summary>
    [Fact]
    public void RuntimeSettingsGroupIsUnchanged()
    {
        var group = RuntimeSettingMetadataExtractor.Extract(typeof(SubscriptionOptions));

        group.ShouldNotBeNull();
        group!.Key.ShouldBe("payment-subscription");
        group.ModuleName.ShouldBe("Payment");
        group.DisplayName.ShouldBe("Subscription");
        group.Order.ShouldBe(510);
    }

    /// <summary>
    /// 五条校验规则与文案逐字不变，只是搬了地方。
    /// </summary>
    /// <remarks>
    /// 文案里的 <c>Subscription:</c> 前缀在拆分前是嵌套属性路径，拆分后正好也是
    /// <c>Payment:Subscription</c> 节内的键名 —— 运维看到的字符串没变。
    /// </remarks>
    [Theory]
    [InlineData(nameof(SubscriptionOptions.AutoRenewalReminderDays), "Subscription:AutoRenewalReminderDays")]
    [InlineData(nameof(SubscriptionOptions.GracePeriodDays), "Subscription:GracePeriodDays")]
    [InlineData(nameof(SubscriptionOptions.MaxRetryCount), "Subscription:MaxRetryCount")]
    [InlineData(nameof(SubscriptionOptions.DefaultTrialDays), "Subscription:DefaultTrialDays")]
    [InlineData(nameof(SubscriptionOptions.MaxPauseDays), "Subscription:MaxPauseDays")]
    public void ValidatorRejectsNegativeValues_WithTheOriginalWording(string propertyName, string expectedPrefix)
    {
        var options = new SubscriptionOptions();
        typeof(SubscriptionOptions).GetProperty(propertyName)!.SetValue(options, -1);

        var result = new SubscriptionOptionsValidator().Validate(name: null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain(expectedPrefix + " cannot be negative.");
    }

    /// <summary>出厂默认值不变 —— 它们是没配置时真正生效的那一组。</summary>
    [Fact]
    public void DefaultsAreUnchanged()
    {
        var options = new SubscriptionOptions();

        options.AutoRenewalReminderDays.ShouldBe(7);
        options.GracePeriodDays.ShouldBe(3);
        options.MaxRetryCount.ShouldBe(3);
        options.DefaultTrialDays.ShouldBe(14);
        options.MaxPauseDays.ShouldBe(90);
    }

    // ───────────────────────── 路由与端点 ─────────────────────────

    /// <summary>
    /// 两条路由模板与端点数量都不变：<c>subscriptions</c> 14 个、<c>admin/subscriptions</c> 12 个。
    /// </summary>
    /// <remarks>
    /// 路由是已经发出去的 URL，属于持久化契约。端点数固定住是为了让「顺手删了一个端点」
    /// 或者「悄悄加了一个未授权的写端点」立刻现形。
    /// </remarks>
    [Theory]
    [InlineData(typeof(Controllers.DefaultSubscriptionController), "subscriptions", 14)]
    [InlineData(typeof(Controllers.DefaultSubscriptionAdminController), "admin/subscriptions", 12)]
    public void RouteTemplatesAndEndpointCountsAreUnchanged(Type controller, string template, int endpointCount)
    {
        controller.GetCustomAttributes(typeof(RouteAttribute), false)
            .Cast<RouteAttribute>()
            .Single()
            .Template.ShouldBe(template);

        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Count(m => m.GetCustomAttributes(typeof(HttpMethodAttribute), false).Length > 0)
            .ShouldBe(endpointCount);
    }

    /// <summary>
    /// 管理端每一个<b>写</b>端点都仍带方法级权限码，读端点只吃类级 <c>.view</c>。
    /// </summary>
    /// <remarks>
    /// 三层 AND 门里最容易在搬迁中掉的就是方法级那一层：掉了不会报错，只是所有拿到
    /// <c>payment.subscription.view</c> 的人从此也能改计划、删计划、代客取消订阅。
    /// </remarks>
    [Fact]
    public void EveryAdminWriteEndpointKeepsItsMethodLevelPermission()
    {
        var writes = typeof(Controllers.DefaultSubscriptionAdminController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes(typeof(HttpMethodAttribute), false)
                .Cast<HttpMethodAttribute>()
                .Any(a => a.HttpMethods.Any(h => h is "POST" or "PUT" or "DELETE" or "PATCH")))
            .ToList();

        writes.Count.ShouldBe(9);

        foreach (var method in writes)
        {
            var code = method.GetCustomAttributes(typeof(ApiAuthorizeAttribute), false)
                .Cast<ApiAuthorizeAttribute>()
                .SingleOrDefault()?.PermissionName;

            code.ShouldNotBeNullOrWhiteSpace($"{method.Name} 是写端点却没有方法级权限码");
            code.ShouldStartWith("payment.subscription.");
        }
    }

    /// <summary>类级门是 <c>payment.subscription.view</c>，与拆分前一致。</summary>
    [Fact]
    public void TheAdminControllerKeepsItsClassLevelViewGate()
    {
        typeof(Controllers.DefaultSubscriptionAdminController)
            .GetCustomAttributes(typeof(ApiAuthorizeAttribute), false)
            .Cast<ApiAuthorizeAttribute>()
            .Single()
            .PermissionName.ShouldBe("payment.subscription.view");
    }

    // ───────────────────────── 枚举取值 ─────────────────────────

    /// <summary>
    /// 五个枚举的取值一字未动 —— 它们是数据库列的存储值，也是前端镜像枚举的线上格式。
    /// </summary>
    [Fact]
    public void EnumValuesAreUnchanged()
    {
        ((int)SubscriptionStatus.Pending).ShouldBe(0);
        ((int)SubscriptionStatus.Trial).ShouldBe(1);
        ((int)SubscriptionStatus.Active).ShouldBe(2);
        ((int)SubscriptionStatus.PendingRenewal).ShouldBe(3);
        ((int)SubscriptionStatus.Paused).ShouldBe(4);
        ((int)SubscriptionStatus.Cancelled).ShouldBe(5);
        ((int)SubscriptionStatus.Expired).ShouldBe(6);
        ((int)SubscriptionStatus.PastDue).ShouldBe(7);

        ((int)BillingCycleType.Day).ShouldBe(1);
        ((int)BillingCycleType.Week).ShouldBe(2);
        ((int)BillingCycleType.Month).ShouldBe(3);
        ((int)BillingCycleType.Year).ShouldBe(4);
        ((int)BillingCycleType.OneTime).ShouldBe(5);

        ((int)SubscriptionBillingPurpose.Initial).ShouldBe(0);
        ((int)SubscriptionBillingPurpose.Renewal).ShouldBe(1);
        ((int)SubscriptionBillingPurpose.TrialConversion).ShouldBe(2);
        ((int)SubscriptionBillingPurpose.Proration).ShouldBe(3);

        ((int)SubscriptionChangeType.Upgrade).ShouldBe(1);
        ((int)SubscriptionChangeType.Downgrade).ShouldBe(2);
        ((int)SubscriptionChangeType.CrossGrade).ShouldBe(3);

        ((int)SubscriptionChangeStatus.Pending).ShouldBe(0);
        ((int)SubscriptionChangeStatus.Applied).ShouldBe(1);
        ((int)SubscriptionChangeStatus.Cancelled).ShouldBe(2);
    }

    // ───────────────────────── 装配 ─────────────────────────

    /// <summary>订阅服务本身由本模块注册。</summary>
    [Fact]
    public void TheModuleRegistersTheSubscriptionService()
    {
        ConfiguredModule()
            .Single(d => d.ServiceType == typeof(ISubscriptionService))
            .ImplementationType.ShouldBe(typeof(SubscriptionService));
    }

    /// <summary>
    /// 六条后台扫描全部贡献给了父模块的后台循环，前五条的名字与拆分前的日志文案逐字相同。
    /// </summary>
    /// <remarks>
    /// 少注册一条不会报错，表现是「某一类订阅从此再也没被处理过」—— 比如少了续费那条，
    /// 所有订阅到期后安静地停在原地，既不扣款也不过期；少了结算待生效变更那条，
    /// 不参与续费的订阅（关掉自动续费、暂停中、逾期）约定好的降级永远不会发生。
    /// </remarks>
    [Fact]
    public void AllScheduledScansAreContributed()
    {
        var scans = ConfiguredModule()
            .Where(d => d.ServiceType == typeof(IPaymentScheduledScan))
            .Select(d => d.ImplementationType!)
            .ToList();

        scans.Count.ShouldBe(6);

        var names = scans
            .Select(t => (IPaymentScheduledScan)Activator.CreateInstance(t, new Mock<ISubscriptionService>().Object)!)
            .Select(s => s.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        names.ShouldBe(
        [
            "apply due plan changes",
            "convert due trials",
            "expire overdue subscriptions",
            "renew due subscriptions",
            "resume due paused subscriptions",
            "send renewal reminders",
        ]);
    }

    /// <summary>
    /// 三个「回流」处理器订的是<b>父模块的</b>支付事件，由本模块注册。
    /// </summary>
    /// <remarks>
    /// 少了它们，订阅相关的支付照常收得成，但订阅状态机<b>永远不会被推进</b>——
    /// 用户付了钱，订阅还停在 Pending / PastDue。这是「钱收了服务没给」，不是少一项能力。
    /// </remarks>
    [Theory]
    [InlineData(typeof(PaymentCompletedEvent), typeof(SubscriptionPaymentCompletedHandler))]
    [InlineData(typeof(PaymentFailedEvent), typeof(SubscriptionPaymentFailedHandler))]
    [InlineData(typeof(PaymentExpiredEvent), typeof(SubscriptionPaymentExpiredHandler))]
    public void TheModuleRoutesPaymentEventsBackIntoTheSubscriptionStateMachine(Type eventType, Type handler)
    {
        var serviceType = typeof(IEventHandler<>).MakeGenericType(eventType);

        ConfiguredModule()
            .Where(d => d.ServiceType == serviceType)
            .Select(d => d.ImplementationType)
            .ShouldContain(handler);
    }

    /// <summary>模块的加载顺序与依赖声明。</summary>
    [Fact]
    public void ModuleShapeIsAsDeclared()
    {
        var module = new PaymentSubscriptionsModule();

        module.LoadOrder.ShouldBe(53);
        module.TableNamePrefix.ShouldBe("Payment");

        typeof(PaymentSubscriptionsModule)
            .GetCustomAttributes(typeof(DependsOnAttribute), false)
            .Cast<DependsOnAttribute>()
            .SelectMany(a => a.DependedModuleTypes)
            .ShouldContain(typeof(PaymentModule));
    }
}
