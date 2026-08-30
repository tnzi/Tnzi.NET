using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable;
using Tnzi.Domain.Repositories;
using Tnzi.EventBus;
using Tnzi.Modules;
using Tnzi.Payment.Dtos;
using Tnzi.Payment.Entities;
using Tnzi.Payment.Events;
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
/// 「宿主没有加载 <c>Tnzi.Payment.Subscriptions</c>」时，本模块该是什么样子。
/// </summary>
/// <remarks>
/// <para>
/// ★ 这批用例住在<b>父测试项目</b>是刻意的：本项目<b>不引用</b>那个可选包，所以它演的是
/// 真实的「没装续费包」现场，而不是把某个服务设成 null 模拟出来的现场。整个
/// <c>Integration/</c> 目录也一并是证据 —— 那套集成库里根本没有订阅的三张表，
/// 支付 / 退款 / 绑卡 / 回调的全部用例照常通过。
/// </para>
/// <para>
/// 本模块的缺席面同时具备两种形态，这也是它比前几次拆分复杂的地方：
/// </para>
/// <list type="number">
/// <item><b>整体拿走的两组路由</b>（<c>subscriptions</c> / <c>admin/subscriptions</c>，26 个端点）——
///   父模块没有任何控制器共用它们，缺席表现为路由不存在（404 来自路由表）。</item>
/// <item><b>留在父控制器上的那一个端点</b>（<c>admin/payment-statistics/subscription-metrics</c>）——
///   它的宿主控制器整条路由是父模块的，所以端点必须留下，缺席时回 <b>501</b> 并指名要加载的包。
///   <b>不是 503</b>：503 意味着暂时故障，会让监控和客户端不停重试一件永远不会恢复的事。</item>
/// </list>
/// <para>
/// 除此之外还有三处「行为要退化得对」的地方，每一处都可能退化成<b>错行为</b>而不是少能力，
/// 因此每一处都有用例钉着：绑卡 / 解绑（受影响条数必须是 0 而不是异常）、
/// 统计总览的活跃订阅数（必须是 <c>null</c> 而不是 0）。
/// <c>FirstSubscriptionOnly</c> 券那一处的提问方随后搬去了折扣包，用例也跟着搬（见文件中部注释）。
/// </para>
/// </remarks>
public class SubscriptionsPackageAbsenceTests
{
    /// <summary>
    /// 只建模、不连库的 <see cref="PaymentTestDbContext"/> —— 集成测试用的就是这个 DbContext，
    /// 它的实体清单即「没装续费包的宿主会建出哪些表」。
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

    private static IServiceProvider LoggingServiceProvider()
    {
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);
        return serviceProvider.Object;
    }

    private static Mock<IRepository<TEntity, Guid>> RepoOver<TEntity>(List<TEntity> rows) where TEntity : class, Tnzi.Domain.Entities.IEntity<Guid>
    {
        var repo = new Mock<IRepository<TEntity, Guid>>();
        var queryable = rows.BuildMock();
        repo.Setup(r => r.AsQueryable(false)).Returns(queryable);
        repo.As<IQueryable<TEntity>>().Setup(q => q.Provider).Returns(queryable.Provider);
        repo.As<IQueryable<TEntity>>().Setup(q => q.Expression).Returns(queryable.Expression);
        repo.As<IQueryable<TEntity>>().Setup(q => q.ElementType).Returns(queryable.ElementType);
        repo.As<IQueryable<TEntity>>().Setup(q => q.GetEnumerator()).Returns(() => queryable.GetEnumerator());
        return repo;
    }

    // ───────────────────────── 501，而不是 503，也不是一份全零的报表 ─────────────────────────

    /// <summary>
    /// 订阅指标端点在没有供给方时回 501，且文案指名要加载哪个包。
    /// </summary>
    /// <remarks>
    /// 这个端点<b>整个</b>只讲订阅，没有任何一半是父模块的，因此「本服务器不提供此功能」才是准确答复。
    /// 回一份全零的 <c>SubscriptionMetricsDto</c> 才是最坏的选择：MRR 0 / 流失率 0 / 活跃 0
    /// 与「生意在一夜之间归零」长得一模一样，而看板不会告诉你哪一种。
    /// </remarks>
    [Fact]
    public async Task SubscriptionMetrics_Answers501_NamingTheModuleToLoad()
    {
        var service = new PaymentStatisticsService(
            RepoOver(new List<PaymentEntity>()).Object,
            RepoOver(new List<Refund>()).Object,
            LoggingServiceProvider());

        var result = await service.GetSubscriptionMetricsAsync();

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(501);
        result.Message!.ShouldContain("Tnzi.Payment.Subscriptions");
    }

    /// <summary>
    /// 501 而不是 503 —— 这条单独钉住，因为两者只差一个数字，后果却完全不同。
    /// </summary>
    /// <remarks>
    /// 503 的语义是「服务暂时不可用」，会让监控告警、让客户端按退避策略重试、让运维去查一个
    /// 根本没坏的依赖。而「这台宿主不装续费包」是一个<b>永远不会恢复</b>的状态。
    /// </remarks>
    [Fact]
    public async Task SubscriptionMetrics_IsNot503()
    {
        var service = new PaymentStatisticsService(
            RepoOver(new List<PaymentEntity>()).Object,
            RepoOver(new List<Refund>()).Object,
            LoggingServiceProvider());

        (await service.GetSubscriptionMetricsAsync()).Code.ShouldNotBe(503);
    }

    // ───────────────────────── 「不适用」不是 0 ─────────────────────────

    /// <summary>
    /// 统计总览照常 200，但活跃订阅数是 <c>null</c>（不适用），<b>不是 0</b>。
    /// </summary>
    /// <remarks>
    /// ★ 这是本次拆分里最容易做错、后果也最隐蔽的一条。总览端点的支付与退款那一半是父模块自己的，
    /// 缺席续费包时必须照常工作，所以整个端点不能一起 501；而那样一来这个字段就得填一个值。
    /// 填 0 的话，「这台宿主不做订阅」与「所有订阅一夜之间全没了」在看板上完全无法区分，
    /// 而后者是每一个订阅制生意最需要立刻看见的灾难。
    /// </remarks>
    [Fact]
    public async Task StatisticsOverview_StillWorks_AndActiveSubscriptionsIsNullNotZero()
    {
        var now = DateTime.UtcNow;
        var payments = new List<PaymentEntity>
        {
            new() { Id = Guid.NewGuid(), Status = PaymentStatus.Succeeded, PaidAmount = 120m, ChannelCode = "Offline", CreationTime = now.AddDays(-1) }
        };

        var service = new PaymentStatisticsService(
            RepoOver(payments).Object,
            RepoOver(new List<Refund>()).Object,
            LoggingServiceProvider());

        var result = await service.GetStatisticsAsync(new StatisticsQueryDto
        {
            StartTime = now.AddDays(-30),
            EndTime = now.AddDays(1)
        });

        result.Succeeded.ShouldBeTrue();
        // 支付那一半照常算得出来 —— 这半是父模块自己的，不该被订阅的缺席牵连
        result.Data!.TotalRevenue.ShouldBe(120m);
        result.Data.SuccessfulTransactions.ShouldBe(1);
        // 订阅那一半：不适用
        result.Data.ActiveSubscriptions.ShouldBeNull();
    }

    /// <summary>
    /// DTO 上这个字段必须是可空的 —— 不可空就没有「不适用」这个答案可讲。
    /// </summary>
    [Fact]
    public void ActiveSubscriptionsIsNullable()
    {
        typeof(PaymentStatisticsDto).GetProperty(nameof(PaymentStatisticsDto.ActiveSubscriptions))!
            .PropertyType.ShouldBe(typeof(int?));
    }

    // ───────────────────────── 绑卡 / 解绑：少一项能力，不是一次异常 ─────────────────────────

    /// <summary>
    /// 没有任何 <see cref="IStoredPaymentMethodBindingSink"/> 时，广播不抛也不报错。
    /// </summary>
    /// <remarks>
    /// 这条守的是注入形态：接收方是 <c>IEnumerable&lt;T&gt;</c>（MS.DI 唯一会解析成空集合的形状）。
    /// 写成单个必需依赖的话，不装续费包的宿主<b>每一次绑卡都会炸在容器里</b> —— 那是错行为。
    /// </remarks>
    [Fact]
    public void PaymentMethodService_ConstructsWithoutAnyBindingSink()
    {
        var service = new PaymentMethodService(
            new Mock<IRepository<StoredPaymentMethod, Guid>>().Object,
            new Mock<IPaymentProviderFactory>().Object,
            OptionsMonitorOver(new PaymentOptions()),
            LoggingServiceProvider());

        service.ShouldNotBeNull();
    }

    /// <summary>
    /// 接收方参数是可选的（<c>IEnumerable</c> + 默认值），因此 DI 不需要任何注册就能构造。
    /// </summary>
    [Fact]
    public void BindingSinkIsInjectedAsAnOptionalEnumerable()
    {
        var parameter = typeof(PaymentMethodService).GetConstructors().Single()
            .GetParameters()
            .Single(p => p.ParameterType == typeof(IEnumerable<IStoredPaymentMethodBindingSink>));

        parameter.HasDefaultValue.ShouldBeTrue(
            "没有默认值的话，不装续费包的宿主每一次绑卡都会炸在容器里 —— 那是错行为，不是少能力");
    }

    // ───────────────── FirstSubscriptionOnly：判据搬走了，见下 ─────────────────
    //
    // 「仅限首次订阅」的券在没有 ISubscriptionHistoryProbe 时放行 —— 那不是守卫失效而是事实
    // （没有订阅表 = 没有人订阅过）。这两条用例随提问方（PromotionService）搬去了折扣包的测试项目
    // tests/Tnzi.Payment.Promotions.Tests/PromotionsPackageAbsenceTests.cs：
    // 本项目既不引用续费包也不引用折扣包，连 PromotionService 这个类型都不在进程里。
    // 而「两个包都装了、探针却漏注册」那条真红线由续费包自己的
    // Tnzi.Payment.Subscriptions.Tests/Architecture/SubscriptionSeamRedLineTests 守着。



    // ───────────────────────── 后台扫描：少五条，不是停摆 ─────────────────────────

    /// <summary>
    /// 没有任何 <see cref="IPaymentScheduledScan"/> 时，后台服务照常构造。
    /// </summary>
    /// <remarks>
    /// ★ 这条对应一个<b>独立于本次拆分</b>就存在的缺陷：拆分前三个服务是在一轮开始时一次性
    /// <c>GetRequiredService</c> 出来的，解析在 <c>try</c> 之外，于是任何一个解析不出来，
    /// 这一轮的<b>全部</b>扫描都不执行 —— 包括与它毫不相干的「关闭过期支付」和「对账在途退款」。
    /// 现在解析搬进了各自的 <c>try</c>，可选域的扫描则经契约贡献。
    /// </remarks>
    [Fact]
    public void BackgroundService_ConstructsWithoutAnyContributedScan()
    {
        var service = new PaymentBackgroundService(
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<PaymentBackgroundService>.Instance,
            OptionsMonitorOver(new PaymentOptions()));

        service.ShouldNotBeNull();
    }

    /// <summary>
    /// 父模块自己不贡献任何 <see cref="IPaymentScheduledScan"/>：它那两条扫描是直接调的。
    /// </summary>
    /// <remarks>
    /// 反过来说，一旦这里出现实现，就意味着有人把可选域的扫描搬回了父模块。
    /// </remarks>
    [Fact]
    public async Task ParentContributesNoScheduledScan()
    {
        (await ConfiguredParentModuleAsync()).Services
            .Where(x => x.ServiceType == typeof(IPaymentScheduledScan))
            .ShouldBeEmpty();
    }

    // ───────────────────────── 父模块不再认识订阅 ─────────────────────────

    /// <summary>
    /// 父模块的 EF 模型里没有订阅的三个实体，<c>Payment</c> 也没有指向它们的外键或影子列。
    /// </summary>
    /// <remarks>
    /// ★ 后半句是本次拆分唯一需要迁移的那件事：<c>Subscription.Payments</c> 这条<b>死导航</b>
    /// 在 EF 约定下会在**支付表**上生成影子列 <c>SubscriptionId</c>、外键
    /// <c>FK_Payment_Payment_Subscription_SubscriptionId</c> 与一条索引。
    /// 留着它，父模块那张表的形状就取决于「有没有加载续费包」。删掉它是
    /// <c>DropForeignKey</c> + <c>DropIndex</c> + <c>DropColumn</c>，无数据损失（从未被写过）。
    /// </remarks>
    [Fact]
    public void ParentModelHasNoSubscriptionEntityAndNoShadowColumn()
    {
        using var db = ModelOnlyContext();

        var entityNames = db.Model.GetEntityTypes().Select(e => e.ClrType.Name).ToList();
        entityNames.ShouldNotContain("Subscription");
        entityNames.ShouldNotContain("SubscriptionPlan");
        entityNames.ShouldNotContain("SubscriptionChange");

        var payment = db.Model.FindEntityType(typeof(Entities.Payment))!;
        payment.GetProperties().Select(p => p.Name).ShouldNotContain("SubscriptionId");
        payment.GetForeignKeys().Select(fk => fk.PrincipalEntityType.ClrType.Name).ShouldNotContain("Subscription");
        payment.GetNavigations().Select(n => n.Name).ShouldNotContain("Subscription");
    }

    /// <summary>
    /// 父模块没有任何控制器占用 <c>subscriptions</c> / <c>admin/subscriptions</c> 两条路由模板。
    /// </summary>
    /// <remarks>
    /// 这是「子模块可以把整个控制器拿走」的前提。反过来，若父模块还有控制器挂在同一个
    /// <c>[Route]</c> 上，子模块就只能把端点加在父控制器上 —— 因为 <c>[DefaultController]</c> 是
    /// <c>Inherited=false</c> 而 <c>[Route]</c> 是 <c>Inherited=true</c>，在父模板上新铸一个默认控制器
    /// 会让继承父默认控制器的消费方把子模块的端点一并继承走。
    /// </remarks>
    [Fact]
    public void ParentOwnsNeitherSubscriptionRouteTemplate()
    {
        var templates = typeof(PaymentModule).Assembly.GetTypes()
            .SelectMany(t => t.GetCustomAttributes(typeof(RouteAttribute), inherit: false))
            .Cast<RouteAttribute>()
            .Select(a => a.Template)
            .ToList();

        templates.ShouldNotContain("subscriptions");
        templates.ShouldNotContain("admin/subscriptions");
    }

    /// <summary>
    /// 反过来：<c>admin/payment-statistics</c> 这条模板<b>确实</b>还在父模块，
    /// 所以订阅指标那个端点只能留下并回 501。
    /// </summary>
    /// <remarks>
    /// 两条断言合起来才说明了本次拆分为什么<b>同时</b>有「路由不存在」和「路由存在但 501」两种缺席面。
    /// </remarks>
    [Fact]
    public void ParentStillOwnsTheStatisticsRouteTemplate()
    {
        var templates = typeof(PaymentModule).Assembly.GetTypes()
            .SelectMany(t => t.GetCustomAttributes(typeof(RouteAttribute), inherit: false))
            .Cast<RouteAttribute>()
            .Select(a => a.Template)
            .ToList();

        templates.ShouldContain("admin/payment-statistics");
    }

    /// <summary>
    /// 权限目录里没有 <c>payment.subscription.*</c>，但 <c>payment</c> 这个组还在。
    /// </summary>
    /// <remarks>
    /// 两半都要：码搬走了（不做续费的宿主不 seed 这四个码），组必须留下 ——
    /// 子模块的 provider 用 <c>parentName: "payment"</c> 往里挂码而<b>不</b>重复 <c>AddGroup</c>，
    /// 组没了它就挂空。
    /// </remarks>
    [Fact]
    public void ParentDeclaresThePaymentGroupButNotTheSubscriptionCodes()
    {
        var context = new PermissionDefinitionContext();

        new PaymentPermissions().Define(context);

        context.Groups.Keys.ShouldContain("payment");
        context.Permissions.Keys
            .Where(c => c.StartsWith("payment.subscription", StringComparison.Ordinal))
            .ShouldBeEmpty();
    }

    /// <summary>
    /// 配置中心里不再出现 Subscription 分组 —— 分组是从**已加载模块的程序集**扫出来的。
    /// </summary>
    /// <remarks>
    /// 这一条比拆分前更准：<c>SubscriptionOptions</c> 留在父模块时，不做续费的宿主也会看到一个
    /// "Subscription" 分组，改它任何一个字段都不生效。渲染出来却控制不了任何东西的设置项，
    /// 比没有这个设置项更糟。
    /// </remarks>
    [Fact]
    public void ParentAssemblyDeclaresNoSubscriptionSettingsGroup()
    {
        var groupKeys = typeof(PaymentOptions).Assembly.GetTypes()
            .Select(RuntimeSettingMetadataExtractor.Extract)
            .Where(g => g != null)
            .Select(g => g!.Key)
            .ToList();

        groupKeys.ShouldContain("payment-general");
        groupKeys.ShouldNotContain("payment-subscription");
    }

    /// <summary>
    /// <see cref="PaymentOptions"/> 不再持有 <c>Subscription</c> 嵌套属性。
    /// </summary>
    /// <remarks>
    /// 留着它就得留着 <c>SubscriptionOptions</c> 这个类型，那是父 → 子的编译期依赖。
    /// 配置 JSON 的形状不受影响：<c>Payment:Subscription</c> 这一节由子模块用绝对节路径自己绑。
    /// </remarks>
    [Fact]
    public void PaymentOptions_NoLongerCarriesTheSubscriptionSection()
    {
        typeof(PaymentOptions).GetProperty("Subscription").ShouldBeNull();
    }

    /// <summary>
    /// 订阅域的枚举不再留在父程序集里。
    /// </summary>
    /// <remarks>
    /// <c>BusinessType.Subscription</c> 与 <c>ProductType.Subscription</c> 刻意留下 ——
    /// 它们是支付域与促销域<b>自己的</b>枚举成员，只是恰好叫这个名字，在不做续费的宿主上照样有意义
    /// （一笔支付可以是别的系统发起的订阅收款）。
    /// </remarks>
    [Fact]
    public void ParentAssemblyKeepsNoSubscriptionEnums()
    {
        var enumNames = typeof(PaymentModule).Assembly.GetTypes()
            .Where(t => t.IsEnum)
            .Select(t => t.Name)
            .ToList();

        enumNames.ShouldNotContain("SubscriptionStatus");
        enumNames.ShouldNotContain("SubscriptionBillingPurpose");
        enumNames.ShouldNotContain("SubscriptionChangeType");
        enumNames.ShouldNotContain("SubscriptionChangeStatus");
        enumNames.ShouldNotContain("BillingCycleType");

        // 这两个是别人的枚举成员，不是订阅类型
        Enum.IsDefined(BusinessType.Subscription).ShouldBeTrue();
        Enum.IsDefined(ProductType.Subscription).ShouldBeTrue();
    }

    /// <summary>
    /// 父模块在三个支付事件上注册的处理器<b>恰好</b>是它自己那三个记日志的。
    /// </summary>
    /// <remarks>
    /// 订阅计费的三个回流处理器现在由子模块注册。它们若悄悄回到父模块，
    /// 不装续费包的宿主会在每笔支付完成 / 失败 / 过期时去解析一个不存在的 <c>ISubscriptionService</c>。
    /// </remarks>
    [Theory]
    [InlineData(typeof(PaymentCompletedEvent), typeof(Events.Handlers.PaymentCompletedEventHandler))]
    [InlineData(typeof(PaymentFailedEvent), typeof(Events.Handlers.PaymentFailedEventHandler))]
    [InlineData(typeof(PaymentExpiredEvent), typeof(Events.Handlers.PaymentExpiredEventHandler))]
    public async Task ParentRegistersOnlyItsOwnHandlerOnEachPaymentEvent(Type eventType, Type expectedHandler)
    {
        var handlerServiceType = typeof(IEventHandler<>).MakeGenericType(eventType);

        var handlers = (await ConfiguredParentModuleAsync()).Services
            .Where(x => x.ServiceType == handlerServiceType)
            .Select(x => x.ImplementationType)
            .ToList();

        handlers.ShouldBe([expectedHandler]);
    }

    /// <summary>
    /// 父模块自己不注册 <c>ISubscriptionService</c>。
    /// </summary>
    /// <remarks>
    /// 该契约整个随续费域走了 —— 留一个 NoOp 在父模块是错的：调用方拿到的会是一个
    /// 「成功但什么都没做」的订阅服务，而那正是最难发现的一种失败。
    /// </remarks>
    [Fact]
    public async Task ParentRegistersNoSubscriptionService()
    {
        var serviceTypeNames = (await ConfiguredParentModuleAsync()).Services
            .Select(d => d.ServiceType.Name)
            .ToList();

        serviceTypeNames.ShouldNotContain("ISubscriptionService");
    }

    private static IOptionsMonitor<T> OptionsMonitorOver<T>(T value) where T : class
    {
        var monitor = new Mock<IOptionsMonitor<T>>();
        monitor.Setup(m => m.CurrentValue).Returns(value);
        return monitor.Object;
    }
}
