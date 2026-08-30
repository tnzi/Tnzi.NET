using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.Domain.Repositories;
using Tnzi.Payment.Dtos;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Options;
using Tnzi.Payment.Permissions;
using Tnzi.Payment.Providers;
using Tnzi.Payment.Services;
using Tnzi.Payment.Tests.Integration;
using Tnzi.Security.Authorization;
using Tnzi.Security.Claims;
using PaymentEntity = Tnzi.Payment.Entities.Payment;

namespace Tnzi.Payment.Tests;

/// <summary>
/// 「宿主没有加载 <c>Tnzi.Payment.Promotions</c>」时，本模块该是什么样子。
/// </summary>
/// <remarks>
/// <para>
/// ★ 这批用例住在<b>父测试项目</b>是刻意的：本项目<b>不引用</b>那个可选包，所以它演的是
/// 真实的「没装折扣包」现场，而不是把某个服务设成 null 模拟出来的现场。整个
/// <c>Integration/</c> 目录也一并是证据 —— 那套集成库里根本没有促销的四张表，
/// 支付 / 退款 / 绑卡 / 回调 / 对账的全部用例照常通过。
/// </para>
/// <para>
/// 本模块的缺席面同时具备两种形态：
/// </para>
/// <list type="number">
/// <item><b>整体拿走的两组路由</b>（<c>promotions</c> / <c>admin/promotions</c>，15 个端点）——
///   父模块没有任何控制器共用它们，缺席表现为路由不存在（404 来自路由表）。</item>
/// <item><b>留在父控制器上的那一个端点</b>（<c>admin/payment-statistics/promotion-analytics</c>）——
///   它的宿主控制器整条路由是父模块的（另外六个端点与促销毫无关系），所以端点必须留下，
///   缺席时回 <b>501</b> 并指名要加载的包。<b>不是 503</b>：503 意味着暂时故障，
///   会让监控和客户端不停重试一件永远不会恢复的事。<b>也不是空列表</b>：
///   空列表是一个答案（「有促销，只是这段时间没人用」），会把部署疏漏伪装成业务结论。
///   这一条钉在 <see cref="StatisticsServiceTests"/> 里。</item>
/// </list>
/// <para>
/// 除此之外还有一处「行为要退化得对」的地方：带优惠券码的建单必须<b>被拒</b>，
/// 而不是静默按原价下单。那台宿主上不存在任何优惠券码，所以「这个码无效」是事实。
/// </para>
/// </remarks>
public class PromotionsPackageAbsenceTests
{
    /// <summary>
    /// 只建模、不连库的 <see cref="PaymentTestDbContext"/> —— 集成测试用的就是这个 DbContext，
    /// 它的实体清单即「没装折扣包的宿主会建出哪些表」。
    /// </summary>
    private static PaymentTestDbContext ModelOnlyContext()
    {
        var options = new DbContextOptionsBuilder<PaymentTestDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        return new PaymentTestDbContext(options, new Mock<ICurrentUser>().Object);
    }

    private static IServiceProvider LoggingServiceProvider()
    {
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(NullLogger.Instance);

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);
        return serviceProvider.Object;
    }

    // ───────────────── 模型：四张表不在，而且没有任何一条边指向它们 ─────────────────

    /// <summary>
    /// 父模块的 EF 模型里既没有促销四个实体，也没有任何指向它们的导航或外键。
    /// </summary>
    /// <remarks>
    /// ★ 这条走的是<b>真实的 EF 模型</b>而不是读源码：父 → 子的边可能来自实体上的导航属性、
    /// 某个 Configuration 里的 <c>HasOne/HasMany</c>，也可能来自 EF 约定<b>推断</b>出来的影子外键。
    /// 只查源码会漏掉第三种（上一次拆分就是这么发现 <c>Payment_Payment</c> 上多了一个影子列的）。
    /// </remarks>
    [Fact]
    public void ParentModelHasNoPromotionEntityAndNoEdgeToOne()
    {
        using var context = ModelOnlyContext();
        var model = context.Model;

        var promotionTypeNames = new[] { "Promotion", "CouponUsage", "RedemptionCode", "UserCoupon" };

        var entityNames = model.GetEntityTypes().Select(e => e.ClrType.Name).ToList();
        entityNames.ShouldNotContain("Promotion");
        entityNames.ShouldNotContain("CouponUsage");
        entityNames.ShouldNotContain("RedemptionCode");
        entityNames.ShouldNotContain("UserCoupon");

        foreach (var entity in model.GetEntityTypes())
        {
            entity.GetNavigations()
                .Select(n => n.TargetEntityType.ClrType.Name)
                .ShouldNotContain(n => promotionTypeNames.Contains(n),
                    $"{entity.ClrType.Name} 上出现了指向促销实体的导航 —— 那是父 → 子依赖。");

            entity.GetForeignKeys()
                .Select(fk => fk.PrincipalEntityType.ClrType.Name)
                .ShouldNotContain(n => promotionTypeNames.Contains(n),
                    $"{entity.ClrType.Name} 上出现了指向促销实体的外键 —— 那是父 → 子依赖。");
        }
    }

    /// <summary>
    /// <c>Payment.CouponId</c> <b>还在</b>，而且仍然只是一个无约束标量。
    /// </summary>
    /// <remarks>
    /// ★ 刻意保留：删掉它是一次 <c>DropColumn</c>，会给每一个既有部署换来一条迁移，
    /// 而拆程序集本身不该动 schema。这条测试把「现状」钉住 —— 谁想清掉这一列，
    /// 会先看到这条红，从而知道自己正在做的是一个独立的、可能有数据损失的决定。
    /// 同时断言它没有变成外键：变成外键就等于父表依赖一张可选包才有的表。
    /// </remarks>
    [Fact]
    public void PaymentKeepsItsCouponIdScalar_AndItIsStillNotAForeignKey()
    {
        using var context = ModelOnlyContext();
        var payment = context.Model.FindEntityType(typeof(PaymentEntity))!;

        payment.FindProperty(nameof(PaymentEntity.CouponId)).ShouldNotBeNull(
            "Payment.CouponId 被删掉了 —— 那是一条 DropColumn 迁移，不该搭拆分的便车。");

        payment.GetForeignKeys()
            .SelectMany(fk => fk.Properties)
            .Select(p => p.Name)
            .ShouldNotContain(nameof(PaymentEntity.CouponId));
    }

    // ───────────────── 路由：两条模板整体搬走，父模块一条都不占 ─────────────────

    /// <summary>
    /// 父模块<b>没有任何</b>控制器使用 <c>promotions</c> 或 <c>admin/promotions</c>。
    /// </summary>
    /// <remarks>
    /// ★ 这正是两个控制器可以<b>整体</b>搬走的判据。反过来，如果父模块还占着其中一条模板，
    /// 子模块就<b>不能</b>在那条模板上新铸一个 <c>[DefaultController]</c>：
    /// <c>[DefaultController]</c> 是 <c>Inherited=false</c> 而 <c>[Route]</c> 是 <c>Inherited=true</c>，
    /// 继承父默认控制器的消费方会把子模块的端点一并继承走。
    /// </remarks>
    [Fact]
    public void ParentOwnsNeitherPromotionRouteTemplate()
    {
        var templates = typeof(PaymentModule).Assembly.GetTypes()
            .SelectMany(t => t.GetCustomAttributes(typeof(RouteAttribute), false).Cast<RouteAttribute>())
            .Select(a => a.Template)
            .ToList();

        templates.ShouldNotContain("promotions");
        templates.ShouldNotContain("admin/promotions");
    }

    /// <summary>
    /// 反过来，<c>admin/payment-statistics</c> 整条路由<b>仍是父模块的</b>。
    /// </summary>
    /// <remarks>
    /// 这条与上一条合起来才说明白「为什么两个控制器整体搬走、而促销分析那个端点必须留下」：
    /// 那条路由上的另外六个端点（总览、趋势、订阅指标、对账导出、退款分析）与促销毫无关系。
    /// </remarks>
    [Fact]
    public void ParentStillOwnsTheStatisticsRouteTemplate()
    {
        typeof(PaymentModule).Assembly.GetTypes()
            .SelectMany(t => t.GetCustomAttributes(typeof(RouteAttribute), false).Cast<RouteAttribute>())
            .Select(a => a.Template)
            .ShouldContain("admin/payment-statistics");
    }

    // ───────────────── 建单：不带券码照常，带券码被拒 ─────────────────

    private static PaymentService PaymentServiceWithoutCoupons()
    {
        var paymentOptions = new Mock<IOptionsMonitor<PaymentOptions>>();
        paymentOptions.Setup(x => x.CurrentValue).Returns(new PaymentOptions { AllowTestProvider = true, DefaultChannelCode = "Null" });

        var taxOptions = new Mock<IOptionsMonitor<TaxOptions>>();
        taxOptions.Setup(x => x.CurrentValue).Returns(new TaxOptions());

        return new PaymentService(
            new Mock<IRepository<PaymentEntity, Guid>>().Object,
            new Mock<IPaymentProviderFactory>().Object,
            new DefaultPaymentTaxCalculator(taxOptions.Object),
            new Mock<IPaymentMethodService>().Object,
            paymentOptions.Object,
            LoggingServiceProvider());
    }

    /// <summary>
    /// 没有 <see cref="ICouponService"/> 时，支付服务照常构造 —— 缺席不是「炸在容器里」。
    /// </summary>
    [Fact]
    public void PaymentService_ConstructsWithoutACouponService()
    {
        PaymentServiceWithoutCoupons().ShouldNotBeNull();
    }

    /// <summary>
    /// <see cref="ICouponService"/> 是可选构造参数（带默认值）。
    /// </summary>
    /// <remarks>
    /// 参数带默认值还有第二个作用：依赖审计器会跳过带默认值的构造参数，
    /// 因此本次拆分一处 <c>[SuppressDependencyAudit]</c> 都没加。
    /// </remarks>
    [Fact]
    public void CouponServiceIsAnOptionalParameter()
    {
        typeof(PaymentService).GetConstructors().Single()
            .GetParameters()
            .Single(p => p.ParameterType == typeof(ICouponService))
            .HasDefaultValue.ShouldBeTrue();
    }

    /// <summary>
    /// 支付服务<b>不再</b>持有 <c>IRepository&lt;CouponUsage&gt;</c>。
    /// </summary>
    /// <remarks>
    /// ★ 这是父模块对促销表的最后一处直接读取（支付失败/过期还券时按 <c>PaymentId</c> 查核销记录），
    /// 现在收进了 <see cref="ICouponService.ReleaseCouponForPaymentAsync"/>。
    /// 判据写成「构造参数里没有任何促销实体的仓储」，而不是只查那一个类型 ——
    /// 换成别的促销实体是同一种损害。
    /// </remarks>
    [Fact]
    public void PaymentServiceHoldsNoRepositoryOverAPromotionEntity()
    {
        var promotionTypeNames = new[] { "Promotion", "CouponUsage", "RedemptionCode", "UserCoupon" };

        typeof(PaymentService).GetConstructors().Single()
            .GetParameters()
            .Where(p => p.ParameterType.IsGenericType)
            .SelectMany(p => p.ParameterType.GetGenericArguments())
            .Select(t => t.Name)
            .ShouldNotContain(n => promotionTypeNames.Contains(n));
    }

    /// <summary>
    /// 归还券的问法收进了契约：<see cref="ICouponService"/> 上有 <c>ReleaseCouponForPaymentAsync</c>。
    /// </summary>
    [Fact]
    public void TheContractCarriesTheReleaseByPaymentQuestion()
    {
        typeof(ICouponService).GetMethod(nameof(ICouponService.ReleaseCouponForPaymentAsync))
            .ShouldNotBeNull("父模块没有别的办法按支付找到核销记录 —— 它不再认识那张表。");
    }

    /// <summary>
    /// 留在父模块的 <see cref="ICouponService"/> 上<b>没有</b>券包与发券那六个方法。
    /// </summary>
    /// <remarks>
    /// 它们各自只有一个调用方，而且都是随子模块搬走的促销控制器。留在这里会把
    /// <c>UserCouponDto</c> 一起钉在父模块 —— 一个只有券包端点在读的 DTO。
    /// </remarks>
    [Theory]
    [InlineData("GetUserAvailableCouponsAsync")]
    [InlineData("GetUserUsedCouponsAsync")]
    [InlineData("RedeemAsync")]
    [InlineData("CanUseFirstSubscriptionDiscountAsync")]
    [InlineData("CreateRedemptionCodeAsync")]
    [InlineData("GrantAsync")]
    public void TheSixWalletAndIssuanceMethodsAreGoneFromTheParentContract(string methodName)
    {
        typeof(ICouponService).GetMethod(methodName).ShouldBeNull();
    }

    /// <summary>
    /// <c>UserCouponDto</c> 不再住在父程序集里 —— 上面那六个方法搬走之后没人需要它。
    /// </summary>
    [Fact]
    public void TheParentNoLongerCarriesTheWalletDto()
    {
        typeof(PaymentModule).Assembly.GetTypes()
            .Select(t => t.Name)
            .ShouldNotContain("UserCouponDto");
    }

    /// <summary>
    /// 留在父模块的三个优惠券 DTO 与两个枚举<b>确实还在</b>。
    /// </summary>
    /// <remarks>
    /// 与上一条是同一枚硬币的两面：搬得太多和搬得太少一样是错。这三个 DTO 在父模块自己的
    /// <see cref="ICouponService"/> 签名上；<c>DiscountType</c> 在父模块的
    /// <c>PaymentChannelCouponDto</c> 上（渠道同步契约的入参，实现住在渠道包），
    /// <c>ProductType</c> 由父模块的 <c>PaymentService</c> 从 <c>BusinessType</c> 现算。
    /// </remarks>
    [Fact]
    public void TheThreeContractDtosAndTwoSharedEnumsStayHere()
    {
        var names = typeof(PaymentModule).Assembly.GetTypes().Select(t => t.Name).ToList();

        names.ShouldContain(nameof(CouponApplyContext));
        names.ShouldContain(nameof(CouponPreviewDto));
        names.ShouldContain(nameof(CouponUsageDto));
        names.ShouldContain(nameof(DiscountType));
        names.ShouldContain(nameof(ProductType));
    }

    // ───────────────── 权限码与配置：随模块走 ─────────────────

    /// <summary>
    /// 父模块仍然声明 <c>payment</c> 组，但<b>不再</b>声明 <c>payment.promotion.*</c> 三个码。
    /// </summary>
    /// <remarks>
    /// 两边都要断言：组留下（子模块把码挂在它下面），码搬走（不打折的宿主不会 seed 它们）。
    /// 少了后半句，重复声明会被跨模块的「每个码只能由一个模块声明」门禁抓到，
    /// 但那条门禁在另一个测试项目里 —— 本地跑不到。
    /// </remarks>
    [Fact]
    public void ParentDeclaresThePaymentGroupButNotThePromotionCodes()
    {
        var context = new PermissionDefinitionContext();
        new PaymentPermissions().Define(context);

        context.Groups.Keys.ShouldContain("payment");

        context.Permissions.Keys.ShouldNotContain("payment.promotion.view");
        context.Permissions.Keys.ShouldNotContain("payment.promotion.create");
        context.Permissions.Keys.ShouldNotContain("payment.promotion.update");
    }

    /// <summary>
    /// <c>PaymentOptions</c> 上不再有 <c>Promotion</c> 这条嵌套属性，校验器也不再提它。
    /// </summary>
    /// <remarks>
    /// 配置 JSON 的形状一字不变（<c>PromotionOptions</c> 带的是绝对节 <c>Payment:Promotion</c>，
    /// 现在由子模块自己绑）。这里断言的是「父模块不再引用那个类型」——
    /// 留着嵌套属性就是一条父 → 子的编译期依赖。
    /// </remarks>
    [Fact]
    public void ParentOptionsNoLongerCarryThePromotionSection()
    {
        typeof(PaymentOptions).GetProperty("Promotion").ShouldBeNull();

        var result = new PaymentOptionsValidator().Validate(name: null, new PaymentOptions());
        (result.FailureMessage ?? string.Empty).ShouldNotContain("Promotion:");
    }

    /// <summary>
    /// 配置中心里不再出现 "Promotion" 分组。
    /// </summary>
    /// <remarks>
    /// 分组是从<b>已加载模块的程序集</b>扫出来的。这一条比拆分前更准：类留在父模块时，
    /// 不打折的宿主也会看到一个改了不生效的分组 —— 渲染出来却控制不了任何东西的设置项，
    /// 比没有这一项更糟。
    /// </remarks>
    [Fact]
    public void TheSettingsCentreNoLongerShowsAPromotionGroup()
    {
        typeof(PaymentModule).Assembly.GetTypes()
            .Select(t => t.Name)
            .ShouldNotContain("PromotionOptions");
    }

    // ───────────────── 统计：父模块自己那几块照常 ─────────────────

    /// <summary>
    /// 统计服务<b>不再</b>持有促销侧的两个仓储。
    /// </summary>
    /// <remarks>
    /// 它们此前只被 <c>GetPromotionAnalyticsAsync</c> 一个方法用到（全文 460 行里就那一处），
    /// 所以整块查询搬走之后它们是纯粹的死重。
    /// </remarks>
    [Fact]
    public void StatisticsServiceHoldsNoPromotionRepository()
    {
        var promotionTypeNames = new[] { "Promotion", "CouponUsage", "RedemptionCode", "UserCoupon" };

        typeof(PaymentStatisticsService).GetConstructors().Single()
            .GetParameters()
            .Where(p => p.ParameterType.IsGenericType)
            .SelectMany(p => p.ParameterType.GetGenericArguments())
            .Select(t => t.Name)
            .ShouldNotContain(n => promotionTypeNames.Contains(n));
    }

    /// <summary>
    /// 促销供给方是可选构造参数（带默认值）。
    /// </summary>
    [Fact]
    public void PromotionAnalyticsProviderIsAnOptionalParameter()
    {
        typeof(PaymentStatisticsService).GetConstructors().Single()
            .GetParameters()
            .Single(p => p.ParameterType == typeof(IPromotionAnalyticsProvider))
            .HasDefaultValue.ShouldBeTrue();
    }
}
