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
        // 验证优惠券
        var validationResult = await _promotionService.ValidateCouponAsync(couponCode, userId, cancellationToken);
        if (!validationResult.Data?.IsValid ?? true)
            return Fail<CouponUsageDto>(validationResult.Data?.ErrorMessage ?? ErrorCodes.CouponInvalid, 400);

        var promotion = validationResult.Data?.Promotion;
        if (promotion == null)
            return Fail<CouponUsageDto>(ErrorCodes.PromotionNotFound, 404);

        // 幂等性：同一订单同一优惠券不允许重复使用
        var existingUsage = await _couponUsageRepository.FirstOrDefaultAsync(
            c => c.CouponId == promotion.Id && c.UserId == userId && c.OrderId == orderId, cancellationToken);

        if (existingUsage != null)
            return Fail<CouponUsageDto>(ErrorCodes.CouponAlreadyUsedByUser, 400);

        // 不可叠加：同一订单已有其他优惠券
        if (!promotion.Stackable)
        {
            var orderHasOtherCoupon = await _couponUsageRepository.AsNoTracking()
                .AnyAsync(c => c.OrderId == orderId && c.UserId == userId, cancellationToken);

            if (orderHasOtherCoupon)
                return Fail<CouponUsageDto>(ErrorCodes.CouponAlreadyUsedByUser, 400);
        }

        // 计算折扣金额
        var discountResult = await _promotionService.CalculateDiscountAsync(couponCode, orderAmount, cancellationToken);
        if (!discountResult.Succeeded)
            return Fail<CouponUsageDto>(discountResult.Message ?? ErrorCodes.CouponInvalid, 400);

        var discountAmount = discountResult.Data?.DiscountAmount ?? 0;

        // 记录使用
        var couponUsage = new CouponUsage
        {
            CouponId = promotion.Id,
            UserId = userId,
            PaymentId = paymentId,
            OrderId = orderId,
            DiscountAmount = discountAmount
        };

        await _couponUsageRepository.InsertAsync(couponUsage, cancellationToken);

        // 更新使用次数
        var promotionEntity = await _promotionRepository.FirstOrDefaultAsync(p => p.Id == promotion.Id, cancellationToken);
        if (promotionEntity != null)
        {
            promotionEntity.UsedCount++;
            await _promotionRepository.UpdateAsync(promotionEntity, cancellationToken);
        }

        Logger.LogInformation("Coupon applied. UserId: {UserId}, Coupon: {Code}, OrderId: {OrderId}, Amount: {Amount}",
            userId, couponCode, orderId, discountAmount);

        return Ok(couponUsage.MapTo<CouponUsageDto>());
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

        var result = new List<UserCouponDto>();

        foreach (var promotion in promotions)
        {
            var userUsageCount = promotion.PerUserUsageLimit.HasValue
                ? await _couponUsageRepository.CountAsync(
                    c => c.CouponId == promotion.Id && c.UserId == userId, cancellationToken)
                : 0;

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
        var redemptionCode = await _redemptionCodeRepository.FirstOrDefaultAsync(
            r => r.Code == code, cancellationToken);

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
                c => c.CouponId == redemptionCode.PromotionId && c.UserId == userId,
                cancellationToken);

            if (userRedemptionCount >= redemptionCode.PerUserLimit.Value)
                return Fail<UserCouponDto>(ErrorCodes.RedemptionCodeUserLimitReached, 400);
        }

        var promotion = await _promotionRepository.FirstOrDefaultAsync(
            p => p.Id == redemptionCode.PromotionId, cancellationToken);

        if (promotion == null)
            return Fail<UserCouponDto>(ErrorCodes.PromotionNotFound, 404);

        // 更新兑换码
        redemptionCode.RedeemedQuantity++;
        if (redemptionCode.TotalQuantity > 0 && redemptionCode.RedeemedQuantity >= redemptionCode.TotalQuantity)
            redemptionCode.Status = RedemptionCodeStatus.Expired;

        await _redemptionCodeRepository.UpdateAsync(redemptionCode, cancellationToken);

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
