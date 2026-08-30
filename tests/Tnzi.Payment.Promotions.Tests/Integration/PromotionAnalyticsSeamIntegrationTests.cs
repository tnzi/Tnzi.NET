namespace Tnzi.Payment.Promotions.Tests.Integration;

/// <summary>
/// 缝合线的另一半：装上本包之后，<b>父模块的</b>促销效果分析端点真的用上了本包的答案。
/// </summary>
/// <remarks>
/// <para>
/// 父测试项目那侧断言的是「没装本包 → 501」。只测那一边不够：一个永远返回 <c>null</c> 的
/// 假实现能让两边各自绿 —— 父那侧照样 501（因为供给方说答不上来），子那侧根本没人问。
/// 这组用例跑在真实 SQLite + 真实 DI 上，从<b>父模块的服务</b>进去，看拿不拿得到真数据。
/// </para>
/// <para>
/// 同时钉住「空 ≠ 缺席」的另一半：库里一条核销记录都没有时，端点必须 <b>200 + 空列表</b>，
/// 而不是 501。反过来那就是把「这段时间没人用券」报成「本服务器不提供此功能」。
/// </para>
/// </remarks>
public class PromotionAnalyticsSeamIntegrationTests : PromotionsIntegrationTestBase
{
    private async Task<Promotion> SeedPromotionAsync(string code, int totalLimit)
    {
        var promotion = new Promotion
        {
            PromotionCode = code,
            Name = code,
            IsActive = true,
            IsPublic = true,
            StartTime = DateTime.UtcNow.AddDays(-1),
            DiscountType = DiscountType.Fixed,
            DiscountValue = 10m,
            Currency = "USD",
            Stackable = true,
            TotalUsageLimit = totalLimit,
            UsedCount = 2
        };

        await SeedAsync(promotion);
        return promotion;
    }

    /// <summary>
    /// 父模块的端点把本包算出来的排名原样交出去。
    /// </summary>
    [Fact]
    public async Task TheParentEndpointUsesTheContributedAnalytics()
    {
        var promotion = await SeedPromotionAsync("ANALYTICS", totalLimit: 100);

        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        await SeedAsync(
            new CouponUsage { CouponId = promotion.Id, UserId = userA, DiscountAmount = 10m, BusinessOrderNo = "A-1" },
            new CouponUsage { CouponId = promotion.Id, UserId = userB, DiscountAmount = 20m, BusinessOrderNo = "B-1" });

        var result = await InScopeAsync<IPaymentStatisticsService, Result<List<PromotionAnalyticsDto>>>(
            svc => svc.GetPromotionAnalyticsAsync());

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Code.ShouldNotBe(501);

        var row = result.Data!.ShouldHaveSingleItem();
        row.PromotionId.ShouldBe(promotion.Id);
        row.PromotionCode.ShouldBe("ANALYTICS");
        row.UsageCount.ShouldBe(2);
        row.UniqueUsers.ShouldBe(2);
        row.TotalDiscountAmount.ShouldBe(30m);
        row.IsActive.ShouldBeTrue();
    }

    /// <summary>
    /// 一条核销记录都没有时是 <b>200 + 空列表</b>，不是 501。
    /// </summary>
    /// <remarks>
    /// 「这段时间没人用券」是运营需要看见的业务结论；「本服务器不提供此功能」是部署事实。
    /// 两句话必须分得开，否则装了包的宿主会以为自己没装。
    /// </remarks>
    [Fact]
    public async Task WithNoRedemptionsAtAll_ItIs200AndEmpty_Not501()
    {
        await SeedPromotionAsync("NOBODY", totalLimit: 100);

        var result = await InScopeAsync<IPaymentStatisticsService, Result<List<PromotionAnalyticsDto>>>(
            svc => svc.GetPromotionAnalyticsAsync());

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Code.ShouldNotBe(501);
        result.Data!.ShouldBeEmpty();
    }

    /// <summary>
    /// 入参校验仍在父模块，与装没装本包无关：非法 <c>topN</c> 是 400，而且先于任何查询发生。
    /// </summary>
    [Fact]
    public async Task InvalidTopN_IsStill400_EvenWithThePackageLoaded()
    {
        var result = await InScopeAsync<IPaymentStatisticsService, Result<List<PromotionAnalyticsDto>>>(
            svc => svc.GetPromotionAnalyticsAsync(topN: 0));

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
    }
}
