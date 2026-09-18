using Microsoft.Extensions.Logging;

namespace Tnzi.Payment.Promotions.Tests;

/// <summary>
/// 装了折扣包、<b>没装</b>续费包时，「仅限首次订阅」的券该是什么行为。
/// </summary>
/// <remarks>
/// <para>
/// ★ 这两条用例住在<b>折扣包的测试项目</b>是刻意的：提问方 <see cref="PromotionService"/>
/// 现在住在本包，而本项目<b>不引用</b> <c>Tnzi.Payment.Subscriptions</c>，
/// 所以它演的是真实的「装了折扣、没装续费」现场，而不是把某个服务设成 null 模拟出来的现场。
/// 拆分前它们住在父测试项目里，那时提问方还在父模块。
/// </para>
/// <para>
/// <b>缺席时放行不是守卫失效，而是事实</b>：没有订阅表 = 没有任何用户订阅过 = 没有人是老订户。
/// 探针只会让校验<b>多拒绝</b>，从不让它少拒绝。
/// </para>
/// <para>
/// ★ 但<b>两个包都装了、探针却漏注册</b>是另一回事 —— 订阅表就在那里，老客户可以反复领首单折扣，
/// 那是纯粹的守卫失效。那条红线由续费包自己的
/// <c>Tnzi.Payment.Subscriptions.Tests/Architecture/SubscriptionSeamRedLineTests</c> 守着，
/// 那个项目同时引用了两个包，是唯一测得到这件事的地方。
/// </para>
/// </remarks>
public class SubscriptionsPackageAbsenceTests
{
    private static IServiceProvider LoggingServiceProvider()
    {
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);
        return serviceProvider.Object;
    }

    private static IOptionsMonitor<PromotionOptions> OptionsMonitorOver(PromotionOptions options)
    {
        var monitor = new Mock<IOptionsMonitor<PromotionOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(options);
        return monitor.Object;
    }

    /// <summary>
    /// 「仅限首次订阅」的券在没有 <see cref="ISubscriptionHistoryProbe"/> 时<b>放行</b>。
    /// </summary>
    /// <remarks>
    /// 这不是「守卫失效」：没有订阅表 = 没有任何用户订阅过 = 没有人是老订户，
    /// 所以「不是老订户」这个答案是<b>事实</b>而不是放宽后的默认值。
    /// 换个说法：在一台没有订阅的宿主上，「首次订阅专属」本来就退化成了「所有人」。
    /// 同形先例是 Finance 的 <c>IMasterDataUsageProvider</c>。
    /// </remarks>
    [Fact]
    public void PromotionService_ConstructsWithoutASubscriptionHistoryProbe()
    {
        var service = new PromotionService(
            new Mock<IRepository<Promotion, Guid>>().Object,
            new Mock<IRepository<CouponUsage, Guid>>().Object,
            new Mock<IRepository<UserCoupon, Guid>>().Object,
            OptionsMonitorOver(new PromotionOptions()),
            LoggingServiceProvider());

        service.ShouldNotBeNull();
    }

    /// <summary>
    /// 探针参数是可选的，缺席时那一条校验整段跳过（而不是解析失败）。
    /// </summary>
    /// <remarks>
    /// 参数带默认值还有第二个作用：依赖审计器会跳过带默认值的构造参数，
    /// 所以本模块不需要为它加 <c>[SuppressDependencyAudit]</c>。
    /// </remarks>
    [Fact]
    public void SubscriptionHistoryProbeIsAnOptionalParameter()
    {
        var parameter = typeof(PromotionService).GetConstructors().Single()
            .GetParameters()
            .Single(p => p.ParameterType == typeof(ISubscriptionHistoryProbe));

        parameter.HasDefaultValue.ShouldBeTrue();
    }

    /// <summary>
    /// 本包<b>不</b>引用续费包 —— 折扣与续费是两件互不相干、各自可选的事。
    /// </summary>
    /// <remarks>
    /// 一次性商品也可以发券，订阅也可以从不打折。哪一边引用了另一边，
    /// 「各自可选」就变成了「装一个就得装两个」。判据是本程序集的引用清单里
    /// 没有任何名字以 <c>Tnzi.Payment.Subscriptions</c> 开头的程序集。
    /// </remarks>
    [Fact]
    public void ThisPackageDoesNotReferenceTheSubscriptionsPackage()
    {
        typeof(PaymentPromotionsModule).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ShouldNotContain("Tnzi.Payment.Subscriptions");
    }

    /// <summary>
    /// 「这个用户能不能用首单优惠」的端点在没有探针时回 <c>true</c> —— 同一个事实的另一面。
    /// </summary>
    /// <remarks>
    /// 预检与核销守卫共用 <c>IPromotionService.IsFirstSubscriptionEligibleAsync</c> 这一个判定，
    /// 探针缺席时它对所有人成立，所以缺席续费包时券包端点照常工作、不失败。
    /// 券包契约上不出现探针类型：探针是折扣服务内部向续费域提的问题，不是券包的形参。
    /// （此前预检另查「用没用过首单券」，与核销的判据分叉，两个方向都会答错；
    /// 缺席场景的行为由 <c>CouponIntegrationTests</c> 里的同名用例守着。）
    /// </remarks>
    [Fact]
    public void TheFirstSubscriptionCheckIsOnTheWalletContract_NotTheProbe()
    {
        typeof(ICouponWalletService).GetMethod(nameof(ICouponWalletService.CanUseFirstSubscriptionDiscountAsync))
            .ShouldNotBeNull();

        typeof(ICouponWalletService).GetMethods()
            .SelectMany(m => m.GetParameters())
            .Select(p => p.ParameterType)
            .ShouldNotContain(typeof(ISubscriptionHistoryProbe));
    }
}
