using Microsoft.Extensions.Configuration;
using System.Reflection;
using Tnzi.Payment.Promotions;

namespace Tnzi.Payment.Subscriptions.Tests.Architecture;

/// <summary>
/// 续费域缝合线的红线门禁。
/// </summary>
/// <remarks>
/// <para>
/// 这组测试守的不是风格，而是这条缝合线上<b>唯一两处会把「少一项能力」变成「错行为」</b>的地方。
/// 拆分把父模块对订阅表的四处直接读写换成了四个扩展点，其中两个的缺席后果是良性的：
/// 没有统计供给方 → 看板显示「不适用」；没有后台扫描 → 少五条扫描。
/// 另外两个不是：
/// </para>
/// <list type="number">
/// <item><b><see cref="IStoredPaymentMethodBindingSink"/> 没注册</b> —— 用户绑了卡，订阅上却拿不到；
///   用户解绑了卡，订阅上的快照却还在，后台于是拿着一个已作废的凭据反复扣款失败。
///   两边都<b>不会报错</b>：绑卡返回成功、解绑返回成功、日志干干净净。</item>
/// <item><b><see cref="ISubscriptionHistoryProbe"/> 没注册</b> —— 一张「仅限首次订阅」的券
///   在一台<b>有订阅</b>的宿主上变成无限制，老客户可以反复领首单折扣。
///   注意这与「没装续费包」不是一回事：那种情况下没有人订阅过，放行是事实；
///   而本包已经加载、订阅表就在那里、探针却没注册，放行就是纯粹的守卫失效。
///   ★ 折扣域随后也拆成了可选包 <c>Tnzi.Payment.Promotions</c>，提问方
///   <c>PromotionService</c> 因此从父模块搬到了那个包。<b>本测试项目是唯一同时引用
///   两个可选包的地方</b>，也就是唯一测得到「两个包都装上了、探针却漏注册」的地方 ——
///   两个包各自的测试项目都只看得见自己那一半。</item>
/// </list>
/// <para>
/// 两处的共同形态是：<b>注册漏了不会有任何症状</b>，编译过、启动过、全部业务测试绿。
/// 所以红线必须是一条会红的测试。★ 本组测试是<b>先写红</b>再写实现的：
/// 最初 <c>PaymentSubscriptionsModule.ConfigureServicesAsync</c> 里只有订阅服务，
/// 这两条断言各红一次，然后才补上两行注册。
/// </para>
/// </remarks>
public class SubscriptionSeamRedLineTests
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

    /// <summary>
    /// 红线 1：本模块<b>必须同时</b>注册绑卡接收方与订阅历史探针。
    /// </summary>
    /// <remarks>
    /// 合成一条断言而不是拆成两条是刻意的：它们是同一条红线的两半 ——
    /// 「加载了续费包之后，父模块问出去的每一个会影响正确性的问题都必须有人回答」。
    /// 失败文案直接写出漏掉哪个会导致什么，而不是只说「缺少注册」。
    /// </remarks>
    [Fact]
    public void TheModuleRegistersBothCorrectnessCriticalExtensionPoints()
    {
        var registered = ConfiguredModule()
            .Select(d => d.ServiceType)
            .ToHashSet();

        registered.ShouldContain(typeof(IStoredPaymentMethodBindingSink),
            "少了它，绑卡不再同步到订阅、解绑不再清理订阅快照，而两条路径都照常返回成功 —— "
            + "后台会拿着一张已作废的卡反复扣款失败，日志里一句异常都没有。");

        registered.ShouldContain(typeof(ISubscriptionHistoryProbe),
            "少了它，FirstSubscriptionOnly 的券在一台有订阅的宿主上变成无限制 —— "
            + "老客户可以反复领首单折扣，而校验代码本身看起来完全正常。");
    }

    /// <summary>
    /// 红线 1 的另一半：这两个扩展点的实现必须真的住在本程序集里。
    /// </summary>
    /// <remarks>
    /// 只断言「注册了」会被一个空实现骗过去。这里再钉一层：实现类型必须来自本程序集，
    /// 且必须是读写订阅表的那两个具体类 —— 换句话说，注册的是真货。
    /// </remarks>
    [Fact]
    public void BothExtensionPointsAreImplementedInThisAssembly()
    {
        var services = ConfiguredModule();

        services.Single(d => d.ServiceType == typeof(IStoredPaymentMethodBindingSink))
            .ImplementationType.ShouldBe(typeof(SubscriptionBindingSink));

        services.Single(d => d.ServiceType == typeof(ISubscriptionHistoryProbe))
            .ImplementationType.ShouldBe(typeof(SubscriptionHistoryProbe));
    }

    /// <summary>
    /// 红线 2：提问的那一侧必须仍然经这三个契约提问，而不是绕回去自己查。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 上面两条守的是「有人回答」，这条守的是「还在问」。两处必须同时成立才算这条缝合线还在：
    /// 谁把 <c>IRepository&lt;Subscription&gt;</c> 拖回提问方，那一侧就再也不可能在不加载本包时构建
    /// —— 那是编译期就会炸的失败，但只有<b>先删掉本包的项目引用</b>才看得见，
    /// 而没有人会在日常改动里这么做。这条测试用反射把它变成一条随时会红的断言。
    /// </para>
    /// <para>
    /// ★ 三行里前两行的提问方在<b>父模块</b>，第三行 <c>PromotionService</c> 的提问方在
    /// <b>另一个可选包</b> <c>Tnzi.Payment.Promotions</c>（折扣域后来也拆出去了）。
    /// 这不改变红线的性质：<c>ISubscriptionHistoryProbe</c> 的契约仍然住在父模块，
    /// 依赖方向仍然是「两个子包各自 → 父模块」，两个子包之间一条引用都没有。
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(typeof(PaymentMethodService), typeof(IEnumerable<IStoredPaymentMethodBindingSink>))]
    [InlineData(typeof(PromotionService), typeof(ISubscriptionHistoryProbe))]
    [InlineData(typeof(PaymentStatisticsService), typeof(IPaymentStatisticsContributor))]
    public void TheParentStillAsksThroughTheContract(Type parentService, Type contract)
    {
        var parameters = parentService.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Single()
            .GetParameters();

        parameters.Select(p => p.ParameterType).ShouldContain(contract,
            $"{parentService.Name} 不再经 {contract.Name} 提问了 —— 它要么绕回去直接查订阅表"
            + "（父 → 子依赖，提问方从此离不开本包），要么这块能力被悄悄删掉了。");

        parameters.Single(p => p.ParameterType == contract).HasDefaultValue.ShouldBeTrue(
            $"{contract.Name} 必须是可选注入（带默认值），否则不加载本包的宿主会炸在容器里。");
    }

    // ────────── 两个可选包都装上时：唯一测得到的地方 ──────────

    /// <summary>
    /// ★ 红线 2 的续集：<b>续费包与折扣包同时加载</b>时，探针必须被真的解析出来。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 上面那条只看构造签名，这条把两个模块<b>都</b>装进同一个容器，然后确认
    /// <c>PromotionService</c> 拿到的 <see cref="ISubscriptionHistoryProbe"/> 不是 null。
    /// 两者不可互相替代：签名对、注册也对，但如果哪一天两个模块被配置成注册进
    /// 互不相干的容器（或者某一方把注册挪进了一个条件分支），签名断言照绿而探针照样是 null。
    /// </para>
    /// <para>
    /// <b>漏了会怎样</b>：订阅表就在那里、老订户就在库里，而一张「仅限首次订阅」的券
    /// 对所有人成立。没有异常、没有日志、校验代码本身看起来完全正常 ——
    /// 唯一的症状是营收上少掉的那一块。
    /// </para>
    /// <para>
    /// 这条测试只能写在这里：折扣包的测试项目看不见续费包，续费包的 src 也不引用折扣包，
    /// <b>只有本测试项目同时引用了两个</b>。
    /// </para>
    /// </remarks>
    [Fact]
    public async Task WithBothOptionalPackagesLoaded_ThePromotionServiceReallyGetsTheProbe()
    {
        var services = new ServiceCollection();
        var context = new ServiceConfigurationContext(services, new ConfigurationBuilder().Build());

        foreach (var module in new ITnziModule[] { new PaymentSubscriptionsModule(), new PaymentPromotionsModule() })
        {
            await module.PreConfigureServicesAsync(context);
            await module.ConfigureServicesAsync(context);
        }

        // 两个模块只声明服务，仓储由宿主的 EFCore 模块提供；这里补上它们需要的那几个。
        services.AddScoped(_ => new Mock<IRepository<Promotion, Guid>>().Object);
        services.AddScoped(_ => new Mock<IRepository<CouponUsage, Guid>>().Object);
        services.AddScoped(_ => new Mock<IRepository<UserCoupon, Guid>>().Object);
        services.AddScoped(_ => new Mock<IRepository<RedemptionCode, Guid>>().Object);
        services.AddScoped(_ => new Mock<IRepository<Subscription, Guid>>().Object);
        services.AddLogging();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var probe = scope.ServiceProvider.GetService<ISubscriptionHistoryProbe>();

        probe.ShouldNotBeNull(
            "两个可选包都装上了，FirstSubscriptionOnly 券的判据却没人回答 —— "
            + "那张券于是对所有人成立，老客户可以反复领首单折扣，而全程没有任何异常或日志。");
        probe.ShouldBeOfType<SubscriptionHistoryProbe>();
    }

    /// <summary>
    /// 两个可选包<b>互不引用</b>：折扣与续费是两件各自可选、互不相干的事。
    /// </summary>
    /// <remarks>
    /// 一次性商品也可以发券，订阅也可以从不打折。哪一边引用了另一边，
    /// 「各自可选」就变成了「装一个就得装两个」，而两者共同需要的那个契约
    /// （<see cref="ISubscriptionHistoryProbe"/>）本来就住在父模块。
    /// </remarks>
    [Fact]
    public void TheTwoOptionalPackagesDoNotReferenceEachOther()
    {
        var subscriptions = typeof(PaymentSubscriptionsModule).Assembly;
        var promotions = typeof(PaymentPromotionsModule).Assembly;

        subscriptions.GetReferencedAssemblies().Select(a => a.Name)
            .ShouldNotContain(promotions.GetName().Name);
        promotions.GetReferencedAssemblies().Select(a => a.Name)
            .ShouldNotContain(subscriptions.GetName().Name);
    }

    /// <summary>
    /// 红线 3：本模块不得反过来被父模块引用。
    /// </summary>
    /// <remarks>
    /// 判据是父程序集的引用清单里不能出现本程序集。这条一旦红，说明「可选」这两个字已经名存实亡。
    /// </remarks>
    [Fact]
    public void TheParentAssemblyDoesNotReferenceThisOne()
    {
        var parentReferences = typeof(PaymentModule).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToList();

        parentReferences.ShouldNotContain(typeof(PaymentSubscriptionsModule).Assembly.GetName().Name);
    }
}
