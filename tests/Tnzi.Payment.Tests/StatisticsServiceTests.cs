using System.Linq.Expressions;
using Mapster;
using MapsterMapper;
using Microsoft.Extensions.Logging;
using MockQueryable;
using MockQueryable.Moq;
using Moq;
using Tnzi.Domain.Repositories;
using Tnzi.Mapster;
using Tnzi.Payment.Dtos;
using Tnzi.Payment.Entities;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Services;
using PaymentEntity = Tnzi.Payment.Entities.Payment;

namespace Tnzi.Payment.Tests;

/// <summary>
/// PaymentStatisticsService 单元测试
/// </summary>
public class StatisticsServiceTests
{
    private readonly Mock<IRepository<PaymentEntity, Guid>> _paymentRepositoryMock;
    private readonly Mock<IRepository<Refund, Guid>> _refundRepositoryMock;
    private readonly Mock<IRepository<Subscription, Guid>> _subscriptionRepositoryMock;
    private readonly Mock<IRepository<SubscriptionPlan, Guid>> _planRepositoryMock;
    private readonly Mock<IRepository<CouponUsage, Guid>> _couponUsageRepositoryMock;
    private readonly Mock<IRepository<Promotion, Guid>> _promotionRepositoryMock;
    private readonly PaymentStatisticsService _service;

    public StatisticsServiceTests()
    {
        // 初始化 Mapster
        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        _paymentRepositoryMock = new Mock<IRepository<PaymentEntity, Guid>>();
        _refundRepositoryMock = new Mock<IRepository<Refund, Guid>>();
        _subscriptionRepositoryMock = new Mock<IRepository<Subscription, Guid>>();
        _planRepositoryMock = new Mock<IRepository<SubscriptionPlan, Guid>>();
        _couponUsageRepositoryMock = new Mock<IRepository<CouponUsage, Guid>>();
        _promotionRepositoryMock = new Mock<IRepository<Promotion, Guid>>();

        // 设置 IServiceProvider mock
        var serviceProviderMock = new Mock<IServiceProvider>();
        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactoryMock.Object);

        _service = new PaymentStatisticsService(
            _paymentRepositoryMock.Object,
            _refundRepositoryMock.Object,
            _subscriptionRepositoryMock.Object,
            _planRepositoryMock.Object,
            _couponUsageRepositoryMock.Object,
            _promotionRepositoryMock.Object,
            serviceProviderMock.Object
        );
    }

    /// <summary>
    /// 设置支付仓储的 IQueryable mock
    /// </summary>
    private void SetupPaymentQueryable(List<PaymentEntity> payments)
    {
        var mockQueryable = payments.BuildMock();
        _paymentRepositoryMock.Setup(r => r.AsQueryable(false)).Returns(mockQueryable);
        _paymentRepositoryMock.As<IQueryable<PaymentEntity>>()
            .Setup(q => q.Provider).Returns(mockQueryable.Provider);
        _paymentRepositoryMock.As<IQueryable<PaymentEntity>>()
            .Setup(q => q.Expression).Returns(mockQueryable.Expression);
        _paymentRepositoryMock.As<IQueryable<PaymentEntity>>()
            .Setup(q => q.ElementType).Returns(mockQueryable.ElementType);
        _paymentRepositoryMock.As<IQueryable<PaymentEntity>>()
            .Setup(q => q.GetEnumerator()).Returns(() => mockQueryable.GetEnumerator());
    }

    /// <summary>
    /// 设置退款仓储的 IQueryable mock
    /// </summary>
    private void SetupRefundQueryable(List<Refund> refunds)
    {
        var mockQueryable = refunds.BuildMock();
        _refundRepositoryMock.Setup(r => r.AsQueryable(false)).Returns(mockQueryable);
        _refundRepositoryMock.As<IQueryable<Refund>>()
            .Setup(q => q.Provider).Returns(mockQueryable.Provider);
        _refundRepositoryMock.As<IQueryable<Refund>>()
            .Setup(q => q.Expression).Returns(mockQueryable.Expression);
        _refundRepositoryMock.As<IQueryable<Refund>>()
            .Setup(q => q.ElementType).Returns(mockQueryable.ElementType);
        _refundRepositoryMock.As<IQueryable<Refund>>()
            .Setup(q => q.GetEnumerator()).Returns(() => mockQueryable.GetEnumerator());
    }

    /// <summary>
    /// 设置订阅仓储的 IQueryable mock
    /// </summary>
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

    #region GetStatisticsAsync Tests

    [Fact]
    public async Task GetStatisticsAsync_WithNoData_ReturnsZeroStatistics()
    {
        // Arrange
        SetupPaymentQueryable(new List<PaymentEntity>());
        SetupRefundQueryable(new List<Refund>());
        SetupSubscriptionQueryable(new List<Subscription>());

        var query = new StatisticsQueryDto
        {
            StartTime = DateTime.UtcNow.AddDays(-30),
            EndTime = DateTime.UtcNow
        };

        // Act
        var result = await _service.GetStatisticsAsync(query);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.TotalRevenue.ShouldBe(0);
        result.Data.TotalTransactions.ShouldBe(0);
        result.Data.SuccessfulTransactions.ShouldBe(0);
        result.Data.FailedTransactions.ShouldBe(0);
        result.Data.TotalRefunds.ShouldBe(0);
        result.Data.RefundCount.ShouldBe(0);
        result.Data.RefundRate.ShouldBe(0);
        result.Data.ActiveSubscriptions.ShouldBe(0);
        result.Data.ChannelDistribution.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetStatisticsAsync_WithMixedPayments_ReturnsCorrectAggregations()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var payments = new List<PaymentEntity>
        {
            new() { Id = Guid.NewGuid(), Status = PaymentStatus.Succeeded, PaidAmount = 100m, ChannelCode = "Stripe", CreationTime = now.AddDays(-5) },
            new() { Id = Guid.NewGuid(), Status = PaymentStatus.Succeeded, PaidAmount = 200m, ChannelCode = "Stripe", CreationTime = now.AddDays(-3) },
            new() { Id = Guid.NewGuid(), Status = PaymentStatus.Succeeded, PaidAmount = 150m, ChannelCode = "PayPal", CreationTime = now.AddDays(-2) },
            new() { Id = Guid.NewGuid(), Status = PaymentStatus.Failed, PaidAmount = 0, ChannelCode = "Stripe", CreationTime = now.AddDays(-1) },
            new() { Id = Guid.NewGuid(), Status = PaymentStatus.Pending, PaidAmount = 0, ChannelCode = "Stripe", CreationTime = now }
        };

        var refunds = new List<Refund>
        {
            new() { Id = Guid.NewGuid(), RefundAmount = 50m, Status = RefundStatus.Succeeded, Reason = "Test", BusinessOrderNo = "O1", CreationTime = now.AddDays(-2) }
        };

        SetupPaymentQueryable(payments);
        SetupRefundQueryable(refunds);
        SetupSubscriptionQueryable(new List<Subscription>
        {
            new() { Id = Guid.NewGuid(), Status = SubscriptionStatus.Active, UserId = Guid.NewGuid(), Currency = "USD", ChannelCode = "Stripe" },
            new() { Id = Guid.NewGuid(), Status = SubscriptionStatus.Trial, UserId = Guid.NewGuid(), Currency = "USD", ChannelCode = "Stripe" },
            new() { Id = Guid.NewGuid(), Status = SubscriptionStatus.Cancelled, UserId = Guid.NewGuid(), Currency = "USD", ChannelCode = "Stripe" }
        });

        var query = new StatisticsQueryDto
        {
            StartTime = now.AddDays(-30),
            EndTime = now.AddDays(1)
        };

        // Act
        var result = await _service.GetStatisticsAsync(query);

        // Assert
        result.Succeeded.ShouldBeTrue();
        var stats = result.Data!;
        stats.TotalTransactions.ShouldBe(5);
        stats.SuccessfulTransactions.ShouldBe(3);
        stats.FailedTransactions.ShouldBe(1);
        stats.TotalRevenue.ShouldBe(450m); // 100+200+150
        stats.TotalRefunds.ShouldBe(50m);
        stats.RefundCount.ShouldBe(1);
        stats.ActiveSubscriptions.ShouldBe(2); // Active + Trial
        stats.ChannelDistribution.Count.ShouldBe(2); // Stripe + PayPal
    }

    [Fact]
    public async Task GetStatisticsAsync_WithDefaultTimeRange_Uses30DayWindow()
    {
        // Arrange
        SetupPaymentQueryable(new List<PaymentEntity>());
        SetupRefundQueryable(new List<Refund>());
        SetupSubscriptionQueryable(new List<Subscription>());

        var query = new StatisticsQueryDto(); // 不设置时间，使用默认

        // Act
        var result = await _service.GetStatisticsAsync(query);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        // 验证默认使用近30天
        var expectedStart = DateTime.UtcNow.AddDays(-30);
        result.Data.StartTime.ShouldBeInRange(expectedStart.AddMinutes(-1), expectedStart.AddMinutes(1));
    }

    [Fact]
    public async Task GetStatisticsAsync_ChannelDistribution_CalculatesPercentageCorrectly()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var payments = new List<PaymentEntity>
        {
            new() { Id = Guid.NewGuid(), Status = PaymentStatus.Succeeded, PaidAmount = 300m, ChannelCode = "Stripe", CreationTime = now.AddDays(-1) },
            new() { Id = Guid.NewGuid(), Status = PaymentStatus.Succeeded, PaidAmount = 100m, ChannelCode = "PayPal", CreationTime = now.AddDays(-1) }
        };

        SetupPaymentQueryable(payments);
        SetupRefundQueryable(new List<Refund>());
        SetupSubscriptionQueryable(new List<Subscription>());

        var query = new StatisticsQueryDto
        {
            StartTime = now.AddDays(-7),
            EndTime = now
        };

        // Act
        var result = await _service.GetStatisticsAsync(query);

        // Assert
        result.Succeeded.ShouldBeTrue();
        var channels = result.Data!.ChannelDistribution;
        channels.Count.ShouldBe(2);

        var stripe = channels.First(c => c.ChannelCode == "Stripe");
        stripe.Revenue.ShouldBe(300m);
        stripe.TransactionCount.ShouldBe(1);
        stripe.Percentage.ShouldBe(75m); // 300/400 * 100

        var paypal = channels.First(c => c.ChannelCode == "PayPal");
        paypal.Revenue.ShouldBe(100m);
        paypal.Percentage.ShouldBe(25m); // 100/400 * 100
    }

    #endregion

    #region GetRevenueTrendAsync Tests

    [Fact]
    public async Task GetRevenueTrendAsync_DailyGranularity_ReturnsPointsGroupedByDay()
    {
        // Arrange
        var baseDate = new DateTime(2026, 2, 1, 10, 0, 0, DateTimeKind.Utc);
        var payments = new List<PaymentEntity>
        {
            new() { Id = Guid.NewGuid(), Status = PaymentStatus.Succeeded, PaidAmount = 100m, ChannelCode = "Stripe", CreationTime = baseDate },
            new() { Id = Guid.NewGuid(), Status = PaymentStatus.Succeeded, PaidAmount = 200m, ChannelCode = "Stripe", CreationTime = baseDate.AddHours(3) },
            new() { Id = Guid.NewGuid(), Status = PaymentStatus.Succeeded, PaidAmount = 150m, ChannelCode = "PayPal", CreationTime = baseDate.AddDays(1) },
            // Failed 不计入趋势
            new() { Id = Guid.NewGuid(), Status = PaymentStatus.Failed, PaidAmount = 0, ChannelCode = "Stripe", CreationTime = baseDate.AddDays(1) }
        };

        var refunds = new List<Refund>
        {
            new() { Id = Guid.NewGuid(), RefundAmount = 30m, Status = RefundStatus.Succeeded, Reason = "Test", BusinessOrderNo = "O1", CreationTime = baseDate }
        };

        SetupPaymentQueryable(payments);
        SetupRefundQueryable(refunds);

        var query = new RevenueTrendQueryDto
        {
            StartTime = baseDate.AddDays(-1),
            EndTime = baseDate.AddDays(2),
            Granularity = TrendGranularity.Day
        };

        // Act
        var result = await _service.GetRevenueTrendAsync(query);

        // Assert
        result.Succeeded.ShouldBeTrue();
        var points = result.Data!;
        points.Count.ShouldBe(2); // 2 天有数据

        var day1 = points.First(p => p.Date == baseDate.Date);
        day1.Revenue.ShouldBe(300m); // 100 + 200
        day1.TransactionCount.ShouldBe(2);
        day1.RefundAmount.ShouldBe(30m);
        day1.NetRevenue.ShouldBe(270m); // 300 - 30

        var day2 = points.First(p => p.Date == baseDate.AddDays(1).Date);
        day2.Revenue.ShouldBe(150m);
        day2.TransactionCount.ShouldBe(1);
        day2.RefundAmount.ShouldBe(0);
        day2.NetRevenue.ShouldBe(150m);
    }

    [Fact]
    public async Task GetRevenueTrendAsync_WeeklyGranularity_AggregatesByWeek()
    {
        // Arrange — 2026-02-02 (周一) ~ 2026-02-15 (周日)
        var monday = new DateTime(2026, 2, 2, 10, 0, 0, DateTimeKind.Utc); // Monday
        var payments = new List<PaymentEntity>
        {
            new() { Id = Guid.NewGuid(), Status = PaymentStatus.Succeeded, PaidAmount = 100m, ChannelCode = "Stripe", CreationTime = monday },
            new() { Id = Guid.NewGuid(), Status = PaymentStatus.Succeeded, PaidAmount = 200m, ChannelCode = "Stripe", CreationTime = monday.AddDays(3) }, // 同周
            new() { Id = Guid.NewGuid(), Status = PaymentStatus.Succeeded, PaidAmount = 300m, ChannelCode = "PayPal", CreationTime = monday.AddDays(7) }, // 下周一
        };

        SetupPaymentQueryable(payments);
        SetupRefundQueryable(new List<Refund>());

        var query = new RevenueTrendQueryDto
        {
            StartTime = monday.AddDays(-1),
            EndTime = monday.AddDays(14),
            Granularity = TrendGranularity.Week
        };

        // Act
        var result = await _service.GetRevenueTrendAsync(query);

        // Assert
        result.Succeeded.ShouldBeTrue();
        var points = result.Data!;
        points.Count.ShouldBe(2); // 2 周

        var week1 = points.First();
        week1.Revenue.ShouldBe(300m); // 100 + 200
        week1.TransactionCount.ShouldBe(2);

        var week2 = points.Last();
        week2.Revenue.ShouldBe(300m);
        week2.TransactionCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetRevenueTrendAsync_MonthlyGranularity_AggregatesByMonth()
    {
        // Arrange
        var jan = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var feb = new DateTime(2026, 2, 10, 10, 0, 0, DateTimeKind.Utc);
        var payments = new List<PaymentEntity>
        {
            new() { Id = Guid.NewGuid(), Status = PaymentStatus.Succeeded, PaidAmount = 500m, ChannelCode = "Stripe", CreationTime = jan },
            new() { Id = Guid.NewGuid(), Status = PaymentStatus.Succeeded, PaidAmount = 300m, ChannelCode = "Stripe", CreationTime = jan.AddDays(5) },
            new() { Id = Guid.NewGuid(), Status = PaymentStatus.Succeeded, PaidAmount = 200m, ChannelCode = "PayPal", CreationTime = feb },
        };

        SetupPaymentQueryable(payments);
        SetupRefundQueryable(new List<Refund>());

        var query = new RevenueTrendQueryDto
        {
            StartTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            Granularity = TrendGranularity.Month
        };

        // Act
        var result = await _service.GetRevenueTrendAsync(query);

        // Assert
        result.Succeeded.ShouldBeTrue();
        var points = result.Data!;
        points.Count.ShouldBe(2); // 1月 + 2月

        var janPoint = points.First(p => p.Date.Month == 1);
        janPoint.Revenue.ShouldBe(800m); // 500 + 300

        var febPoint = points.First(p => p.Date.Month == 2);
        febPoint.Revenue.ShouldBe(200m);
    }

    [Fact]
    public async Task GetRevenueTrendAsync_WithNoData_ReturnsEmptyList()
    {
        // Arrange
        SetupPaymentQueryable(new List<PaymentEntity>());
        SetupRefundQueryable(new List<Refund>());

        var query = new RevenueTrendQueryDto
        {
            StartTime = DateTime.UtcNow.AddDays(-30),
            EndTime = DateTime.UtcNow
        };

        // Act
        var result = await _service.GetRevenueTrendAsync(query);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.ShouldBeEmpty();
    }

    #endregion

    #region GetSubscriptionMetricsAsync Tests

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
        var result = await _service.GetSubscriptionMetricsAsync();

        // Assert
        result.Succeeded.ShouldBeTrue();
        var metrics = result.Data!;

        metrics.ActiveSubscriptions.ShouldBe(4); // 3 existing active + 1 new active
        metrics.TrialSubscriptions.ShouldBe(1);
        metrics.NewSubscriptionsThisMonth.ShouldBe(2); // 1 trial + 1 active created this month
        metrics.CancelledThisMonth.ShouldBe(1);

        // MRR: 4 active subscriptions (3 monthly × $29.99 + 1 yearly $299.88/12 = $24.99)
        var expectedMrr = 29.99m * 3 + 299.88m / 12;
        metrics.MonthlyRecurringRevenue.ShouldBe(expectedMrr);

        // ARPU: MRR / active count
        var expectedArpu = Math.Round(expectedMrr / 4, 2);
        metrics.AverageRevenuePerUser.ShouldBe(expectedArpu);

        // Churn rate: cancelled this month / last month active
        // Last month active = subscriptions created before this month that are still active or cancelled this month
        // = 3 (active from before) + 1 (cancelled this month, was active) = 4
        metrics.ChurnRate.ShouldBe(Math.Round(1m / 4 * 100, 2)); // 25%
    }

    [Fact]
    public async Task GetSubscriptionMetricsAsync_WithNoSubscriptions_ReturnsZeroMetrics()
    {
        // Arrange
        SetupSubscriptionQueryable(new List<Subscription>());

        // Act
        var result = await _service.GetSubscriptionMetricsAsync();

        // Assert
        result.Succeeded.ShouldBeTrue();
        var metrics = result.Data!;
        metrics.ActiveSubscriptions.ShouldBe(0);
        metrics.TrialSubscriptions.ShouldBe(0);
        metrics.MonthlyRecurringRevenue.ShouldBe(0);
        metrics.AverageRevenuePerUser.ShouldBe(0);
        metrics.ChurnRate.ShouldBe(0);
        metrics.PlanDistribution.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetSubscriptionMetricsAsync_PlanDistribution_GroupsByPlanName()
    {
        // Arrange
        var basicPlan = new SubscriptionPlan { Id = Guid.NewGuid(), PlanCode = "BASIC", PlanName = "Basic", Price = 9.99m, CycleType = BillingCycleType.Month, CycleValue = 1, Currency = "USD" };
        var proPlan = new SubscriptionPlan { Id = Guid.NewGuid(), PlanCode = "PRO", PlanName = "Pro", Price = 29.99m, CycleType = BillingCycleType.Month, CycleValue = 1, Currency = "USD" };

        var now = DateTime.UtcNow;
        var subscriptions = new List<Subscription>
        {
            new() { Id = Guid.NewGuid(), Status = SubscriptionStatus.Active, UserId = Guid.NewGuid(), PlanId = basicPlan.Id, Plan = basicPlan, PaidAmount = 9.99m, Currency = "USD", ChannelCode = "Stripe", CreationTime = now.AddMonths(-3) },
            new() { Id = Guid.NewGuid(), Status = SubscriptionStatus.Active, UserId = Guid.NewGuid(), PlanId = basicPlan.Id, Plan = basicPlan, PaidAmount = 9.99m, Currency = "USD", ChannelCode = "Stripe", CreationTime = now.AddMonths(-2) },
            new() { Id = Guid.NewGuid(), Status = SubscriptionStatus.Active, UserId = Guid.NewGuid(), PlanId = proPlan.Id, Plan = proPlan, PaidAmount = 29.99m, Currency = "USD", ChannelCode = "Stripe", CreationTime = now.AddMonths(-1) },
        };

        SetupSubscriptionQueryable(subscriptions);

        // Act
        var result = await _service.GetSubscriptionMetricsAsync();

        // Assert
        result.Succeeded.ShouldBeTrue();
        var distribution = result.Data!.PlanDistribution;
        distribution.Count.ShouldBe(2);

        var basic = distribution.First(p => p.PlanName == "Basic");
        basic.SubscriptionCount.ShouldBe(2);
        basic.Revenue.ShouldBe(19.98m); // 9.99 * 2

        var pro = distribution.First(p => p.PlanName == "Pro");
        pro.SubscriptionCount.ShouldBe(1);
        pro.Revenue.ShouldBe(29.99m);
    }

    #endregion

    #region ExportReconciliationAsync Tests

    [Fact]
    public async Task ExportReconciliationAsync_WithNoData_ReturnsEmptyCsv()
    {
        // Arrange
        SetupPaymentQueryable(new List<PaymentEntity>());
        SetupRefundQueryable(new List<Refund>());

        var query = new ReconciliationQueryDto
        {
            StartTime = DateTime.UtcNow.AddDays(-30),
            EndTime = DateTime.UtcNow
        };

        // Act
        var result = await _service.ExportReconciliationAsync(query);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data.ShouldNotBeNull();
        result.Data.TotalRecords.ShouldBe(0);
        result.Data.TotalRevenue.ShouldBe(0);
        result.Data.TotalRefunds.ShouldBe(0);
        result.Data.NetRevenue.ShouldBe(0);
        result.Data.CsvContent.ShouldContain("TradeNo"); // Header should be present
    }

    [Fact]
    public async Task ExportReconciliationAsync_WithPayments_GeneratesCsvWithCorrectData()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var paymentId1 = Guid.NewGuid();
        var paymentId2 = Guid.NewGuid();

        var payments = new List<PaymentEntity>
        {
            new() { Id = paymentId1, TradeNo = "TRD001", BusinessOrderNo = "ORD001", BusinessType = BusinessType.Order, ChannelCode = "Stripe", PaymentMethod = PaymentMethod.CreditCard, OriginalAmount = 100m, DiscountAmount = 10m, PaidAmount = 90m, Currency = "USD", Status = PaymentStatus.Succeeded, CreationTime = now.AddDays(-2), PaidTime = now.AddDays(-2) },
            new() { Id = paymentId2, TradeNo = "TRD002", BusinessOrderNo = "ORD002", BusinessType = BusinessType.Order, ChannelCode = "PayPal", PaymentMethod = PaymentMethod.PayPal, OriginalAmount = 200m, DiscountAmount = 0m, PaidAmount = 200m, Currency = "USD", Status = PaymentStatus.Succeeded, CreationTime = now.AddDays(-1), PaidTime = now.AddDays(-1) }
        };

        var refunds = new List<Refund>
        {
            new() { Id = Guid.NewGuid(), PaymentId = paymentId1, RefundAmount = 30m, Status = RefundStatus.Succeeded, Reason = "Partial refund", BusinessOrderNo = "ORD001", CreationTime = now }
        };

        SetupPaymentQueryable(payments);
        SetupRefundQueryable(refunds);

        var query = new ReconciliationQueryDto
        {
            StartTime = now.AddDays(-7),
            EndTime = now.AddDays(1)
        };

        // Act
        var result = await _service.ExportReconciliationAsync(query);

        // Assert
        result.Succeeded.ShouldBeTrue();
        var data = result.Data!;
        data.TotalRecords.ShouldBe(2);
        data.TotalRevenue.ShouldBe(290m); // 90 + 200
        data.TotalRefunds.ShouldBe(30m);
        data.NetRevenue.ShouldBe(260m); // 290 - 30
        data.CsvContent.ShouldContain("TRD001");
        data.CsvContent.ShouldContain("TRD002");
        data.FileName.ShouldContain("reconciliation_");
        data.FileName.ShouldEndWith(".csv");
    }

    [Fact]
    public async Task ExportReconciliationAsync_WithChannelFilter_FiltersCorrectly()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var payments = new List<PaymentEntity>
        {
            new() { Id = Guid.NewGuid(), TradeNo = "TRD001", BusinessOrderNo = "ORD001", ChannelCode = "Stripe", PaidAmount = 100m, Currency = "USD", Status = PaymentStatus.Succeeded, CreationTime = now.AddDays(-1) },
            new() { Id = Guid.NewGuid(), TradeNo = "TRD002", BusinessOrderNo = "ORD002", ChannelCode = "PayPal", PaidAmount = 200m, Currency = "USD", Status = PaymentStatus.Succeeded, CreationTime = now.AddDays(-1) }
        };

        SetupPaymentQueryable(payments);
        SetupRefundQueryable(new List<Refund>());

        var query = new ReconciliationQueryDto
        {
            StartTime = now.AddDays(-7),
            EndTime = now,
            ChannelCode = "Stripe"
        };

        // Act
        var result = await _service.ExportReconciliationAsync(query);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data!.TotalRecords.ShouldBe(1);
        result.Data.TotalRevenue.ShouldBe(100m);
        result.Data.CsvContent.ShouldContain("TRD001");
        result.Data.CsvContent.ShouldNotContain("TRD002");
    }

    [Fact]
    public async Task ExportReconciliationAsync_WithStatusFilter_FiltersCorrectly()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var payments = new List<PaymentEntity>
        {
            new() { Id = Guid.NewGuid(), TradeNo = "TRD001", BusinessOrderNo = "ORD001", ChannelCode = "Stripe", PaidAmount = 100m, Currency = "USD", Status = PaymentStatus.Succeeded, CreationTime = now.AddDays(-1) },
            new() { Id = Guid.NewGuid(), TradeNo = "TRD002", BusinessOrderNo = "ORD002", ChannelCode = "Stripe", PaidAmount = 0m, Currency = "USD", Status = PaymentStatus.Failed, CreationTime = now.AddDays(-1) }
        };

        SetupPaymentQueryable(payments);
        SetupRefundQueryable(new List<Refund>());

        var query = new ReconciliationQueryDto
        {
            StartTime = now.AddDays(-7),
            EndTime = now,
            Status = PaymentStatus.Succeeded
        };

        // Act
        var result = await _service.ExportReconciliationAsync(query);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data!.TotalRecords.ShouldBe(1);
        result.Data.CsvContent.ShouldContain("TRD001");
        result.Data.CsvContent.ShouldNotContain("TRD002");
    }

    [Fact]
    public async Task ExportReconciliationAsync_DefaultTimeRange_UsesCurrentMonth()
    {
        // Arrange
        SetupPaymentQueryable(new List<PaymentEntity>());
        SetupRefundQueryable(new List<Refund>());

        var query = new ReconciliationQueryDto(); // No time specified

        // Act
        var result = await _service.ExportReconciliationAsync(query);

        // Assert
        result.Succeeded.ShouldBeTrue();
        // Should use current month start as default
        result.Data!.FileName.ShouldContain(DateTime.UtcNow.ToString("yyyyMM"));
    }

    #endregion

    #region GetPromotionAnalyticsAsync Tests

    private void SetupCouponUsageQueryable(List<CouponUsage> usages)
    {
        var mockQueryable = usages.BuildMock();
        _couponUsageRepositoryMock.Setup(r => r.AsQueryable(false)).Returns(mockQueryable);
        _couponUsageRepositoryMock.As<IQueryable<CouponUsage>>()
            .Setup(q => q.Provider).Returns(mockQueryable.Provider);
        _couponUsageRepositoryMock.As<IQueryable<CouponUsage>>()
            .Setup(q => q.Expression).Returns(mockQueryable.Expression);
        _couponUsageRepositoryMock.As<IQueryable<CouponUsage>>()
            .Setup(q => q.ElementType).Returns(mockQueryable.ElementType);
        _couponUsageRepositoryMock.As<IQueryable<CouponUsage>>()
            .Setup(q => q.GetEnumerator()).Returns(() => mockQueryable.GetEnumerator());
    }

    private void SetupPromotionQueryable(List<Promotion> promotions)
    {
        var mockQueryable = promotions.BuildMock();
        _promotionRepositoryMock.Setup(r => r.AsQueryable(false)).Returns(mockQueryable);
        _promotionRepositoryMock.As<IQueryable<Promotion>>()
            .Setup(q => q.Provider).Returns(mockQueryable.Provider);
        _promotionRepositoryMock.As<IQueryable<Promotion>>()
            .Setup(q => q.Expression).Returns(mockQueryable.Expression);
        _promotionRepositoryMock.As<IQueryable<Promotion>>()
            .Setup(q => q.ElementType).Returns(mockQueryable.ElementType);
        _promotionRepositoryMock.As<IQueryable<Promotion>>()
            .Setup(q => q.GetEnumerator()).Returns(() => mockQueryable.GetEnumerator());
    }

    [Fact]
    public async Task GetPromotionAnalyticsAsync_WithUsageData_ReturnsTopPromotions()
    {
        // Arrange
        var promo1Id = Guid.NewGuid();
        var promo2Id = Guid.NewGuid();
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();
        var user3 = Guid.NewGuid();

        var usages = new List<CouponUsage>
        {
            new() { Id = Guid.NewGuid(), CouponId = promo1Id, UserId = user1, DiscountAmount = 10m, CreationTime = DateTime.UtcNow.AddDays(-5) },
            new() { Id = Guid.NewGuid(), CouponId = promo1Id, UserId = user2, DiscountAmount = 15m, CreationTime = DateTime.UtcNow.AddDays(-3) },
            new() { Id = Guid.NewGuid(), CouponId = promo1Id, UserId = user1, DiscountAmount = 10m, CreationTime = DateTime.UtcNow.AddDays(-1) },
            new() { Id = Guid.NewGuid(), CouponId = promo2Id, UserId = user3, DiscountAmount = 50m, CreationTime = DateTime.UtcNow.AddDays(-2) },
        };

        var promotions = new List<Promotion>
        {
            new() { Id = promo1Id, Name = "Summer Sale", PromotionCode = "SUMMER", DiscountType = DiscountType.Percentage, DiscountValue = 10, UsedCount = 3, TotalUsageLimit = 100, IsActive = true },
            new() { Id = promo2Id, Name = "Welcome", PromotionCode = "WELCOME", DiscountType = DiscountType.Fixed, DiscountValue = 50, UsedCount = 1, TotalUsageLimit = null, IsActive = true },
        };

        SetupCouponUsageQueryable(usages);
        SetupPromotionQueryable(promotions);

        // Act
        var result = await _service.GetPromotionAnalyticsAsync(topN: 10);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data!.Count.ShouldBe(2);

        var top = result.Data[0]; // promo1 has 3 usages
        top.Name.ShouldBe("Summer Sale");
        top.UsageCount.ShouldBe(3);
        top.UniqueUsers.ShouldBe(2); // user1 + user2
        top.TotalDiscountAmount.ShouldBe(35m); // 10+15+10
        top.AverageDiscountPerUse.ShouldBe(11.67m); // 35/3
        top.RedemptionRate.ShouldBe(3m); // 3/100 * 100

        var second = result.Data[1]; // promo2 has 1 usage
        second.Name.ShouldBe("Welcome");
        second.UsageCount.ShouldBe(1);
        second.TotalDiscountAmount.ShouldBe(50m);
        second.RedemptionRate.ShouldBe(-1); // no limit
    }

    [Fact]
    public async Task GetPromotionAnalyticsAsync_WithDateFilter_FiltersCorrectly()
    {
        // Arrange
        var promoId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var usages = new List<CouponUsage>
        {
            new() { Id = Guid.NewGuid(), CouponId = promoId, UserId = Guid.NewGuid(), DiscountAmount = 10m, CreationTime = now.AddDays(-30) },
            new() { Id = Guid.NewGuid(), CouponId = promoId, UserId = Guid.NewGuid(), DiscountAmount = 20m, CreationTime = now.AddDays(-2) },
        };

        var promotions = new List<Promotion>
        {
            new() { Id = promoId, Name = "Test", PromotionCode = "TEST", DiscountType = DiscountType.Fixed, DiscountValue = 10, IsActive = true },
        };

        SetupCouponUsageQueryable(usages);
        SetupPromotionQueryable(promotions);

        // Act — filter to last 7 days
        var result = await _service.GetPromotionAnalyticsAsync(topN: 10, startDate: now.AddDays(-7));

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data!.Count.ShouldBe(1);
        result.Data[0].UsageCount.ShouldBe(1); // only the recent usage
        result.Data[0].TotalDiscountAmount.ShouldBe(20m);
    }

    [Fact]
    public async Task GetPromotionAnalyticsAsync_InvalidTopN_ReturnsFail()
    {
        // Act
        var result = await _service.GetPromotionAnalyticsAsync(topN: 0);

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    [Fact]
    public async Task GetPromotionAnalyticsAsync_EmptyData_ReturnsEmptyList()
    {
        // Arrange
        SetupCouponUsageQueryable(new List<CouponUsage>());

        // Act
        var result = await _service.GetPromotionAnalyticsAsync();

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data!.Count.ShouldBe(0);
    }

    [Fact]
    public async Task GetPromotionAnalyticsAsync_TopN_LimitsResults()
    {
        // Arrange
        var promo1Id = Guid.NewGuid();
        var promo2Id = Guid.NewGuid();
        var promo3Id = Guid.NewGuid();

        var usages = new List<CouponUsage>
        {
            new() { Id = Guid.NewGuid(), CouponId = promo1Id, UserId = Guid.NewGuid(), DiscountAmount = 10m, CreationTime = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), CouponId = promo1Id, UserId = Guid.NewGuid(), DiscountAmount = 10m, CreationTime = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), CouponId = promo1Id, UserId = Guid.NewGuid(), DiscountAmount = 10m, CreationTime = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), CouponId = promo2Id, UserId = Guid.NewGuid(), DiscountAmount = 20m, CreationTime = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), CouponId = promo2Id, UserId = Guid.NewGuid(), DiscountAmount = 20m, CreationTime = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), CouponId = promo3Id, UserId = Guid.NewGuid(), DiscountAmount = 30m, CreationTime = DateTime.UtcNow },
        };

        var promotions = new List<Promotion>
        {
            new() { Id = promo1Id, Name = "P1", PromotionCode = "P1", IsActive = true },
            new() { Id = promo2Id, Name = "P2", PromotionCode = "P2", IsActive = true },
            new() { Id = promo3Id, Name = "P3", PromotionCode = "P3", IsActive = true },
        };

        SetupCouponUsageQueryable(usages);
        SetupPromotionQueryable(promotions);

        // Act — limit to top 2
        var result = await _service.GetPromotionAnalyticsAsync(topN: 2);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data!.Count.ShouldBe(2);
        result.Data[0].UsageCount.ShouldBe(3); // P1
        result.Data[1].UsageCount.ShouldBe(2); // P2
    }

    #endregion

    #region GetRefundAnalyticsAsync Tests

    [Fact]
    public async Task GetRefundAnalyticsAsync_WithRefundData_ReturnsCorrectAnalytics()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var payment1Id = Guid.NewGuid();
        var payment2Id = Guid.NewGuid();

        var refunds = new List<Refund>
        {
            new() { Id = Guid.NewGuid(), PaymentId = payment1Id, RefundAmount = 50m, Reason = "Defective product", Status = RefundStatus.Succeeded, CompletedTime = now, BusinessOrderNo = "O1", CreationTime = now.AddHours(-24) },
            new() { Id = Guid.NewGuid(), PaymentId = payment1Id, RefundAmount = 30m, Reason = "Defective product", Status = RefundStatus.Succeeded, CompletedTime = now.AddHours(-1), BusinessOrderNo = "O2", CreationTime = now.AddHours(-48) },
            new() { Id = Guid.NewGuid(), PaymentId = payment2Id, RefundAmount = 100m, Reason = "Changed mind", Status = RefundStatus.Pending, BusinessOrderNo = "O3", CreationTime = now.AddHours(-12) },
        };

        var payments = new List<PaymentEntity>
        {
            new() { Id = payment1Id, ChannelCode = "Stripe", Status = PaymentStatus.Succeeded },
            new() { Id = payment2Id, ChannelCode = "PayPal", Status = PaymentStatus.Succeeded },
        };

        SetupRefundQueryable(refunds);
        SetupPaymentQueryable(payments);

        // Act
        var result = await _service.GetRefundAnalyticsAsync();

        // Assert
        result.Succeeded.ShouldBeTrue();
        var analytics = result.Data!;

        analytics.TotalRefundCount.ShouldBe(3);
        analytics.TotalRefundAmount.ShouldBe(180m); // 50+30+100

        // Processing time: only 2 completed refunds
        analytics.AverageProcessingTimeHours.ShouldBeGreaterThan(0);

        // Reason breakdown
        analytics.ReasonBreakdown.Count.ShouldBe(2);
        var defective = analytics.ReasonBreakdown.First(r => r.Reason == "Defective product");
        defective.Count.ShouldBe(2);
        defective.Amount.ShouldBe(80m);

        // Channel breakdown
        analytics.ChannelBreakdown.Count.ShouldBe(2);
        var stripe = analytics.ChannelBreakdown.First(c => c.ChannelCode == "Stripe");
        stripe.Count.ShouldBe(2); // 2 refunds from payment1 (Stripe)

        // Status breakdown
        analytics.StatusBreakdown.Count.ShouldBe(2);
        var succeeded = analytics.StatusBreakdown.First(s => s.Status == "Succeeded");
        succeeded.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GetRefundAnalyticsAsync_WithDateFilter_FiltersCorrectly()
    {
        // Arrange
        var now = DateTime.UtcNow;

        var refunds = new List<Refund>
        {
            new() { Id = Guid.NewGuid(), PaymentId = Guid.NewGuid(), RefundAmount = 50m, Reason = "Old", Status = RefundStatus.Succeeded, BusinessOrderNo = "O1", CreationTime = now.AddDays(-60) },
            new() { Id = Guid.NewGuid(), PaymentId = Guid.NewGuid(), RefundAmount = 30m, Reason = "Recent", Status = RefundStatus.Succeeded, BusinessOrderNo = "O2", CreationTime = now.AddDays(-2) },
        };

        SetupRefundQueryable(refunds);
        SetupPaymentQueryable(new List<PaymentEntity>());

        // Act — filter to last 7 days
        var result = await _service.GetRefundAnalyticsAsync(startDate: now.AddDays(-7));

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data!.TotalRefundCount.ShouldBe(1);
        result.Data.TotalRefundAmount.ShouldBe(30m);
    }

    [Fact]
    public async Task GetRefundAnalyticsAsync_EmptyData_ReturnsZeroAnalytics()
    {
        // Arrange
        SetupRefundQueryable(new List<Refund>());

        // Act
        var result = await _service.GetRefundAnalyticsAsync();

        // Assert
        result.Succeeded.ShouldBeTrue();
        var analytics = result.Data!;
        analytics.TotalRefundCount.ShouldBe(0);
        analytics.TotalRefundAmount.ShouldBe(0);
        analytics.AverageProcessingTimeHours.ShouldBe(0);
        analytics.ReasonBreakdown.ShouldBeEmpty();
        analytics.ChannelBreakdown.ShouldBeEmpty();
        analytics.StatusBreakdown.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetRefundAnalyticsAsync_ProcessingTime_CalculatesCorrectly()
    {
        // Arrange
        var now = DateTime.UtcNow;

        var refunds = new List<Refund>
        {
            // Completed in 24 hours
            new() { Id = Guid.NewGuid(), PaymentId = Guid.NewGuid(), RefundAmount = 50m, Reason = "Test", Status = RefundStatus.Succeeded, CompletedTime = now, BusinessOrderNo = "O1", CreationTime = now.AddHours(-24) },
            // Completed in 48 hours
            new() { Id = Guid.NewGuid(), PaymentId = Guid.NewGuid(), RefundAmount = 30m, Reason = "Test", Status = RefundStatus.Succeeded, CompletedTime = now, BusinessOrderNo = "O2", CreationTime = now.AddHours(-48) },
            // Pending (no CompletedTime) — should not affect avg
            new() { Id = Guid.NewGuid(), PaymentId = Guid.NewGuid(), RefundAmount = 100m, Reason = "Test", Status = RefundStatus.Pending, BusinessOrderNo = "O3", CreationTime = now.AddHours(-12) },
        };

        SetupRefundQueryable(refunds);
        SetupPaymentQueryable(new List<PaymentEntity>());

        // Act
        var result = await _service.GetRefundAnalyticsAsync();

        // Assert
        result.Succeeded.ShouldBeTrue();
        // Avg of 24h and 48h = 36h
        result.Data!.AverageProcessingTimeHours.ShouldBe(36.0);
    }

    [Fact]
    public async Task GetRefundAnalyticsAsync_UnspecifiedReason_GroupedAsUnspecified()
    {
        // Arrange
        var refunds = new List<Refund>
        {
            new() { Id = Guid.NewGuid(), PaymentId = Guid.NewGuid(), RefundAmount = 50m, Reason = "", Status = RefundStatus.Succeeded, BusinessOrderNo = "O1", CreationTime = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), PaymentId = Guid.NewGuid(), RefundAmount = 30m, Reason = "   ", Status = RefundStatus.Succeeded, BusinessOrderNo = "O2", CreationTime = DateTime.UtcNow },
        };

        SetupRefundQueryable(refunds);
        SetupPaymentQueryable(new List<PaymentEntity>());

        // Act
        var result = await _service.GetRefundAnalyticsAsync();

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data!.ReasonBreakdown.Count.ShouldBe(1);
        result.Data.ReasonBreakdown[0].Reason.ShouldBe("Unspecified");
        result.Data.ReasonBreakdown[0].Count.ShouldBe(2);
    }

    #endregion
}
