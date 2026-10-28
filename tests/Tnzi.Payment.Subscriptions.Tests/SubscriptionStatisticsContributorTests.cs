using Mapster;
using MapsterMapper;
using Microsoft.Extensions.Logging;
using MockQueryable;
using Tnzi.Mapster;

namespace Tnzi.Payment.Subscriptions.Tests;

/// <summary>
/// 订阅统计供给方：MRR / ARPU / 流失率 / 计划分布，以及总览要用的活跃订阅数。
/// </summary>
/// <remarks>
/// 这三条 MRR / 空库 / 计划分布用例拆分前住在父测试项目的 <c>StatisticsServiceTests</c>
/// （那时 <c>PaymentStatisticsService</c> 自己查订阅表）。口径、期望值与注释一字未改，
/// 搬过来是因为这一整块计算现在归本模块所有。
/// 另外两条（活跃订阅数、以及「供给方接上之后父服务确实用了它的答案」）是新的 ——
/// 后者证明的是这条缝合线接上了，而不只是各自都能算。
/// </remarks>
public class SubscriptionStatisticsContributorTests
{

    /// <summary>默认支付配置：本组用例不受其中任何一项影响。</summary>
    private static IOptionsMonitor<PaymentOptions> DefaultPaymentOptions()
    {
        var mock = new Mock<IOptionsMonitor<PaymentOptions>>();
        mock.Setup(x => x.CurrentValue).Returns(new PaymentOptions());
        return mock.Object;
    }
    private readonly Mock<IRepository<Subscription, Guid>> _subscriptionRepositoryMock = new();
    private readonly SubscriptionStatisticsContributor _contributor;

    public SubscriptionStatisticsContributorTests()
    {
        MapperExtensions.SetMapper(new Mapper(new TypeAdapterConfig()));
        _contributor = new SubscriptionStatisticsContributor(_subscriptionRepositoryMock.Object);
    }

    private void SetupSubscriptionQueryable(List<Subscription> subscriptions)
    {
        var mockQueryable = subscriptions.BuildMock();
        _subscriptionRepositoryMock.Setup(r => r.AsQueryable(false)).Returns(mockQueryable);
        _subscriptionRepositoryMock.As<IQueryable<Subscription>>()
            .Setup(q => q.Provider).Returns(mockQueryable.Provider);
        _subscriptionRepositoryMock.As<IQueryable<Subscription>>()
            .Setup(q => q.Expression).Returns(mockQueryable.Expression);
        _subscriptionRepositoryMock.As<IQueryable<Subscription>>()
            .Setup(q => q.ElementType).Returns(mockQueryable.ElementType);
        _subscriptionRepositoryMock.As<IQueryable<Subscription>>()
            .Setup(q => q.GetEnumerator()).Returns(() => mockQueryable.GetEnumerator());
    }

    private static IServiceProvider LoggingServiceProvider()
    {
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);
        return serviceProvider.Object;
    }

    private static Mock<IRepository<TEntity, Guid>> EmptyRepo<TEntity>() where TEntity : class, Tnzi.Domain.Entities.IEntity<Guid>
    {
        var repo = new Mock<IRepository<TEntity, Guid>>();
        var queryable = new List<TEntity>().BuildMock();
        repo.Setup(r => r.AsQueryable(false)).Returns(queryable);
        repo.As<IQueryable<TEntity>>().Setup(q => q.Provider).Returns(queryable.Provider);
        repo.As<IQueryable<TEntity>>().Setup(q => q.Expression).Returns(queryable.Expression);
        repo.As<IQueryable<TEntity>>().Setup(q => q.ElementType).Returns(queryable.ElementType);
        repo.As<IQueryable<TEntity>>().Setup(q => q.GetEnumerator()).Returns(() => queryable.GetEnumerator());
        return repo;
    }

    [Fact]
    public async Task GetSubscriptionMetricsAsync_WithActiveSubscriptions_CalculatesMRRAndARPU()
    {
        // Arrange
        var monthlyPlan = new SubscriptionPlan
        {
            Id = Guid.NewGuid(),
            PlanCode = "MONTHLY",
            PlanName = "Monthly Pro",
            Price = 29.99m,
            CycleType = BillingCycleType.Month,
            CycleValue = 1,
            Currency = "USD"
        };
        var yearlyPlan = new SubscriptionPlan
        {
            Id = Guid.NewGuid(),
            PlanCode = "YEARLY",
            PlanName = "Yearly Pro",
            Price = 299.88m,
            CycleType = BillingCycleType.Year,
            CycleValue = 1,
            Currency = "USD"
        };

        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var subscriptions = new List<Subscription>
        {
            // 2 个月度活跃订阅
            new() { Id = Guid.NewGuid(), Status = SubscriptionStatus.Active, UserId = Guid.NewGuid(), PlanId = monthlyPlan.Id, Plan = monthlyPlan, PaidAmount = 29.99m, Currency = "USD", ChannelCode = "Stripe", CreationTime = monthStart.AddMonths(-3) },
            new() { Id = Guid.NewGuid(), Status = SubscriptionStatus.Active, UserId = Guid.NewGuid(), PlanId = monthlyPlan.Id, Plan = monthlyPlan, PaidAmount = 29.99m, Currency = "USD", ChannelCode = "Stripe", CreationTime = monthStart.AddMonths(-1) },
            // 1 个年度活跃订阅
            new() { Id = Guid.NewGuid(), Status = SubscriptionStatus.Active, UserId = Guid.NewGuid(), PlanId = yearlyPlan.Id, Plan = yearlyPlan, PaidAmount = 299.88m, Currency = "USD", ChannelCode = "Stripe", CreationTime = monthStart.AddMonths(-6) },
            // 1 个试用订阅
            new() { Id = Guid.NewGuid(), Status = SubscriptionStatus.Trial, UserId = Guid.NewGuid(), PlanId = monthlyPlan.Id, Plan = monthlyPlan, PaidAmount = 0, Currency = "USD", ChannelCode = "Stripe", CreationTime = monthStart.AddDays(2) },
            // 1 个本月新增活跃订阅
            new() { Id = Guid.NewGuid(), Status = SubscriptionStatus.Active, UserId = Guid.NewGuid(), PlanId = monthlyPlan.Id, Plan = monthlyPlan, PaidAmount = 29.99m, Currency = "USD", ChannelCode = "Stripe", CreationTime = monthStart.AddDays(5) },
            // 1 个本月取消的订阅（上月活跃）
            new() { Id = Guid.NewGuid(), Status = SubscriptionStatus.Cancelled, UserId = Guid.NewGuid(), PlanId = monthlyPlan.Id, Plan = monthlyPlan, PaidAmount = 29.99m, Currency = "USD", ChannelCode = "Stripe", CancelTime = monthStart.AddDays(3), CreationTime = monthStart.AddMonths(-2) }
        };

        SetupSubscriptionQueryable(subscriptions);

        // Act
        var metrics = await _contributor.GetSubscriptionMetricsAsync();

        // Assert
        metrics.ShouldNotBeNull();
        metrics!.ActiveSubscriptions.ShouldBe(4); // 3 existing active + 1 new active
        metrics.TrialSubscriptions.ShouldBe(1);
        metrics.NewSubscriptionsThisMonth.ShouldBe(2); // 1 trial + 1 active created this month
        metrics.CancelledThisMonth.ShouldBe(1);

        // MRR: 4 active subscriptions (3 monthly × $29.99 + 1 yearly $299.88/12 = $24.99)
        var expectedMrr = 29.99m * 3 + 299.88m / 12;
        metrics.MonthlyRecurringRevenue.ShouldBe(expectedMrr);

        // ARPU: MRR / active count
        metrics.AverageRevenuePerUser.ShouldBe(Math.Round(expectedMrr / 4, 2));

        // Churn rate: cancelled this month / last month active = 1 / 4
        metrics.ChurnRate.ShouldBe(Math.Round(1m / 4 * 100, 2));
    }

    [Fact]
    public async Task GetSubscriptionMetricsAsync_WithNoSubscriptions_ReturnsZeroMetrics()
    {
        SetupSubscriptionQueryable([]);

        var metrics = await _contributor.GetSubscriptionMetricsAsync();

        metrics.ShouldNotBeNull();
        metrics!.ActiveSubscriptions.ShouldBe(0);
        metrics.TrialSubscriptions.ShouldBe(0);
        metrics.MonthlyRecurringRevenue.ShouldBe(0);
        metrics.AverageRevenuePerUser.ShouldBe(0);
        metrics.ChurnRate.ShouldBe(0);
        metrics.PlanDistribution.ShouldBeEmpty();
    }

    /// <summary>
    /// ★ 与父测试项目的「缺席 → 501」互补：<b>加载了</b>本包但库里一条订阅都没有时，
    /// 答案是一份全零的报表，而不是 501。两者必须能被区分开。
    /// </summary>
    [Fact]
    public async Task AnEmptyDatabaseIsAnAnswer_NotAnAbsence()
    {
        SetupSubscriptionQueryable([]);

        (await _contributor.GetSubscriptionMetricsAsync()).ShouldNotBeNull();
        (await _contributor.GetActiveSubscriptionCountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task GetSubscriptionMetricsAsync_PlanDistribution_GroupsByPlanName()
    {
        var basicPlan = new SubscriptionPlan { Id = Guid.NewGuid(), PlanCode = "BASIC", PlanName = "Basic", Price = 9.99m, CycleType = BillingCycleType.Month, CycleValue = 1, Currency = "USD" };
        var proPlan = new SubscriptionPlan { Id = Guid.NewGuid(), PlanCode = "PRO", PlanName = "Pro", Price = 29.99m, CycleType = BillingCycleType.Month, CycleValue = 1, Currency = "USD" };

        var now = DateTime.UtcNow;
        SetupSubscriptionQueryable(
        [
            new() { Id = Guid.NewGuid(), Status = SubscriptionStatus.Active, UserId = Guid.NewGuid(), PlanId = basicPlan.Id, Plan = basicPlan, PaidAmount = 9.99m, Currency = "USD", ChannelCode = "Stripe", CreationTime = now.AddMonths(-3) },
            new() { Id = Guid.NewGuid(), Status = SubscriptionStatus.Active, UserId = Guid.NewGuid(), PlanId = basicPlan.Id, Plan = basicPlan, PaidAmount = 9.99m, Currency = "USD", ChannelCode = "Stripe", CreationTime = now.AddMonths(-2) },
            new() { Id = Guid.NewGuid(), Status = SubscriptionStatus.Active, UserId = Guid.NewGuid(), PlanId = proPlan.Id, Plan = proPlan, PaidAmount = 29.99m, Currency = "USD", ChannelCode = "Stripe", CreationTime = now.AddMonths(-1) },
        ]);

        var metrics = await _contributor.GetSubscriptionMetricsAsync();

        var distribution = metrics!.PlanDistribution;
        distribution.Count.ShouldBe(2);

        var basic = distribution.First(p => p.PlanName == "Basic");
        basic.SubscriptionCount.ShouldBe(2);
        basic.Revenue.ShouldBe(19.98m); // 9.99 * 2

        var pro = distribution.First(p => p.PlanName == "Pro");
        pro.SubscriptionCount.ShouldBe(1);
        pro.Revenue.ShouldBe(29.99m);
    }

    /// <summary>
    /// 活跃订阅数的口径与拆分前一致：<c>Active</c> 或 <c>Trial</c>，不按时间窗过滤。
    /// </summary>
    [Fact]
    public async Task GetActiveSubscriptionCountAsync_CountsOnlyActive()
    {
        SetupSubscriptionQueryable(
        [
            new() { Id = Guid.NewGuid(), Status = SubscriptionStatus.Active, UserId = Guid.NewGuid(), Currency = "USD", ChannelCode = "Stripe" },
            new() { Id = Guid.NewGuid(), Status = SubscriptionStatus.Trial, UserId = Guid.NewGuid(), Currency = "USD", ChannelCode = "Stripe" },
            new() { Id = Guid.NewGuid(), Status = SubscriptionStatus.Cancelled, UserId = Guid.NewGuid(), Currency = "USD", ChannelCode = "Stripe" },
            new() { Id = Guid.NewGuid(), Status = SubscriptionStatus.PastDue, UserId = Guid.NewGuid(), Currency = "USD", ChannelCode = "Stripe" },
        ]);

        // 试用不计入「活跃」：指标端点的 ActiveSubscriptions 也是这个口径，
        // 两边算法不同的话，同一张 KPI 卡片会在两个数字之间跳
        (await _contributor.GetActiveSubscriptionCountAsync()).ShouldBe(1);
    }

    /// <summary>
    /// 总览的活跃订阅数与指标端点的 <c>ActiveSubscriptions</c> 必须是同一个数字。
    /// </summary>
    /// <remarks>
    /// 这一条才是那个缺陷的形状：两处各自算得都不算错，只是算的不是同一件事，
    /// 而它们并排显示在同一份看板上。分开断言两个数字都不会红，断言它们相等才会。
    /// </remarks>
    [Fact]
    public async Task TheOverviewCountAndTheMetricsCount_AreTheSameNumber()
    {
        SetupSubscriptionQueryable(
        [
            new() { Id = Guid.NewGuid(), Status = SubscriptionStatus.Active, UserId = Guid.NewGuid(), Currency = "USD", ChannelCode = "Stripe" },
            new() { Id = Guid.NewGuid(), Status = SubscriptionStatus.Active, UserId = Guid.NewGuid(), Currency = "USD", ChannelCode = "Stripe" },
            new() { Id = Guid.NewGuid(), Status = SubscriptionStatus.Trial, UserId = Guid.NewGuid(), Currency = "USD", ChannelCode = "Stripe" },
        ]);

        var overviewCount = await _contributor.GetActiveSubscriptionCountAsync();
        var metrics = await _contributor.GetSubscriptionMetricsAsync();

        overviewCount.ShouldBe(metrics!.ActiveSubscriptions);
        metrics.TrialSubscriptions.ShouldBe(1);
    }

    /// <summary>
    /// ★ 缝合线接上了：供给方在场时，父服务的总览<b>确实</b>用它的答案填活跃订阅数。
    /// </summary>
    /// <remarks>
    /// 与父测试项目那条「没有供给方 → <c>null</c>」合起来才完整。只测一边的话，
    /// 一个永远返回 <c>null</c> 的实现也能让两边各自绿。
    /// </remarks>
    [Fact]
    public async Task TheParentOverviewUsesTheContributedCount()
    {
        SetupSubscriptionQueryable(
        [
            new() { Id = Guid.NewGuid(), Status = SubscriptionStatus.Active, UserId = Guid.NewGuid(), Currency = "USD", ChannelCode = "Stripe" },
            new() { Id = Guid.NewGuid(), Status = SubscriptionStatus.Active, UserId = Guid.NewGuid(), Currency = "USD", ChannelCode = "Stripe" },
            new() { Id = Guid.NewGuid(), Status = SubscriptionStatus.Trial, UserId = Guid.NewGuid(), Currency = "USD", ChannelCode = "Stripe" },
        ]);

        var service = new PaymentStatisticsService(
            EmptyRepo<PaymentEntity>().Object,
            EmptyRepo<Refund>().Object,
            DefaultPaymentOptions(),
            LoggingServiceProvider(),
            _contributor);

        var overview = await service.GetStatisticsAsync(new StatisticsQueryDto());

        overview.Succeeded.ShouldBeTrue();
        overview.Data!.ActiveSubscriptions.ShouldBe(2);
    }

    /// <summary>
    /// ★ 缝合线的另一半：供给方在场时，订阅指标端点回 200 而不是 501。
    /// </summary>
    [Fact]
    public async Task TheParentMetricsEndpointSucceedsWhenTheContributorIsPresent()
    {
        SetupSubscriptionQueryable([]);

        var service = new PaymentStatisticsService(
            EmptyRepo<PaymentEntity>().Object,
            EmptyRepo<Refund>().Object,
            DefaultPaymentOptions(),
            LoggingServiceProvider(),
            _contributor);

        var result = await service.GetSubscriptionMetricsAsync();

        result.Succeeded.ShouldBeTrue();
        result.Code.ShouldNotBe(501);
    }

    /// <summary>
    /// ★ 默认的「到期取消」也是取消。此前分子只数 <c>Status == Cancelled</c>，而到期取消把订阅置为
    /// <c>PendingRenewal</c>、到点再变 <c>Expired</c>，永远进不了流失率；分母也漏掉了等续费 / 欠费 / 暂停的在册订阅。
    /// </summary>
    [Fact]
    public async Task ChurnRate_CountsPeriodEndCancellations_AgainstEverySubscriptionLiveAtMonthStart()
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        Subscription Make(SubscriptionStatus status, int createdMonthsAgo, DateTime? cancelTime = null, DateTime? endTime = null) => new()
        {
            Id = Guid.NewGuid(), Status = status, UserId = Guid.NewGuid(), Currency = "USD", ChannelCode = "Stripe",
            CreationTime = monthStart.AddMonths(-createdMonthsAgo), CancelTime = cancelTime, EndTime = endTime,
            // 付过首期：真正开始过的订阅
            LastBillingTradeNo = "PAY-" + Guid.NewGuid().ToString("N"), PaidAmount = 10m
        };

        SetupSubscriptionQueryable(
        [
            Make(SubscriptionStatus.Active, 3),
            Make(SubscriptionStatus.Active, 5),
            Make(SubscriptionStatus.PastDue, 2),
            // 本月提出「到期取消」，还在本期里
            Make(SubscriptionStatus.PendingRenewal, 2, cancelTime: monthStart.AddMinutes(1)),
            // 本月提出「到期取消」，本月已到期
            Make(SubscriptionStatus.Expired, 2, cancelTime: monthStart.AddMinutes(2), endTime: monthStart.AddMinutes(3)),
            // 本月立即取消
            Make(SubscriptionStatus.Cancelled, 1, cancelTime: monthStart.AddMinutes(4), endTime: monthStart.AddMinutes(4)),
            // 上月就提出了到期取消：月初仍在册，但这一次取消算在上个月
            Make(SubscriptionStatus.PendingRenewal, 4, cancelTime: monthStart.AddDays(-3)),
        ]);

        var metrics = await _contributor.GetSubscriptionMetricsAsync();

        metrics!.CancelledThisMonth.ShouldBe(3);
        metrics.ChurnRate.ShouldBe(Math.Round(3m / 7 * 100, 2));
    }

    /// <summary>
    /// ★ 从没开始过的订阅不是订户，它的结束不是流失。首期单没付成的 Pending 会被系统收口为 <c>Cancelled</c>
    /// （首期单失败 / 过期 / 关单，或付款人在付首期之前自己取消），带着 <c>CancelTime</c>。
    /// 按 <c>CancelTime</c> 数取消时把它们一并算进去，每一次在收银台放弃都成了一次流失，
    /// 结账放弃率直接抬高流失率与「本月取消」。判据与「是否订阅过」的探针、恢复守卫同一个：开过试用、付过订阅款。
    /// </summary>
    [Fact]
    public async Task ChurnRate_IgnoresSubscriptionsThatNeverStarted()
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        Subscription Make(SubscriptionStatus status, int createdMonthsAgo, bool started, DateTime? cancelTime = null, bool trial = false) => new()
        {
            Id = Guid.NewGuid(), Status = status, UserId = Guid.NewGuid(), Currency = "USD", ChannelCode = "Stripe",
            CreationTime = createdMonthsAgo == 0 ? now : monthStart.AddMonths(-createdMonthsAgo), CancelTime = cancelTime,
            EndTime = cancelTime,
            LastBillingTradeNo = started && !trial ? "PAY-" + Guid.NewGuid().ToString("N") : null,
            PaidAmount = started && !trial ? 10m : 0m,
            TrialStartTime = trial ? monthStart.AddMonths(-createdMonthsAgo) : null
        };

        SetupSubscriptionQueryable(
        [
            Make(SubscriptionStatus.Active, 2, started: true),
            Make(SubscriptionStatus.Active, 3, started: true),
            // 本月立即取消的真订户：一次流失
            Make(SubscriptionStatus.Cancelled, 2, started: true, cancelTime: monthStart.AddMinutes(5)),
            // 试用期内退出：同样是一次流失（试用开始过）
            Make(SubscriptionStatus.Cancelled, 1, started: false, cancelTime: monthStart.AddMinutes(6), trial: true),
            // 上月建单、首期单本月才过期（线下渠道按天）：系统收口的 Pending，不是订户
            Make(SubscriptionStatus.Cancelled, 1, started: false, cancelTime: monthStart.AddMinutes(7)),
            // 本月在收银台放弃的两次开通
            Make(SubscriptionStatus.Cancelled, 0, started: false, cancelTime: now),
            Make(SubscriptionStatus.Cancelled, 0, started: false, cancelTime: now),
        ]);

        var metrics = await _contributor.GetSubscriptionMetricsAsync();

        metrics!.CancelledThisMonth.ShouldBe(2);
        metrics.ChurnRate.ShouldBe(Math.Round(2m / 4 * 100, 2));
    }
}
