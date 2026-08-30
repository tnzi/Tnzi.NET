using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Configuration;
using Tnzi.Payment.Permissions;
using Tnzi.Payment.Promotions.Permissions;

namespace Tnzi.Payment.Promotions.Tests;

/// <summary>
/// 拆分不改契约：权限码、配置节、路由模板、枚举取值、接口方法归属一律逐字不变。
/// </summary>
/// <remarks>
/// 这批断言全部对着「消费方看得见的东西」，而不是内部结构 —— 它们才是拆包真正可能弄坏的那一层。
/// </remarks>
public class PromotionsModuleContractTests
{
    private static IServiceCollection ConfiguredModule()
    {
        var services = new ServiceCollection();
        var context = new ServiceConfigurationContext(services, new ConfigurationBuilder().Build());
        var module = new PaymentPromotionsModule();
        module.PreConfigureServicesAsync(context).GetAwaiter().GetResult();
        module.ConfigureServicesAsync(context).GetAwaiter().GetResult();
        return services;
    }

    // ───────────────────────── 模块形状 ─────────────────────────

    /// <summary>
    /// 有表的子模块必须是 <c>TnziApplicationModule</c>，且前缀与父模块逐字相同。
    /// </summary>
    [Fact]
    public void ModuleShapeIsAsDeclared()
    {
        var module = new PaymentPromotionsModule();

        module.ShouldBeAssignableTo<TnziApplicationModule>();
        module.TableNamePrefix.ShouldBe("Payment");
        // Payment(50) 与前三个 Payment 子包之后
        module.LoadOrder.ShouldBe(54);
    }

    /// <summary>
    /// 依赖父模块，且<b>只</b>依赖父模块 —— 折扣是纯算术，不发通知、不出 PDF、不落文件。
    /// </summary>
    [Fact]
    public void ModuleDependsOnThePaymentCoreOnly()
    {
        typeof(PaymentPromotionsModule)
            .GetCustomAttributes(typeof(DependsOnAttribute), false)
            .Cast<DependsOnAttribute>()
            .SelectMany(a => a.DependedModuleTypes)
            .ShouldBe([typeof(PaymentModule)]);
    }

    // ───────────────────────── 权限码 ─────────────────────────

    /// <summary>
    /// 三个权限码逐字节不变，且<b>不重复声明</b> <c>payment</c> 这个组。
    /// </summary>
    /// <remarks>
    /// 码串是持久化契约：已授出去的角色行按码串匹配，改名等于把所有人的授权静默清空；
    /// 管理端路由的 <c>meta.permission</c> 也把它们当字面量写死。
    /// 组由父模块声明、子模块只往里挂码 —— <c>AddGroup</c> 是 first-wins 的，
    /// 重复声明会让组的显示名取决于模块加载顺序。
    /// </remarks>
    [Fact]
    public void PermissionCodes_AreUnchanged_AndTheGroupIsNotRedeclared()
    {
        var context = new PermissionDefinitionContext();

        new PaymentPromotionsPermissions().Define(context);

        context.Permissions.Keys.OrderBy(k => k, StringComparer.Ordinal).ShouldBe(
        [
            "payment.promotion.create",
            "payment.promotion.update",
            "payment.promotion.view",
        ]);

        context.Groups.ShouldBeEmpty("组由父模块的 PaymentPermissions 声明，子模块只挂码");
    }

    /// <summary>
    /// 刻意<b>没有</b> <c>payment.promotion.delete</c>，与拆分前一致。
    /// </summary>
    /// <remarks>
    /// 促销只停用不删除：一条已经被人核销过的促销删掉，核销记录就成了指向虚空的孤儿。
    /// 顺手补齐一个「看起来该有」的 delete 码，等于凭空多发一项没人实现的能力。
    /// </remarks>
    [Fact]
    public void ThereIsDeliberatelyNoDeleteCode()
    {
        var context = new PermissionDefinitionContext();
        new PaymentPromotionsPermissions().Define(context);

        context.Permissions.Keys.ShouldNotContain("payment.promotion.delete");
    }

    /// <summary>父组仍是父模块声明的 <c>payment</c>。</summary>
    [Fact]
    public void TheParentStillDeclaresTheGroupTheseCodesHangUnder()
    {
        var parent = new PermissionDefinitionContext();
        new PaymentPermissions().Define(parent);

        parent.Groups.Keys.ShouldContain("payment");
    }

    /// <summary>权限 provider 真的注册了 —— 不注册的话这三个码没人 seed，端点永远拒绝。</summary>
    [Fact]
    public void TheModuleRegistersItsPermissionProvider()
    {
        ConfiguredModule()
            .Where(d => d.ServiceType == typeof(IPermissionDefinitionProvider))
            .Select(d => d.ImplementationType)
            .ShouldContain(typeof(PaymentPromotionsPermissions));
    }

    // ───────────────────────── 配置节 ─────────────────────────

    /// <summary>
    /// 配置节路径仍是绝对的 <c>Payment:Promotion</c>，运维手上的 appsettings.json 一个字符都不用改。
    /// </summary>
    [Fact]
    public void ConfigSectionPathIsUnchanged()
    {
        typeof(PromotionOptions).GetCustomAttributes(typeof(ConfigSectionAttribute), false)
            .Cast<ConfigSectionAttribute>()
            .Single()
            .Section.ShouldBe("Payment:Promotion");
    }

    /// <summary>
    /// 配置中心分组的 key / 模块名 / 显示名 / 排序都不变 —— 它们决定设置页上这一块长什么样、排在哪。
    /// </summary>
    [Fact]
    public void RuntimeSettingsGroupIsUnchanged()
    {
        var group = RuntimeSettingMetadataExtractor.Extract(typeof(PromotionOptions));

        group.ShouldNotBeNull();
        group!.Key.ShouldBe("payment-promotion");
        group.ModuleName.ShouldBe("Payment");
        group.DisplayName.ShouldBe("Promotion");
        group.Order.ShouldBe(530);
    }

    /// <summary>那一条校验规则与文案逐字不变，只是搬了地方。</summary>
    [Fact]
    public void ValidatorKeepsTheOriginalWording()
    {
        var result = new PromotionOptionsValidator()
            .Validate(name: null, new PromotionOptions { MaxCouponUsagePerUser = 0 });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Promotion:MaxCouponUsagePerUser must be greater than 0.");
    }

    /// <summary>出厂默认值不变 —— 它们是没配置时真正生效的那一组。</summary>
    [Fact]
    public void DefaultsAreUnchanged()
    {
        var options = new PromotionOptions();

        options.MaxCouponUsagePerUser.ShouldBe(5);
        options.EnableStripeCouponSync.ShouldBeTrue();
    }

    // ───────────────────────── 路由与端点 ─────────────────────────

    /// <summary>
    /// 两条路由模板与端点数量都不变：<c>promotions</c> 6 个、<c>admin/promotions</c> 9 个。
    /// </summary>
    /// <remarks>
    /// 路由是已经发出去的 URL，属于持久化契约。端点数固定住是为了让「顺手删了一个端点」
    /// 或者「悄悄加了一个未授权的写端点」立刻现形。
    /// </remarks>
    [Theory]
    [InlineData(typeof(Controllers.DefaultPromotionController), "promotions", 6)]
    [InlineData(typeof(Controllers.DefaultPromotionAdminController), "admin/promotions", 9)]
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
    /// <c>payment.promotion.view</c> 的人从此也能建促销、改促销、铸兑换码、给任意用户发券。
    /// </remarks>
    [Fact]
    public void EveryAdminWriteEndpointKeepsItsMethodLevelPermission()
    {
        var writes = typeof(Controllers.DefaultPromotionAdminController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes(typeof(HttpMethodAttribute), false)
                .Cast<HttpMethodAttribute>()
                .Any(a => a.HttpMethods.Any(h => h is "POST" or "PUT" or "DELETE" or "PATCH")))
            .ToList();

        writes.Count.ShouldBe(6);

        foreach (var method in writes)
        {
            var code = method.GetCustomAttributes(typeof(ApiAuthorizeAttribute), false)
                .Cast<ApiAuthorizeAttribute>()
                .SingleOrDefault()?.PermissionName;

            code.ShouldNotBeNullOrWhiteSpace($"{method.Name} 是写端点却没有方法级权限码");
            code.ShouldStartWith("payment.promotion.");
        }
    }

    /// <summary>类级门是 <c>payment.promotion.view</c>，与拆分前一致。</summary>
    [Fact]
    public void TheAdminControllerKeepsItsClassLevelViewGate()
    {
        typeof(Controllers.DefaultPromotionAdminController)
            .GetCustomAttributes(typeof(ApiAuthorizeAttribute), false)
            .Cast<ApiAuthorizeAttribute>()
            .Single()
            .PermissionName.ShouldBe("payment.promotion.view");
    }

    // ───────────────────────── 枚举取值 ─────────────────────────

    /// <summary>
    /// 搬过来的五个枚举取值一字未动 —— 它们是数据库列的存储值，也是前端镜像枚举的线上格式。
    /// </summary>
    [Fact]
    public void MovedEnumValuesAreUnchanged()
    {
        ((int)PromotionType.PercentageDiscount).ShouldBe(1);
        ((int)PromotionType.FixedAmountDiscount).ShouldBe(2);
        ((int)PromotionType.FirstSubscription).ShouldBe(3);
        ((int)PromotionType.LimitedTime).ShouldBe(4);
        ((int)PromotionType.ThresholdDiscount).ShouldBe(5);

        ((int)ApplyScope.Global).ShouldBe(0);
        ((int)ApplyScope.Plan).ShouldBe(1);
        ((int)ApplyScope.Product).ShouldBe(2);

        ((int)RedemptionCodeType.Unique).ShouldBe(1);
        ((int)RedemptionCodeType.General).ShouldBe(2);

        ((int)RedemptionCodeStatus.Active).ShouldBe(1);
        ((int)RedemptionCodeStatus.Inactive).ShouldBe(2);
        ((int)RedemptionCodeStatus.Expired).ShouldBe(3);

        ((int)UserCouponStatus.Available).ShouldBe(0);
        ((int)UserCouponStatus.Used).ShouldBe(1);
        ((int)UserCouponStatus.Expired).ShouldBe(2);
        ((int)UserCouponStatus.Revoked).ShouldBe(3);
    }

    /// <summary>
    /// <c>DiscountType</c> 与 <c>ProductType</c> 留在了<b>父模块</b>的命名空间里。
    /// </summary>
    /// <remarks>
    /// ★ 同一个 <c>#region</c> 里的七个枚举只搬走五个，这两个不能搬：
    /// <c>DiscountType</c> 在父模块的 <c>PaymentChannelCouponDto</c> 上（渠道同步契约的入参，
    /// 唯一实现住在 <c>Tnzi.Payment.Stripe</c>），<c>ProductType</c> 由父模块的
    /// <c>PaymentService</c> 从 <c>BusinessType</c> 现算并写进同样留在父模块的
    /// <c>CouponApplyContext</c>。搬走任何一个，只加载「支付 + 渠道包」的宿主就编不过。
    /// </remarks>
    [Fact]
    public void DiscountTypeAndProductTypeStayInTheParentAssembly()
    {
        typeof(DiscountType).Assembly.ShouldBe(typeof(PaymentModule).Assembly);
        typeof(ProductType).Assembly.ShouldBe(typeof(PaymentModule).Assembly);

        // 反过来，搬走的五个确实在本程序集
        typeof(PromotionType).Assembly.ShouldBe(typeof(PaymentPromotionsModule).Assembly);
        typeof(ApplyScope).Assembly.ShouldBe(typeof(PaymentPromotionsModule).Assembly);
        typeof(UserCouponStatus).Assembly.ShouldBe(typeof(PaymentPromotionsModule).Assembly);
    }

    /// <summary>
    /// 留在父模块的三个 DTO 也确实在父程序集里 —— 它们在父模块自己的 <c>ICouponService</c> 签名上。
    /// </summary>
    [Fact]
    public void TheThreeContractDtosStayInTheParentAssembly()
    {
        typeof(CouponApplyContext).Assembly.ShouldBe(typeof(PaymentModule).Assembly);
        typeof(CouponPreviewDto).Assembly.ShouldBe(typeof(PaymentModule).Assembly);
        typeof(CouponUsageDto).Assembly.ShouldBe(typeof(PaymentModule).Assembly);

        // 其余 12 个随本模块走
        typeof(PromotionDto).Assembly.ShouldBe(typeof(PaymentPromotionsModule).Assembly);
        typeof(UserCouponDto).Assembly.ShouldBe(typeof(PaymentPromotionsModule).Assembly);
    }

    // ───────────────────────── 接口划分 ─────────────────────────

    /// <summary>
    /// 留在父模块的 <see cref="ICouponService"/> 恰好只有支付流程用得到的四个方法。
    /// </summary>
    /// <remarks>
    /// 判据是逐个查调用方：摘掉的六个各自<b>只有一个</b>调用方，而且都是随本模块搬走的促销控制器。
    /// 多留一个方法就意味着多把一个 DTO 钉在父模块 —— <c>UserCouponDto</c> 就是这么被钉住的。
    /// </remarks>
    [Fact]
    public void TheParentCouponContractIsNarrowedToTheFourPaymentFlowMethods()
    {
        typeof(ICouponService).GetMethods().Select(m => m.Name).OrderBy(x => x, StringComparer.Ordinal)
            .ShouldBe(["ApplyCouponAsync", "PreviewAsync", "ReleaseCouponAsync", "ReleaseCouponForPaymentAsync"]);
    }

    /// <summary>券包与发券分成两个接口，六个方法各归各位。</summary>
    /// <remarks>
    /// 分开而不是合成一个，是因为「用我自己的身份读 / 换」与「凭管理员权限凭空发出去」
    /// 是两种能力。合成一个接口，任何为了显示券包而注入它的地方都顺手拿到了铸码与发券。
    /// </remarks>
    [Fact]
    public void TheWalletAndIssuanceContractsCarryTheSixMovedMethods()
    {
        typeof(ICouponWalletService).GetMethods().Select(m => m.Name).OrderBy(x => x, StringComparer.Ordinal)
            .ShouldBe([
                "CanUseFirstSubscriptionDiscountAsync",
                "GetUserAvailableCouponsAsync",
                "GetUserUsedCouponsAsync",
                "RedeemAsync",
            ]);

        typeof(ICouponIssuanceService).GetMethods().Select(m => m.Name).OrderBy(x => x, StringComparer.Ordinal)
            .ShouldBe(["CreateRedemptionCodeAsync", "GrantAsync"]);
    }

    /// <summary>
    /// 三个优惠券接口必须解析到<b>同一个</b> Scoped 实例。
    /// </summary>
    /// <remarks>
    /// 分别注册三份实现编译照过、单测照绿，但同一次请求里会出现三份各自持有 DbContext
    /// 变更跟踪的副本 —— 核销写在一份上、还券读的是另一份。
    /// </remarks>
    [Fact]
    public void TheThreeCouponContractsResolveToOneInstance()
    {
        var services = ConfiguredModule();
        services.AddScoped(_ => new Mock<IRepository<CouponUsage, Guid>>().Object);
        services.AddScoped(_ => new Mock<IRepository<RedemptionCode, Guid>>().Object);
        services.AddScoped(_ => new Mock<IRepository<UserCoupon, Guid>>().Object);
        services.AddScoped(_ => new Mock<IRepository<Promotion, Guid>>().Object);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var asPaymentContract = scope.ServiceProvider.GetRequiredService<ICouponService>();
        var asWallet = scope.ServiceProvider.GetRequiredService<ICouponWalletService>();
        var asIssuance = scope.ServiceProvider.GetRequiredService<ICouponIssuanceService>();

        asWallet.ShouldBeSameAs(asPaymentContract);
        asIssuance.ShouldBeSameAs(asPaymentContract);
    }

    /// <summary>促销服务与父模块统计的促销供给方都由本模块注册。</summary>
    [Fact]
    public void TheModuleRegistersItsServices()
    {
        var registered = ConfiguredModule().Select(d => d.ServiceType).ToHashSet();

        registered.ShouldContain(typeof(IPromotionService));
        registered.ShouldContain(typeof(ICouponService));
        registered.ShouldContain(typeof(ICouponWalletService));
        registered.ShouldContain(typeof(ICouponIssuanceService));
        registered.ShouldContain(typeof(IPromotionAnalyticsProvider));
    }
}
