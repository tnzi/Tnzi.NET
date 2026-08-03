namespace Tnzi.Payment.Services;

/// <summary>
/// 优惠券服务实现
/// </summary>
public class CouponService : ApplicationService, ICouponService
{
    private readonly IRepository<CouponUsage, Guid> _couponUsageRepository;
    private readonly IRepository<RedemptionCode, Guid> _redemptionCodeRepository;
    private readonly IRepository<UserCoupon, Guid> _userCouponRepository;
    private readonly IRepository<Promotion, Guid> _promotionRepository;
    private readonly IPromotionService _promotionService;

    /// <summary>
    /// 用量统计的空占位：单券场景没有批量统计，剩余次数按促销自身上限展示即可
    /// </summary>
    private static readonly IReadOnlyDictionary<Guid, int> EmptyUsageCounts = new Dictionary<Guid, int>();

    public CouponService(
        IRepository<CouponUsage, Guid> couponUsageRepository,
        IRepository<RedemptionCode, Guid> redemptionCodeRepository,
        IRepository<UserCoupon, Guid> userCouponRepository,
        IRepository<Promotion, Guid> promotionRepository,
        IPromotionService promotionService,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _couponUsageRepository = Check.NotNull(couponUsageRepository);
        _redemptionCodeRepository = Check.NotNull(redemptionCodeRepository);
        _userCouponRepository = Check.NotNull(userCouponRepository);
        _promotionRepository = Check.NotNull(promotionRepository);
        _promotionService = Check.NotNull(promotionService);
    }

    public async Task<Result<CouponPreviewDto>> PreviewAsync(CouponApplyContext context, CancellationToken cancellationToken = default)
    {
        Check.NotNull(context);

        var validation = await _promotionService.ValidateCouponAsync(context, cancellationToken);
        if (validation.Data is not { IsValid: true } valid || valid.Promotion == null)
            return Fail<CouponPreviewDto>(validation.Data?.ErrorMessage ?? ErrorCodes.CouponInvalid, 400);

        return Ok(new CouponPreviewDto
        {
            PromotionId = valid.Promotion.Id,
            CouponCode = valid.Promotion.PromotionCode,
            DiscountAmount = valid.DiscountAmount,
            FinalAmount = context.OrderAmount - valid.DiscountAmount
        });
    }

    public async Task<Result<CouponUsageDto>> ApplyCouponAsync(CouponApplyContext context, CancellationToken cancellationToken = default)
    {
        Check.NotNull(context);

        if (context.UserId == Guid.Empty)
            return Fail<CouponUsageDto>(ErrorCodes.PaymentCouponRequiresUser, 400);

        if (string.IsNullOrWhiteSpace(context.BusinessOrderNo))
            return Fail<CouponUsageDto>(ErrorCodes.CouponInvalid, 400);

        // 校验与折扣计算在事务外完成，避免把外部调用/复杂查询圈进事务
        var validation = await _promotionService.ValidateCouponAsync(context, cancellationToken);
        if (validation.Data is not { IsValid: true } valid || valid.Promotion == null)
            return Fail<CouponUsageDto>(validation.Data?.ErrorMessage ?? ErrorCodes.CouponInvalid, 400);

        var promotionId = valid.Promotion.Id;
        var stackable = valid.Promotion.Stackable;
        var discountAmount = valid.DiscountAmount;

        return await ExecuteInUnitOfWorkAsync(async ct =>
        {
            // 物理事务是延迟开启的（首次 SaveChanges 才 BEGIN），而下面的 CAS 递增是裸 SQL：
            // 不先强开事务，它会在自动提交模式下执行——行锁不持有到事务结束，回滚也撤不掉它。
            await _promotionRepository.EnsureTransactionStartedAsync(ct);

            // 幂等：同一促销 + 同一用户 + 同一业务单号重复核销时返回既有记录。
            // 支付创建可能被重试，报错会让调用方误以为券不可用。
            var existingUsage = await _couponUsageRepository
                .Where(c => c.CouponId == promotionId
                    && c.UserId == context.UserId
                    && c.BusinessOrderNo == context.BusinessOrderNo)
                .Include(c => c.Coupon)
                .FirstOrDefaultAsync(ct);

            if (existingUsage != null)
                return Ok(existingUsage.MapTo<CouponUsageDto>());

            // 不可叠加：同一业务单号上已用过其它券
            if (!stackable)
            {
                var orderHasOtherCoupon = await _couponUsageRepository.AnyAsync(
                    c => c.BusinessOrderNo == context.BusinessOrderNo && c.UserId == context.UserId, ct);

                if (orderHasOtherCoupon)
                    return Fail<CouponUsageDto>(ErrorCodes.CouponAlreadyUsedByUser, 400);
            }

            // ★ 原子递增总使用次数放在所有写入之前（带总量上限 CAS，防止并发超发）。
            // ExecuteInUnitOfWorkAsync 只在**抛异常**时回滚：返回失败 Result 照样提交。
            // 因此任何"可能返回失败"的判定都必须先于写入完成，否则配额没抢到、
            // 使用记录却已落库，用户白白消耗一次机会而调用方看到的是失败。
            var incremented = await _promotionRepository.AsQueryable()
                .Where(p => p.Id == promotionId
                    && (!p.TotalUsageLimit.HasValue || p.UsedCount < p.TotalUsageLimit.Value))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedCount, x => x.UsedCount + 1), ct);

            if (incremented == 0)
                return Fail<CouponUsageDto>(ErrorCodes.CouponUsageLimitReached, 400);

            // 消耗一张用户持券（非公开促销必然有持券，公开促销可有可无）
            var userCoupon = await _userCouponRepository
                .Where(u => u.UserId == context.UserId
                    && u.PromotionId == promotionId
                    && u.Status == UserCouponStatus.Available)
                .OrderBy(u => u.ExpireTime == null)
                .ThenBy(u => u.ExpireTime)
                .FirstOrDefaultAsync(ct);

            var couponUsage = new CouponUsage
            {
                CouponId = promotionId,
                UserId = context.UserId,
                PaymentId = context.PaymentId,
                SubscriptionId = context.SubscriptionId,
                OrderId = context.OrderId,
                BusinessOrderNo = context.BusinessOrderNo,
                DiscountAmount = discountAmount,
                UserCouponId = userCoupon?.Id
            };

            await _couponUsageRepository.InsertAsync(couponUsage, ct);

            if (userCoupon != null)
            {
                userCoupon.Status = UserCouponStatus.Used;
                userCoupon.UsedTime = DateTime.UtcNow;
                userCoupon.CouponUsageId = couponUsage.Id;
                await _userCouponRepository.UpdateAsync(userCoupon, ct);
            }

            Logger.LogInformation("Coupon applied. UserId: {UserId}, Coupon: {Code}, OrderNo: {OrderNo}, Discount: {Discount}",
                context.UserId, context.CouponCode, context.BusinessOrderNo, discountAmount);

            var dto = couponUsage.MapTo<CouponUsageDto>();
            dto.CouponCode = valid.Promotion.PromotionCode;
            return Ok(dto);
        }, cancellationToken);
    }

    public async Task<Result> ReleaseCouponAsync(Guid couponUsageId, CancellationToken cancellationToken = default)
    {
        return await ExecuteInUnitOfWorkAsync(async ct =>
        {
            // 下面的递减是裸 SQL：不先强开物理事务它会在自动提交模式执行，回滚撤不掉
            await _promotionRepository.EnsureTransactionStartedAsync(ct);

            var usage = await _couponUsageRepository.FirstOrDefaultAsync(c => c.Id == couponUsageId, ct);
            if (usage == null)
                return Ok();

            if (usage.UserCouponId.HasValue)
            {
                var userCoupon = await _userCouponRepository.FirstOrDefaultAsync(u => u.Id == usage.UserCouponId.Value, ct);
                if (userCoupon is { Status: UserCouponStatus.Used })
                {
                    userCoupon.Status = UserCouponStatus.Available;
                    userCoupon.UsedTime = null;
                    userCoupon.CouponUsageId = null;
                    await _userCouponRepository.UpdateAsync(userCoupon, ct);
                }
            }

            // 递减不低于 0：即便发生异常路径重复释放，计数也不会跌成负数
            await _promotionRepository.AsQueryable()
                .Where(p => p.Id == usage.CouponId && p.UsedCount > 0)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedCount, x => x.UsedCount - 1), ct);

            await _couponUsageRepository.DeleteAsync(usage, ct);

            Logger.LogInformation("Coupon usage released. UsageId: {UsageId}, OrderNo: {OrderNo}",
                couponUsageId, usage.BusinessOrderNo);

            return Ok();
        }, cancellationToken);
    }

    public async Task<Result<List<UserCouponDto>>> GetUserAvailableCouponsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // 用户可用的券由两部分构成：
        // 1) 已领取且未使用的持券（含非公开促销）；
        // 2) 公开促销（任何人输码即可用）。
        // 此前不区分这两者，直接返回全部生效促销，等于把内部券全网公开。
        var heldCoupons = await _userCouponRepository.AsNoTracking()
            .Where(u => u.UserId == userId
                && u.Status == UserCouponStatus.Available
                && (u.ExpireTime == null || u.ExpireTime > now))
            .Include(u => u.Promotion)
            .OrderBy(u => u.ExpireTime == null)
            .ThenBy(u => u.ExpireTime)
            .ToListAsync(cancellationToken);

        var publicPromotions = await _promotionRepository.AsNoTracking()
            .Where(p => p.IsPublic
                && p.IsActive
                && p.StartTime <= now
                && (!p.EndTime.HasValue || p.EndTime.Value > now)
                && (!p.TotalUsageLimit.HasValue || p.UsedCount < p.TotalUsageLimit.Value))
            .OrderByDescending(p => p.Priority)
            .ToListAsync(cancellationToken);

        var candidateIds = heldCoupons
            .Select(u => u.PromotionId)
            .Concat(publicPromotions.Select(p => p.Id))
            .Distinct()
            .ToList();

        // 批量取用户在候选促销上的使用次数（消除 N+1）
        var userUsageCounts = candidateIds.Count > 0
            ? await _couponUsageRepository.AsNoTracking()
                .Where(c => c.UserId == userId && candidateIds.Contains(c.CouponId))
                .GroupBy(c => c.CouponId)
                .Select(g => new { CouponId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.CouponId, x => x.Count, cancellationToken)
            : [];

        var result = new List<UserCouponDto>();
        var seenPromotionIds = new HashSet<Guid>();

        foreach (var held in heldCoupons)
        {
            if (held.Promotion == null || !IsPromotionUsable(held.Promotion, now))
                continue;

            if (!HasRemainingUserQuota(held.Promotion, userUsageCounts))
                continue;

            result.Add(BuildDto(held.Promotion, userUsageCounts, held));
            seenPromotionIds.Add(held.PromotionId);
        }

        foreach (var promotion in publicPromotions)
        {
            if (seenPromotionIds.Contains(promotion.Id))
                continue;

            if (!HasRemainingUserQuota(promotion, userUsageCounts))
                continue;

            result.Add(BuildDto(promotion, userUsageCounts, null));
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
        if (string.IsNullOrWhiteSpace(code))
            return Fail<UserCouponDto>(ErrorCodes.RedemptionCodeNotFound, 404);

        if (userId == Guid.Empty)
            return Fail<UserCouponDto>(ErrorCodes.PaymentCouponRequiresUser, 400);

        return await ExecuteInUnitOfWorkAsync(async ct =>
        {
            // 下面的兑换数量 CAS 是裸 SQL：不先强开物理事务它会在自动提交模式执行，
            // 行锁不持有到事务结束（并发下超兑），回滚也撤不掉
            await _redemptionCodeRepository.EnsureTransactionStartedAsync(ct);

            var now = DateTime.UtcNow;

            var redemptionCode = await _redemptionCodeRepository.FirstOrDefaultAsync(
                r => r.Code == code, ct);

            if (redemptionCode == null)
                return Fail<UserCouponDto>(ErrorCodes.RedemptionCodeNotFound, 404);

            if (redemptionCode.Status != RedemptionCodeStatus.Active)
                return Fail<UserCouponDto>(ErrorCodes.RedemptionCodeNotActive, 400);

            if (redemptionCode.ValidFrom > now)
                return Fail<UserCouponDto>(ErrorCodes.RedemptionCodeNotActive, 400);

            if (redemptionCode.ValidUntil.HasValue && redemptionCode.ValidUntil.Value < now)
                return Fail<UserCouponDto>(ErrorCodes.RedemptionCodeExpired, 400);

            // 每用户领取上限按"已领取的持券"计数。
            // 此前按核销记录计数，而兑换从不产生核销记录，导致该限制永远不触发。
            if (redemptionCode.PerUserLimit.HasValue)
            {
                var userRedemptionCount = await _userCouponRepository.CountAsync(
                    u => u.RedemptionCodeId == redemptionCode.Id && u.UserId == userId, ct);

                if (userRedemptionCount >= redemptionCode.PerUserLimit.Value)
                    return Fail<UserCouponDto>(ErrorCodes.RedemptionCodeUserLimitReached, 400);
            }

            var promotion = await _promotionRepository.FirstOrDefaultAsync(
                p => p.Id == redemptionCode.PromotionId, ct);

            if (promotion == null)
                return Fail<UserCouponDto>(ErrorCodes.PromotionNotFound, 404);

            if (!IsPromotionUsable(promotion, now))
                return Fail<UserCouponDto>(ErrorCodes.CouponExpired, 400);

            // 原子递增已兑换数量（带总量 CAS）：读-改-写在并发下会超兑
            var claimed = await _redemptionCodeRepository.AsQueryable()
                .Where(r => r.Id == redemptionCode.Id
                    && (r.TotalQuantity <= 0 || r.RedeemedQuantity < r.TotalQuantity))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RedeemedQuantity, x => x.RedeemedQuantity + 1), ct);

            if (claimed == 0)
                return Fail<UserCouponDto>(ErrorCodes.RedemptionCodeLimitReached, 400);

            // 兑换的产物：一张真正落到用户名下的券
            var userCoupon = new UserCoupon
            {
                UserId = userId,
                PromotionId = promotion.Id,
                RedemptionCodeId = redemptionCode.Id,
                RedemptionCode = redemptionCode.Code,
                Status = UserCouponStatus.Available,
                AcquiredTime = now,
                ExpireTime = MinDate(promotion.EndTime, redemptionCode.ValidUntil)
            };

            await _userCouponRepository.InsertAsync(userCoupon, ct);

            // 兑完即置为过期状态，避免后续请求继续打到 CAS 上
            if (redemptionCode.TotalQuantity > 0 && redemptionCode.RedeemedQuantity + 1 >= redemptionCode.TotalQuantity)
            {
                await _redemptionCodeRepository.AsQueryable()
                    .Where(r => r.Id == redemptionCode.Id && r.RedeemedQuantity >= r.TotalQuantity)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, RedemptionCodeStatus.Expired), ct);
            }

            Logger.LogInformation("Redemption code redeemed. UserId: {UserId}, Code: {Code}, Promotion: {Promotion}",
                userId, code, promotion.PromotionCode);

            return Ok(BuildDto(promotion, EmptyUsageCounts, userCoupon));
        }, cancellationToken);
    }

    public async Task<Result<UserCouponDto>> GrantAsync(Guid promotionId, Guid userId, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return Fail<UserCouponDto>(ErrorCodes.PaymentCouponRequiresUser, 400);

        var promotion = await _promotionRepository.FirstOrDefaultAsync(p => p.Id == promotionId, cancellationToken);
        if (promotion == null)
            return Fail<UserCouponDto>(ErrorCodes.PromotionNotFound, 404);

        var now = DateTime.UtcNow;
        var userCoupon = new UserCoupon
        {
            UserId = userId,
            PromotionId = promotion.Id,
            Status = UserCouponStatus.Available,
            AcquiredTime = now,
            ExpireTime = promotion.EndTime
        };

        await _userCouponRepository.InsertAsync(userCoupon, cancellationToken);

        Logger.LogInformation("Coupon granted. UserId: {UserId}, Promotion: {Promotion}", userId, promotion.PromotionCode);

        return Ok(BuildDto(promotion, EmptyUsageCounts, userCoupon));
    }

    public async Task<Result<string>> CreateRedemptionCodeAsync(Guid promotionId, int quantity, CancellationToken cancellationToken = default)
    {
        if (quantity <= 0)
            return Fail<string>(ErrorCodes.RedemptionCodeLimitReached, 400);

        var promotion = await _promotionRepository.FirstOrDefaultAsync(p => p.Id == promotionId, cancellationToken);
        if (promotion == null)
            return Fail<string>(ErrorCodes.PromotionNotFound, 404);

        var code = RedemptionCode.GenerateCode();

        var redemptionCode = new RedemptionCode
        {
            Code = code,
            PromotionId = promotionId,
            Type = quantity > 1 ? RedemptionCodeType.General : RedemptionCodeType.Unique,
            Status = RedemptionCodeStatus.Active,
            TotalQuantity = quantity,
            RedeemedQuantity = 0,
            ValidFrom = DateTime.UtcNow,
            ValidUntil = promotion.EndTime,
            // 唯一码天然一人一次；通用码不限，由促销自身的 per-user 上限收口
            PerUserLimit = quantity > 1 ? null : 1
        };

        await _redemptionCodeRepository.InsertAsync(redemptionCode, cancellationToken);

        Logger.LogInformation("Redemption code created. PromotionId: {PromotionId}, Quantity: {Quantity}", promotionId, quantity);

        return Ok<string>(code);
    }

    public async Task<Result<bool>> CanUseFirstSubscriptionDiscountAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var hasUsedFirstSubscriptionCoupon = await _couponUsageRepository.AsNoTracking()
            .AnyAsync(c => c.UserId == userId && c.Coupon!.FirstSubscriptionOnly, cancellationToken);

        return Ok(!hasUsedFirstSubscriptionCoupon);
    }

    private static bool IsPromotionUsable(Promotion promotion, DateTime now)
    {
        if (!promotion.IsActive)
            return false;

        if (promotion.StartTime > now)
            return false;

        if (promotion.EndTime.HasValue && promotion.EndTime.Value < now)
            return false;

        if (promotion.TotalUsageLimit.HasValue && promotion.UsedCount >= promotion.TotalUsageLimit.Value)
            return false;

        return true;
    }

    private static bool HasRemainingUserQuota(Promotion promotion, IReadOnlyDictionary<Guid, int> usageCounts)
    {
        if (!promotion.PerUserUsageLimit.HasValue)
            return true;

        var used = usageCounts.GetValueOrDefault(promotion.Id, 0);
        return used < promotion.PerUserUsageLimit.Value;
    }

    private static UserCouponDto BuildDto(Promotion promotion, IReadOnlyDictionary<Guid, int> usageCounts, UserCoupon? userCoupon)
    {
        var used = usageCounts.GetValueOrDefault(promotion.Id, 0);

        return new UserCouponDto
        {
            Id = promotion.Id,
            UserCouponId = userCoupon?.Id,
            IsHeld = userCoupon != null,
            CouponCode = promotion.PromotionCode,
            Name = promotion.Name,
            Description = promotion.Description,
            DiscountValue = promotion.DiscountValue,
            DiscountType = promotion.DiscountType,
            MaxDiscountAmount = promotion.MaxDiscountAmount,
            RemainingUsageCount = promotion.PerUserUsageLimit.HasValue
                ? Math.Max(0, promotion.PerUserUsageLimit.Value - used)
                : -1,
            ExpireTime = userCoupon?.ExpireTime ?? promotion.EndTime,
            Stackable = promotion.Stackable
        };
    }

    private static DateTime? MinDate(DateTime? left, DateTime? right)
    {
        if (left == null) return right;
        if (right == null) return left;
        return left < right ? left : right;
    }
}
