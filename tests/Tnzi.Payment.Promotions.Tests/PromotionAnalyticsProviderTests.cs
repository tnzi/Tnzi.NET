using MockQueryable;

namespace Tnzi.Payment.Promotions.Tests;

/// <summary>
/// 促销效果分析的那段查询：Top N 排名、唯一用户数、总折扣、兑换率、时间窗过滤。
/// </summary>
/// <remarks>
/// <para>
/// 这四个用例拆分前住在父测试项目的 <c>StatisticsServiceTests</c> 里，直接测
/// <c>PaymentStatisticsService.GetPromotionAnalyticsAsync</c>。查询体一字未动，
/// 只是换了住处：核销记录与促销两张表现在是本模块的，父模块经
/// <see cref="IPromotionAnalyticsProvider"/> 提问。
/// </para>
/// <para>
/// 父模块那侧留下的是「入参校验 + 缺席回 501」，见
/// <c>Tnzi.Payment.Tests/StatisticsServiceTests</c>。两边合起来才覆盖完整：
/// 只测这边，一个「装了包也回 501」的父模块能全绿；只测那边，一个永远返回 null
/// 的假实现也能全绿。
/// </para>
/// </remarks>
public class PromotionAnalyticsProviderTests
{
    private readonly Mock<IRepository<CouponUsage, Guid>> _couponUsageRepositoryMock = new();
    private readonly Mock<IRepository<Promotion, Guid>> _promotionRepositoryMock = new();
    private readonly PromotionAnalyticsProvider _provider;

    public PromotionAnalyticsProviderTests()
    {
        _provider = new PromotionAnalyticsProvider(_couponUsageRepositoryMock.Object, _promotionRepositoryMock.Object);
    }

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
    public async Task WithUsageData_ReturnsTopPromotions()
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
        var result = await _provider.GetTopPromotionsAsync(10, null, null);

        // Assert
        result.ShouldNotBeNull();
        result!.Count.ShouldBe(2);

        var top = result[0]; // promo1 has 3 usages
        top.Name.ShouldBe("Summer Sale");
        top.UsageCount.ShouldBe(3);
        top.UniqueUsers.ShouldBe(2); // user1 + user2
        top.TotalDiscountAmount.ShouldBe(35m); // 10+15+10
        top.AverageDiscountPerUse.ShouldBe(11.67m); // 35/3
        top.RedemptionRate.ShouldBe(3m); // 3/100 * 100

        var second = result[1]; // promo2 has 1 usage
        second.Name.ShouldBe("Welcome");
        second.UsageCount.ShouldBe(1);
        second.TotalDiscountAmount.ShouldBe(50m);
        second.RedemptionRate.ShouldBe(-1); // no limit
    }

    [Fact]
    public async Task WithDateFilter_FiltersCorrectly()
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

        // Act - filter to last 7 days
        var result = await _provider.GetTopPromotionsAsync(10, now.AddDays(-7), null);

        // Assert
        result.ShouldNotBeNull();
        result!.Count.ShouldBe(1);
        result[0].UsageCount.ShouldBe(1); // only the recent usage
        result[0].TotalDiscountAmount.ShouldBe(20m);
    }

    /// <summary>
    /// 一张券也没人用时返回<b>空列表</b>，而不是 <c>null</c>。
    /// </summary>
    /// <remarks>
    /// ★ 这条区分的是两句不同的话：<c>null</c> 的意思是「我答不上来」（调用方据此回 501），
    /// 空列表的意思是「有促销这回事，只是这段时间没人用」。本供给方总是答得上来，
    /// 所以它<b>永远不返回 null</b>；一旦返回，看板上「没人用券」就会变成「本服务器不提供此功能」。
    /// </remarks>
    [Fact]
    public async Task EmptyData_IsAnAnswer_NotAnAbsence()
    {
        SetupCouponUsageQueryable([]);

        var result = await _provider.GetTopPromotionsAsync(10, null, null);

        result.ShouldNotBeNull();
        result!.ShouldBeEmpty();
    }

    [Fact]
    public async Task TopN_LimitsResults()
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

        // Act - limit to top 2
        var result = await _provider.GetTopPromotionsAsync(2, null, null);

        // Assert
        result.ShouldNotBeNull();
        result!.Count.ShouldBe(2);
        result[0].UsageCount.ShouldBe(3); // P1
        result[1].UsageCount.ShouldBe(2); // P2
    }
}
