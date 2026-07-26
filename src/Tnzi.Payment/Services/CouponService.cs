namespace Tnzi.Payment.Services;

/// <summary>
/// 优惠券服务实现
/// </summary>
public class CouponService : ApplicationService, ICouponService
{
    private readonly IRepository<CouponUsage, Guid> _couponUsageRepository;
    private readonly IRepository<RedemptionCode, Guid> _redemptionCodeRepository;
    private readonly IRepository<Promotion, Guid> _promotionRepository;
    private readonly IPromotionService _promotionService;

    public CouponService(
        IRepository<CouponUsage, Guid> couponUsageRepository,
        IRepository<RedemptionCode, Guid> redemptionCodeRepository,
        IRepository<Promotion, Guid> promotionRepository,
        IPromotionService promotionService,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _couponUsageRepository = Check.NotNull(couponUsageRepository);
        _redemptionCodeRepository = Check.NotNull(redemptionCodeRepository);
        _promotionRepository = Check.NotNull(promotionRepository);
        _promotionService = Check.NotNull(promotionService);
    }

    public async Task<Result<CouponUsageDto>> ApplyCouponAsync(string couponCode, Guid userId, Guid orderId, decimal orderAmount, Guid? paymentId = null, CancellationToken cancellationToken = default)
    {
        // 验证优惠券（在事务外执行，避免长事务）
        var validationResult = await _promotionService.ValidateCouponAsync(couponCode, userId, cancellationToken);
        if (!validationResult.Data?.IsValid ?? true)
            return Fail<CouponUsageDto>(validationResult.Data?.ErrorMessage ?? ErrorCodes.CouponInvalid, 400);

        var promotion = validationResult.Data?.Promotion;
        if (promotion == null)
            return Fail<CouponUsageDto>(ErrorCodes.PromotionNotFound, 404);

        // 计算折扣金额（在事务外执行）
        var discountResult = await _promotionService.CalculateDiscountAsync(couponCode, orderAmount, cancellationToken);
        if (!discountResult.Succeeded)
            return Fail<CouponUsageDto>(discountResult.Message ?? ErrorCodes.CouponInvalid, 400);

        var discountAmount = discountResult.Data?.DiscountAmount ?? 0;

        // 事务保护：优惠券使用记录 + UsedCount 原子更新，防止并发超发
        return await ExecuteInUnitOfWorkAsync(async ct =>
        {
            // 幂等性：同一订单同一优惠券不允许重复使用
            var existingUsage = await _couponUsageRepository.FirstOrDefaultAsync(
                c => c.CouponId == promotion.Id && c.UserId == userId && c.OrderId == orderId, ct);

            if (existingUsage != null)
                return Fail<CouponUsageDto>(ErrorCodes.CouponAlreadyUsedByUser, 400);

            // 不可叠加：同一订单已有其他优惠券
            if (!promotion.Stackable)
            {
                var orderHasOtherCoupon = await _couponUsageRepository
                    .AnyAsync(c => c.OrderId == orderId && c.UserId == userId, ct);

                if (orderHasOtherCoupon)
                    return Fail<CouponUsageDto>(ErrorCodes.CouponAlreadyUsedByUser, 400);
            }

            // per-user 使用次数上限校验（事务内，缩小并发超限窗口）
            if (promotion.PerUserUsageLimit.HasValue)
            {
                var userUsageCount = await _couponUsageRepository.CountAsync(
                    c => c.CouponId == promotion.Id && c.UserId == userId, ct);

                if (userUsageCount >= promotion.PerUserUsageLimit.Value)
                    return Fail<CouponUsageDto>(ErrorCodes.CouponUsageLimitReached, 400);
            }

            // 记录使用
            var couponUsage = new CouponUsage
            {
                CouponId = promotion.Id,
                UserId = userId,
                PaymentId = paymentId,
                OrderId = orderId,
                DiscountAmount = discountAmount
            };

            await _couponUsageRepository.InsertAsync(couponUsage, ct);

            // 原子递增总使用次数（带总量上限 CAS，防止并发超发）
            var incremented = await _promotionRepository.AsQueryable()
                .Where(p => p.Id == promotion.Id
                    && (!p.TotalUsageLimit.HasValue || p.UsedCount < p.TotalUsageLimit.Value))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedCount, x => x.UsedCount + 1), ct);

            if (incremented == 0)
                return Fail<CouponUsageDto>(ErrorCodes.CouponUsageLimitReached, 400);

            Logger.LogInformation("Coupon applied. UserId: {UserId}, Coupon: {Code}, OrderId: {OrderId}, Amount: {Amount}",
                userId, couponCode, orderId, discountAmount);

            return Ok(couponUsage.MapTo<CouponUsageDto>());
        }, cancellationToken);
    }

    public async Task<Result<List<UserCouponDto>>> GetUserAvailableCouponsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var promotions = await _promotionRepository.AsNoTracking()
            .Where(p => p.IsActive &&
                p.StartTime <= DateTime.UtcNow &&
                (!p.EndTime.HasValue || p.EndTime.Value > DateTime.UtcNow) &&
                (!p.TotalUsageLimit.HasValue || p.UsedCount < p.TotalUsageLimit.Value))
            .OrderByDescending(p => p.Priority)
            .ToListAsync(cancellationToken);

        if (promotions.Count == 0)
            return Ok(new List<UserCouponDto>());

        // 批量查询用户在所有有限制的促销上的使用次数（消除 N+1 查询）
        var promotionIds = promotions
            .Where(p => p.PerUserUsageLimit.HasValue)
            .Select(p => p.Id)
            .ToList();

        var userUsageCounts = promotionIds.Count > 0
            ? await _couponUsageRepository.AsNoTracking()
                .Where(c => c.UserId == userId && promotionIds.Contains(c.CouponId))
                .GroupBy(c => c.CouponId)
                .Select(g => new { CouponId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.CouponId, x => x.Count, cancellationToken)
            : new Dictionary<Guid, int>();

        var result = new List<UserCouponDto>();

        foreach (var promotion in promotions)
        {
            var userUsageCount = userUsageCounts.GetValueOrDefault(promotion.Id, 0);

            if (promotion.PerUserUsageLimit.HasValue && userUsageCount >= promotion.PerUserUsageLimit.Value)
                continue;

            result.Add(new UserCouponDto
            {
                Id = promotion.Id,
                CouponCode = promotion.PromotionCode,
                Name = promotion.Name,
                Description = promotion.Description,
                DiscountValue = promotion.DiscountValue,
                DiscountType = promotion.DiscountType,
                MaxDiscountAmount = promotion.MaxDiscountAmount,
                RemainingUsageCount = promotion.PerUserUsageLimit.HasValue
                    ? promotion.PerUserUsageLimit.Value - userUsageCount
                    : -1,
                ExpireTime = promotion.EndTime,
                Stackable = promotion.Stackable
            });
        }

        return Ok(result);
    }

    public async Task<Result<List<CouponUsageDto>>> GetUserUsedCouponsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var usages = await _couponUsageRepository.AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.CreationTime)
            .ProjectTo<CouponUsage, CouponUsageDto>()
            .ToListAsync(cancellationToken);

        return Ok(usages);
    }

    public async Task<Result<UserCouponDto>> RedeemAsync(string code, Guid userId, CancellationToken cancellationToken = default)
    {
        // 事务保护：兑换码数量检查 + RedeemedQuantity 原子更新，防止并发超兑
        return await ExecuteInUnitOfWorkAsync(async ct =>
        {
            var redemptionCode = await _redemptionCodeRepository.FirstOrDefaultAsync(
                r => r.Code == code, ct);

            if (redemptionCode == null)
                return Fail<UserCouponDto>(ErrorCodes.RedemptionCodeNotFound, 404);

            if (redemptionCode.Status != RedemptionCodeStatus.Active)
                return Fail<UserCouponDto>(ErrorCodes.RedemptionCodeNotActive, 400);

            if (redemptionCode.ValidFrom > DateTime.UtcNow)
                return Fail<UserCouponDto>(ErrorCodes.RedemptionCodeNotActive, 400);

            if (redemptionCode.ValidUntil.HasValue && redemptionCode.ValidUntil.Value < DateTime.UtcNow)
                return Fail<UserCouponDto>(ErrorCodes.RedemptionCodeExpired, 400);

            if (redemptionCode.TotalQuantity > 0 && redemptionCode.RedeemedQuantity >= redemptionCode.TotalQuantity)
                return Fail<UserCouponDto>(ErrorCodes.RedemptionCodeLimitReached, 400);

            if (redemptionCode.PerUserLimit.HasValue)
            {
                var userRedemptionCount = await _couponUsageRepository.CountAsync(
                    c => c.CouponId == redemptionCode.PromotionId && c.UserId == userId, ct);

                if (userRedemptionCount >= redemptionCode.PerUserLimit.Value)
                    return Fail<UserCouponDto>(ErrorCodes.RedemptionCodeUserLimitReached, 400);
            }

            var promotion = await _promotionRepository.FirstOrDefaultAsync(
                p => p.Id == redemptionCode.PromotionId, ct);

            if (promotion == null)
                return Fail<UserCouponDto>(ErrorCodes.PromotionNotFound, 404);

            // 更新兑换码：读-改-写在事务内提交，并发保护取决于数据库隔离级别，
            // 并非"原子递增"（对比 ApplyCouponAsync 的 UsedCount 走条件更新 CAS）。
            redemptionCode.RedeemedQuantity++;
            if (redemptionCode.TotalQuantity > 0 && redemptionCode.RedeemedQuantity >= redemptionCode.TotalQuantity)
                redemptionCode.Status = RedemptionCodeStatus.Expired;

            await _redemptionCodeRepository.UpdateAsync(redemptionCode, ct);

            Logger.LogInformation("Redemption code redeemed. UserId: {UserId}, Code: {Code}", userId, code);

            return Ok(new UserCouponDto
            {
                Id = promotion.Id,
                CouponCode = promotion.PromotionCode,
                Name = promotion.Name,
                Description = promotion.Description,
                DiscountValue = promotion.DiscountValue,
                DiscountType = promotion.DiscountType,
                MaxDiscountAmount = promotion.MaxDiscountAmount,
                RemainingUsageCount = promotion.PerUserUsageLimit ?? -1,
                ExpireTime = promotion.EndTime,
                Stackable = promotion.Stackable
            });
        }, cancellationToken);
    }

    public async Task<Result<bool>> CanUseFirstSubscriptionDiscountAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var hasSubscription = await _couponUsageRepository.AsNoTracking()
            .AnyAsync(c => c.UserId == userId && c.Coupon!.FirstSubscriptionOnly, cancellationToken);

        return Ok(!hasSubscription);
    }

    public async Task<Result<string>> CreateRedemptionCodeAsync(Guid promotionId, int quantity, CancellationToken cancellationToken = default)
    {
        var promotion = await _promotionRepository.FirstOrDefaultAsync(p => p.Id == promotionId, cancellationToken);
        if (promotion == null)
            return Fail<string>(ErrorCodes.PromotionNotFound, 404);

        var code = RedemptionCode.GenerateCode();

        var redemptionCode = new RedemptionCode
        {
            Code = code,
            PromotionId = promotionId,
            Type = RedemptionCodeType.Unique,
            Status = RedemptionCodeStatus.Active,
            TotalQuantity = quantity,
            RedeemedQuantity = 0,
            ValidFrom = DateTime.UtcNow,
            ValidUntil = promotion.EndTime
        };

        await _redemptionCodeRepository.InsertAsync(redemptionCode, cancellationToken);

        Logger.LogInformation("Redemption code created. PromotionId: {PromotionId}, Code: {Code}", promotionId, code);

        return Ok<string>(code);
    }
}
