namespace Tnzi.Payment.Promotions.Services;

/// <summary>
/// 父模块 <see cref="IPromotionAnalyticsProvider"/> 的实现：促销效果分析的那一段查询。
/// </summary>
/// <remarks>
/// 查询体与拆分前 <c>PaymentStatisticsService.GetPromotionAnalyticsAsync</c> 里的<b>一字不差</b>
/// （数据库级 GroupBy + 批量回捞促销），只是换了个住处：核销记录与促销两张表现在是本模块的。
/// 入参校验（topN 必须为正）留在父模块 —— 非法请求应当是 400，与装没装本包无关。
/// </remarks>
public sealed class PromotionAnalyticsProvider : IPromotionAnalyticsProvider
{
    private readonly IRepository<CouponUsage, Guid> _couponUsageRepository;
    private readonly IRepository<Promotion, Guid> _promotionRepository;

    public PromotionAnalyticsProvider(
        IRepository<CouponUsage, Guid> couponUsageRepository,
        IRepository<Promotion, Guid> promotionRepository)
    {
        _couponUsageRepository = Check.NotNull(couponUsageRepository);
        _promotionRepository = Check.NotNull(promotionRepository);
    }

    /// <inheritdoc />
    public async Task<List<PromotionAnalyticsDto>?> GetTopPromotionsAsync(
        int topN, DateTime? startDate, DateTime? endDate, CancellationToken cancellationToken = default)
    {
        // 构建优惠券使用查询（含时间范围过滤）+ 数据库级 GroupBy
        var usageStats = await _couponUsageRepository.AsNoTracking()
            .Where(u => (!startDate.HasValue || u.CreationTime >= startDate.Value)
                && (!endDate.HasValue || u.CreationTime <= endDate.Value))
            .GroupBy(u => u.CouponId)
            .Select(g => new
            {
                PromotionId = g.Key,
                UsageCount = g.Count(),
                UniqueUsers = g.Select(u => u.UserId).Distinct().Count(),
                TotalDiscountAmount = g.Sum(u => u.DiscountAmount)
            })
            .OrderByDescending(s => s.UsageCount)
            .Take(topN)
            .ToListAsync(cancellationToken);

        // 空列表是一个答案（"有促销这回事，只是这段时间没人用"），不是 null。
        // 只有「本供给方给不出」才返回 null，而本类总是给得出。
        if (usageStats.Count == 0)
            return [];

        // 批量加载关联促销信息
        var promotionIds = usageStats.Select(s => s.PromotionId).ToList();
        var promotions = await _promotionRepository.AsNoTracking()
            .Where(p => promotionIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        var promotionLookup = promotions.ToDictionary(p => p.Id);

        return usageStats.Select(s =>
        {
            var promotion = promotionLookup.GetValueOrDefault(s.PromotionId);
            return new PromotionAnalyticsDto
            {
                PromotionId = s.PromotionId,
                Name = promotion?.Name ?? string.Empty,
                PromotionCode = promotion?.PromotionCode ?? string.Empty,
                DiscountType = promotion?.DiscountType.ToString() ?? string.Empty,
                DiscountValue = promotion?.DiscountValue ?? 0,
                UsageCount = s.UsageCount,
                UniqueUsers = s.UniqueUsers,
                TotalDiscountAmount = s.TotalDiscountAmount,
                AverageDiscountPerUse = s.UsageCount > 0 ? Math.Round(s.TotalDiscountAmount / s.UsageCount, 2) : 0,
                RedemptionRate = promotion?.TotalUsageLimit > 0
                    ? Math.Round((decimal)(promotion.UsedCount) / promotion.TotalUsageLimit.Value * 100, 2)
                    : -1,
                IsActive = promotion?.IsActive ?? false
            };
        }).ToList();
    }
}
