using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace Tnzi.Payment.Promotions.Tests.Architecture;

/// <summary>
/// 折扣域缝合线的红线门禁。
/// </summary>
/// <remarks>
/// <para>
/// 这组测试守的不是风格，而是这条缝合线上<b>会把「少一项能力」变成「错行为」或者
/// 「父模块从此离不开本包」</b>的几处。拆分把父模块对促销表的两处直接读取换成了两个扩展点：
/// </para>
/// <list type="number">
/// <item><b><see cref="ICouponService.ReleaseCouponForPaymentAsync"/> 没实现</b> ——
///   支付失败或超时未付之后，券不还给用户。用户没付成钱还白丢一张券，
///   而两条路径都<b>不会报错</b>：支付照常置为 Failed / Expired，日志干干净净。</item>
/// <item><b><see cref="IPromotionAnalyticsProvider"/> 没注册</b> ——
///   促销效果分析端点永远回 501，即使本包已经加载。这不是缺席，是能力丢失。</item>
/// </list>
/// <para>
/// 两处的共同形态是：<b>漏了不会有任何症状</b>，编译过、启动过、全部业务测试绿。
/// 所以红线必须是一条会红的测试。
/// </para>
/// </remarks>
public class PromotionSeamRedLineTests
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

    /// <summary>
    /// 红线 1：本模块<b>必须</b>把父模块问出去的两个问题都接上。
    /// </summary>
    /// <remarks>
    /// 合成一条断言而不是拆成两条是刻意的：它们是同一条红线的两半 ——
    /// 「加载了折扣包之后，父模块问出去的每一个问题都必须有人回答」。
    /// 失败文案直接写出漏掉哪个会导致什么，而不是只说「缺少注册」。
    /// </remarks>
    [Fact]
    public void TheModuleAnswersBothQuestionsTheParentAsks()
    {
        var registered = ConfiguredModule().Select(d => d.ServiceType).ToHashSet();

        registered.ShouldContain(typeof(ICouponService),
            "少了它，父模块的 ICouponService 依旧是 null：带券码的建单会被拒（400），"
            + "而本包明明已经加载、促销表就在那里 —— 那不是「缺席 = 少能力」，是装了却不生效。");

        registered.ShouldContain(typeof(IPromotionAnalyticsProvider),
            "少了它，促销效果分析端点永远回 501，即使本包已经加载 —— 看板上一片空白，"
            + "而运营看不出是「没装包」还是「没人用券」。");
    }

    /// <summary>
    /// 红线 1 的另一半：这两个扩展点的实现必须真的住在本程序集里。
    /// </summary>
    /// <remarks>
    /// 只断言「注册了」会被一个空实现骗过去。这里再钉一层：实现类型必须来自本程序集，
    /// 且必须是读写促销表的那两个具体类 —— 换句话说，注册的是真货。
    /// </remarks>
    [Fact]
    public void BothExtensionPointsAreImplementedInThisAssembly()
    {
        var services = ConfiguredModule();

        // ICouponService 走的是工厂委托（三个接口共用一个 Scoped 实例），所以断言到那个实例类型上
        services.Single(d => d.ServiceType == typeof(CouponService))
            .ImplementationType.ShouldBe(typeof(CouponService));

        services.Single(d => d.ServiceType == typeof(IPromotionAnalyticsProvider))
            .ImplementationType.ShouldBe(typeof(PromotionAnalyticsProvider));

        typeof(CouponService).Assembly.ShouldBe(typeof(PaymentPromotionsModule).Assembly);
        typeof(PromotionAnalyticsProvider).Assembly.ShouldBe(typeof(PaymentPromotionsModule).Assembly);
    }

    /// <summary>
    /// 红线 2：父模块<b>那一侧</b>必须仍然经这两个契约提问，而不是绕回去自己查。
    /// </summary>
    /// <remarks>
    /// 上面两条守的是「有人回答」，这条守的是「还在问」。两处必须同时成立才算这条缝合线还在：
    /// 谁把 <c>IRepository&lt;CouponUsage&gt;</c> 拖回父模块，父模块就再也不可能在不加载本包时构建
    /// —— 那是编译期就会炸的失败，但只有<b>先删掉本包的项目引用</b>才看得见，
    /// 而没有人会在日常改动里这么做。这条测试用反射把它变成一条随时会红的断言。
    /// </remarks>
    [Theory]
    [InlineData(typeof(PaymentService), typeof(ICouponService))]
    [InlineData(typeof(PaymentStatisticsService), typeof(IPromotionAnalyticsProvider))]
    public void TheParentStillAsksThroughTheContract(Type parentService, Type contract)
    {
        var parameters = parentService.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Single()
            .GetParameters();

        parameters.Select(p => p.ParameterType).ShouldContain(contract,
            $"{parentService.Name} 不再经 {contract.Name} 提问了 —— 它要么绕回去直接查促销表"
            + "（父 → 子依赖，父模块从此离不开本包），要么这块能力被悄悄删掉了。");

        parameters.Single(p => p.ParameterType == contract).HasDefaultValue.ShouldBeTrue(
            $"{contract.Name} 必须是可选注入（带默认值），否则不加载本包的宿主会炸在容器里。");
    }

    /// <summary>
    /// 红线 3：父模块不得再持有促销侧的任何仓储。
    /// </summary>
    /// <remarks>
    /// 上一条查的是「有没有在问」，这一条查的是「有没有偷偷不问」。一个新增的
    /// <c>IRepository&lt;CouponUsage&gt;</c> 参数不会让上一条变红（那个契约参数还在），
    /// 但父模块从此在不加载本包时构建不出模型。
    /// </remarks>
    [Theory]
    [InlineData(typeof(PaymentService))]
    [InlineData(typeof(PaymentStatisticsService))]
    [InlineData(typeof(PaymentMethodService))]
    [InlineData(typeof(RefundService))]
    public void TheParentHoldsNoPromotionRepository(Type parentService)
    {
        var promotionEntities = new[] { typeof(Promotion), typeof(CouponUsage), typeof(RedemptionCode), typeof(UserCoupon) };

        var offending = parentService.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Single()
            .GetParameters()
            .Where(p => p.ParameterType.IsGenericType
                && p.ParameterType.GetGenericArguments().Any(promotionEntities.Contains))
            .Select(p => p.Name)
            .ToList();

        offending.ShouldBeEmpty(
            $"{parentService.Name} 又持有促销实体的仓储了 —— 父模块从此在不加载本包时建不出模型。");
    }

    /// <summary>
    /// 红线 4：本模块不得反过来被父模块引用。
    /// </summary>
    /// <remarks>
    /// 判据是父程序集的引用清单里不能出现本程序集。这条一旦红，说明「可选」这两个字已经名存实亡。
    /// </remarks>
    [Fact]
    public void TheParentAssemblyDoesNotReferenceThisOne()
    {
        typeof(PaymentModule).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ShouldNotContain(typeof(PaymentPromotionsModule).Assembly.GetName().Name);
    }
}
