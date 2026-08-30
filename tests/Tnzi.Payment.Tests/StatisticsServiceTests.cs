using Mapster;
using MapsterMapper;
using Microsoft.Extensions.Logging;
using MockQueryable;
using Tnzi.Domain.Repositories;
using Tnzi.Mapster;
using Tnzi.Payment.Dtos;
using Tnzi.Payment.Entities;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Services;
using PaymentEntity = Tnzi.Payment.Entities.Payment;

namespace Tnzi.Payment.Tests;

/// <summary>
/// PaymentStatisticsService 单元测试：支付与退款两块由本服务自己算。
/// </summary>
/// <remarks>
/// 订阅那一块（总览里的活跃订阅数 + 整块订阅指标）已改为向 <c>IPaymentStatisticsContributor</c> 提问，
/// 促销那一块（Top N 促销效果分析）改为向 <c>IPromotionAnalyticsProvider</c> 提问。
/// 本测试**两个供给方都不注册**，跑的是「续费包与折扣包都没装」的宿主。
/// 真正算数的那段促销查询搬去了 <c>Tnzi.Payment.Promotions.Tests/PromotionAnalyticsProviderTests</c>。
/// </remarks>
public class StatisticsServiceTests
{
    private readonly Mock<IRepository<PaymentEntity, Guid>> _paymentRepositoryMock;
    private readonly Mock<IRepository<Refund, Guid>> _refundRepositoryMock;
    private readonly PaymentStatisticsService _service;

    public StatisticsServiceTests()
    {
        // 初始化 Mapster
        var config = new TypeAdapterConfig();
        var mapper = new Mapper(config);
        MapperExtensions.SetMapper(mapper);

        _paymentRepositoryMock = new Mock<IRepository<PaymentEntity, Guid>>();
        _refundRepositoryMock = new Mock<IRepository<Refund, Guid>>();

        // 设置 IServiceProvider mock
        var serviceProviderMock = new Mock<IServiceProvider>();
        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactoryMock.Object);

        _service = new PaymentStatisticsService(
            _paymentRepositoryMock.Object,
            _refundRepositoryMock.Object,
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

    #region GetStatisticsAsync Tests

    [Fact]
    public async Task GetStatisticsAsync_WithNoData_ReturnsZeroStatistics()
    {
        // Arrange
        SetupPaymentQueryable(new List<PaymentEntity>());
        SetupRefundQueryable(new List<Refund>());

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
        // ★ null 而不是 0：没有 IPaymentStatisticsContributor 的宿主根本不做订阅，
        //   而「不适用」与「一个活跃订阅都没有」是两句不同的话。见 SubscriptionsPackageAbsenceTests。
        result.Data.ActiveSubscriptions.ShouldBeNull();
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
        // 没有供给方 → 「不适用」而不是 0（活跃订阅数由 IPaymentStatisticsContributor 回答）
        stats.ActiveSubscriptions.ShouldBeNull();
        stats.ChannelDistribution.Count.ShouldBe(2); // Stripe + PayPal
    }

    [Fact]
    public async Task GetStatisticsAsync_WithDefaultTimeRange_Uses30DayWindow()
    {
        // Arrange
        SetupPaymentQueryable(new List<PaymentEntity>());
        SetupRefundQueryable(new List<Refund>());

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
        // Arrange - 2026-02-02 (周一) ~ 2026-02-15 (周日)
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

    // GetSubscriptionMetricsAsync 的三条用例随续费域搬去了
    // Tnzi.Payment.Subscriptions.Tests/SubscriptionStatisticsContributorTests —— 那一整块
    // 现在由 IPaymentStatisticsContributor 计算。本服务这一侧只剩「没有供给方时回 501」，
    // 钉在 SubscriptionsPackageAbsenceTests。

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

    /// <summary>
    /// 入参校验在父模块，与装没装折扣包无关：非法 topN 永远是 400。
    /// </summary>
    /// <remarks>
    /// 顺序也是断言的一部分 —— 400 必须先于 501 给出。反过来（先答 501）会让一个
    /// 写错了的请求在装了折扣包之后突然变成 400，同一个请求两种答复。
    /// </remarks>
    [Fact]
    public async Task GetPromotionAnalyticsAsync_InvalidTopN_ReturnsFail()
    {
        // Act
        var result = await _service.GetPromotionAnalyticsAsync(topN: 0);

        // Assert
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }

    /// <summary>
    /// 没装折扣包时回 501，并在文案里指名要加载哪个包。
    /// </summary>
    /// <remarks>
    /// ★ <b>不是空列表</b>：空列表是一个答案（「有促销这回事，只是这段时间没人用」），
    /// 会把一次部署疏漏伪装成一条业务结论 —— 运营看着一张空白看板，以为促销没人领。
    /// </remarks>
    [Fact]
    public async Task GetPromotionAnalyticsAsync_WithoutThePromotionsPackage_Answers501NamingTheModule()
    {
        var result = await _service.GetPromotionAnalyticsAsync();

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(501);
        result.Message!.ShouldContain("Tnzi.Payment.Promotions");
        result.Data.ShouldBeNull();
    }

    /// <summary>
    /// 缺席回的是 501 而<b>不是 503</b>。
    /// </summary>
    /// <remarks>
    /// 503 意味着暂时故障，会让监控告警、客户端退避重试 —— 而这件事永远不会自己恢复。
    /// 501 是「本服务器不提供此功能」，恰好就是事实。
    /// </remarks>
    [Fact]
    public async Task GetPromotionAnalyticsAsync_IsNot503()
    {
        (await _service.GetPromotionAnalyticsAsync()).Code.ShouldNotBe(503);
    }

    /// <summary>
    /// 装上供给方时，父模块把它的答案原样交出去 —— 包括<b>空列表</b>这个答案。
    /// </summary>
    /// <remarks>
    /// 这条与上面两条合起来才完整：只测缺席，一个永远返回 null 的假实现也能全绿。
    /// 空列表必须是 200 而不是 501，否则「没人用券」会被报成「本服务器不提供此功能」。
    /// </remarks>
    [Fact]
    public async Task GetPromotionAnalyticsAsync_WithAProvider_PassesItsAnswerThrough_IncludingEmpty()
    {
        var provider = new Mock<IPromotionAnalyticsProvider>();
        provider.Setup(p => p.GetTopPromotionsAsync(It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var service = ServiceWith(promotionAnalytics: provider.Object);

        var result = await service.GetPromotionAnalyticsAsync();

        result.Succeeded.ShouldBeTrue();
        result.Data!.ShouldBeEmpty();
    }

    /// <summary>
    /// 供给方自己说「我答不上来」（返回 null）时，父模块同样回 501 —— 而不是把 null 当成空列表。
    /// </summary>
    [Fact]
    public async Task GetPromotionAnalyticsAsync_WhenTheProviderCannotAnswer_Answers501()
    {
        var provider = new Mock<IPromotionAnalyticsProvider>();
        provider.Setup(p => p.GetTopPromotionsAsync(It.IsAny<int>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<PromotionAnalyticsDto>?)null);

        var result = await ServiceWith(promotionAnalytics: provider.Object).GetPromotionAnalyticsAsync();

        result.Code.ShouldBe(501);
    }

    private PaymentStatisticsService ServiceWith(IPromotionAnalyticsProvider promotionAnalytics)
    {
        var serviceProviderMock = new Mock<IServiceProvider>();
        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        serviceProviderMock.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactoryMock.Object);

        return new PaymentStatisticsService(
            _paymentRepositoryMock.Object,
            _refundRepositoryMock.Object,
            serviceProviderMock.Object,
            promotionAnalytics: promotionAnalytics);
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

        // Act - filter to last 7 days
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
            // Pending (no CompletedTime) - should not affect avg
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

    #region CSV Formula Injection Protection

    [Fact]
    public async Task ExportReconciliationAsync_FormulaLikeBusinessOrderNo_IsEscapedInCsv()
    {
        // Arrange: 用户可控字段以公式起始字符开头(经核心 CsvBuilder 必须前置单引号防注入)
        var now = DateTime.UtcNow;
        var payments = new List<PaymentEntity>
        {
            new() { Id = Guid.NewGuid(), TradeNo = "TRD100", BusinessOrderNo = "=2+5", BusinessType = BusinessType.Order, ChannelCode = "Stripe", PaymentMethod = PaymentMethod.CreditCard, OriginalAmount = 10m, DiscountAmount = 0m, PaidAmount = 10m, Currency = "USD", Status = PaymentStatus.Succeeded, CreationTime = now.AddDays(-1), PaidTime = now.AddDays(-1) }
        };

        SetupPaymentQueryable(payments);
        SetupRefundQueryable(new List<Refund>());

        var query = new ReconciliationQueryDto { StartTime = now.AddDays(-30), EndTime = now };

        // Act
        var result = await _service.ExportReconciliationAsync(query);

        // Assert
        result.Succeeded.ShouldBeTrue();
        result.Data!.CsvContent.ShouldContain("'=2+5");
        result.Data.CsvContent.ShouldNotContain(",=2+5");
    }

    #endregion
}
