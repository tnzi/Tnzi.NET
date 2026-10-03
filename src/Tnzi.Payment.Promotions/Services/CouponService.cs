using ConflictException = Tnzi.Exceptions.ConflictException;

namespace Tnzi.Payment.Promotions.Services;

/// <summary>
/// 优惠券服务实现：同时回答父模块的支付流程与本模块自己的券包 / 发券两个面。
/// </summary>
/// <remarks>
/// 三个接口一个实现类，是因为它们操作的是同一组表与同一套并发不变量
/// （总用量 CAS、持券状态机、核销幂等键）。分成三个类会把这些不变量摊到三处去维护，
/// 而接口分开已经足够表达「谁能调用什么」。
/// </remarks>
public class CouponService : ApplicationService, ICouponService, ICouponWalletService, ICouponIssuanceService
{
    private readonly IRepository<CouponUsage, Guid> _couponUsageRepository;
    private readonly IRepository<RedemptionCode, Guid> _redemptionCodeRepository;
    private readonly IRepository<UserCoupon, Guid> _userCouponRepository;
    private readonly IRepository<Promotion, Guid> _promotionRepository;
    private readonly IPromotionService _promotionService;
    private readonly IOptionsMonitor<PromotionOptions> _promotionOptions;

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
        IOptionsMonitor<PromotionOptions> promotionOptions,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _promotionOptions = Check.NotNull(promotionOptions);
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

            // 不可叠加是**双向**的：单上已有券而这张不可叠加，拒绝；单上已有一张不可叠加的券，
            // 不论这张可不可叠加也拒绝 —— 只判前者时，先用不可叠加的 A、再用可叠加的 B，A 的承诺就破了。
            // 「单上已有」= 未释放的核销记录（释放即删行）。按用户 + 单号划定：业务单号由消费方给出、不保证跨用户唯一。
            var orderCoupons = await _couponUsageRepository.AsNoTracking()
                .Where(c => c.BusinessOrderNo == context.BusinessOrderNo && c.UserId == context.UserId)
                .Select(c => new { c.OrderSlot, c.Coupon!.Stackable })
                .ToListAsync(ct);

            if (orderCoupons.Count > 0 && (!stackable || orderCoupons.Any(c => !c.Stackable)))
            {
                return Fail<CouponUsageDto>(
                    "This coupon cannot be combined with the coupon already applied to this order.",
                    400, ErrorCodes.CouponNotStackable);
            }

            // 上面的判定是先读后写，并发的两笔核销（不同的券，促销行锁管不到）互相看不见对方未提交的行。
            // 槽位与判定出自同一次读：两笔读到同一个券集合就算出同一个槽位，
            // (UserId, BusinessOrderNo, OrderSlot) 唯一索引只放一笔过去（见 CouponUsage.OrderSlot）。
            var orderSlot = (orderCoupons.Max(c => c.OrderSlot) ?? -1) + 1;

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

            // ★ 上面那条语句**总是**更新促销这一行，因此从这里开始本事务持有它的行锁。
            //   同一张促销上的其它核销要么已经提交（下面读得到），要么还堵在自己那条语句上
            //   （还没写任何东西）—— 于是接下来这两个「按人」的判定读到的数是准的。
            //   放在递增之前读则是另一回事：那时看到的是各自进来时的快照，
            //   三笔并发各自读到「还没用过」，全部放行。
            //   代价是失败时要把刚才那一次递增补回去（见 ReleaseTotalUsageAsync）。

            // 每用户使用次数：与事务外那次校验同一个判据，区别只在这一次是在锁后读的
            var perUserLimit = valid.Promotion.PerUserUsageLimit ?? _promotionOptions.CurrentValue.MaxCouponUsagePerUser;
            if (perUserLimit > 0)
            {
                var userUsageCount = await _couponUsageRepository.CountAsync(
                    c => c.CouponId == promotionId && c.UserId == context.UserId, ct);

                if (userUsageCount >= perUserLimit)
                {
                    await ReleaseTotalUsageAsync(promotionId, ct);
                    return Fail<CouponUsageDto>(ErrorCodes.CouponUsageLimitReached, 400);
                }
            }

            // 消耗一张用户持券：**条件更新**抢占，而不是先读后写。
            // 先读后写时，同一张券会被并发的三笔核销同时读到并各自写一次「已用」——
            // 一张券换来三份折扣，而三条核销记录上写的都是同一个 UserCouponId。
            var claimedCouponId = await ClaimUserCouponAsync(context.UserId, promotionId, ct);

            // 非公开促销必须凭持券使用（事务外那次校验也是这么判的）。抢不到就是没抢到 ——
            // 此前这里**不失败**，照样写一条 UserCouponId 为 null 的核销记录，
            // 于是券被别人抢走的那个人照样拿到了折扣，而账面上没有任何一张券为此消耗。
            if (claimedCouponId == null && !valid.Promotion.IsPublic)
            {
                await ReleaseTotalUsageAsync(promotionId, ct);
                return Fail<CouponUsageDto>(ErrorCodes.CouponNotHeld, 400);
            }

            var couponUsage = new CouponUsage
            {
                CouponId = promotionId,
                UserId = context.UserId,
                PaymentId = context.PaymentId,
                SubscriptionId = context.SubscriptionId,
                OrderId = context.OrderId,
                BusinessOrderNo = context.BusinessOrderNo,
                DiscountAmount = discountAmount,
                UserCouponId = claimedCouponId,
                OrderSlot = orderSlot
            };

            await _couponUsageRepository.InsertAsync(couponUsage, ct);

            // 当场刷出去，让唯一索引的冲突在这里而不是在调用方提交时浮出来。
            // 冲突只能抛异常：撞约束之后事务已不可用（PostgreSQL 直接中止它），而且上面的总用量递增与持券抢占
            // 都要一起撤掉 —— 返回失败 Result 会照样提交。
            try
            {
                await FlushAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation() && ex.Entries.Any(e => e.Entity is CouponUsage))
            {
                throw new ConflictException(
                    "The coupons on this order changed while this coupon was being applied. Please retry.");
            }

            if (claimedCouponId != null)
            {
                await _userCouponRepository.AsQueryable()
                    .Where(u => u.Id == claimedCouponId.Value)
                    .ExecuteUpdateAsync(u => u.SetProperty(x => x.CouponUsageId, couponUsage.Id), ct);
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

    /// <inheritdoc />
    /// <remarks>
    /// 「按支付找到那条核销记录」这一步收在这里，父模块因此不再需要
    /// <c>IRepository&lt;CouponUsage&gt;</c> —— 那是它对本模块表的最后一处直接读取。
    /// 查不到记录返回成功而不是 404：调用它的是支付失败与支付过期两条清扫路径，
    /// 「这笔支付没用券」和「券已经还过了」都是正常情形，报错只会让那两条路径无谓地记一堆日志。
    /// </remarks>
    public async Task<Result> ReleaseCouponForPaymentAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        var usage = await _couponUsageRepository
            .FirstOrDefaultAsync(c => c.PaymentId == paymentId, cancellationToken);

        if (usage == null)
            return Ok();

        return await ReleaseCouponAsync(usage.Id, cancellationToken);
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

            // ★ 按规范形态比对：码由 RedemptionCode.GenerateCode 生成，恒为大写且不含空白与连字符，
            // 而用户照着海报 / 短信敲进来的常是小写、带空格或按四位一组加了连字符。按原样 == 比对时，
            // 结果取决于数据库排序规则（SQL Server / MySQL 默认不区分大小写能兑到，PostgreSQL / SQLite 答 404），
            // 同一个码换一个库就兑不出来。原样写法也比一次，照顾消费方直接写进表里的自定义码。
            var trimmed = code.Trim();
            var normalized = NormalizeRedemptionCode(code);
            var redemptionCode = await _redemptionCodeRepository.FirstOrDefaultAsync(
                r => r.Code == normalized || r.Code == trimmed, ct);

            if (redemptionCode == null)
                return Fail<UserCouponDto>(ErrorCodes.RedemptionCodeNotFound, 404);

            if (redemptionCode.Status != RedemptionCodeStatus.Active)
                return Fail<UserCouponDto>(ErrorCodes.RedemptionCodeNotActive, 400);

            if (redemptionCode.ValidFrom > now)
                return Fail<UserCouponDto>(ErrorCodes.RedemptionCodeNotActive, 400);

            if (redemptionCode.ValidUntil.HasValue && redemptionCode.ValidUntil.Value < now)
                return Fail<UserCouponDto>(ErrorCodes.RedemptionCodeExpired, 400);

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

            // ★ 每用户领取上限**放在总量 CAS 之后**读。上面那条语句总是更新兑换码这一行，
            //   本事务从这里开始持有它的行锁：同一个码的其它兑换要么已经提交（下面数得到），
            //   要么还堵在自己那条语句上（还没插入任何持券）—— 于是这个计数是准的。
            //   放在前面读则是各自进来时的快照，一个人并发提交 N 次会拿到 N 张券。
            //   计数按「已领取的持券」而不是核销记录：兑换从不产生核销记录，
            //   按后者数这条限制永远不触发。
            if (redemptionCode.PerUserLimit.HasValue)
            {
                var userRedemptionCount = await _userCouponRepository.CountAsync(
                    u => u.RedemptionCodeId == redemptionCode.Id && u.UserId == userId, ct);

                if (userRedemptionCount >= redemptionCode.PerUserLimit.Value)
                {
                    // 递增已经落下了，返回失败不会回滚它 —— 不补回去，这个码的名额会莫名其妙地少
                    await _redemptionCodeRepository.AsQueryable()
                        .Where(r => r.Id == redemptionCode.Id && r.RedeemedQuantity > 0)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.RedeemedQuantity, x => x.RedeemedQuantity - 1), ct);

                    return Fail<UserCouponDto>(ErrorCodes.RedemptionCodeUserLimitReached, 400);
                }
            }

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

        // 已停用或已结束的促销发不出券：那张券生下来就是死的，用户看得见却永远用不了。
        // ★ 尚未开始的**照发**：预热期先把券发出去是正常运营动作，
        //   到期后能不能用由核销时的校验说了算，这里拦下它只会挡住一件合法的事。
        if (!promotion.IsActive || (promotion.EndTime.HasValue && promotion.EndTime.Value < now))
            return Fail<UserCouponDto>(ErrorCodes.CouponExpired, 400);

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

    public async Task<Result<string>> CreateRedemptionCodeAsync(Guid promotionId, int quantity, int? perUserLimit = null, CancellationToken cancellationToken = default)
    {
        if (quantity <= 0)
            return Fail<string>(ErrorCodes.RedemptionCodeLimitReached, 400);

        if (perUserLimit < 0)
            return Fail<string>("Per-user limit must be 0 (unlimited) or a positive number.", 400);

        var promotion = await _promotionRepository.FirstOrDefaultAsync(p => p.Id == promotionId, cancellationToken);
        if (promotion == null)
            return Fail<string>(ErrorCodes.PromotionNotFound, 404);

        var code = RedemptionCode.GenerateCode();

        var type = quantity > 1 ? RedemptionCodeType.General : RedemptionCodeType.Unique;
        var redemptionCode = new RedemptionCode
        {
            Code = code,
            PromotionId = promotionId,
            Type = type,
            Status = RedemptionCodeStatus.Active,
            TotalQuantity = quantity,
            RedeemedQuantity = 0,
            ValidFrom = DateTime.UtcNow,
            ValidUntil = promotion.EndTime,
            // ★ 通用码默认每人一张，「不限」要显式传 0。此前这里写 null 并注明「由促销自身的 per-user 上限收口」，
            //   但那条上限只在核销路径读，RedeemAsync 的每用户判定只看这一列 —— 于是一个人能把整批名额兑光，
            //   而 RedeemAsync 里那段「CAS 之后计数 + 补偿」的守卫在生产上没有任何可达输入。
            PerUserLimit = ResolvePerUserLimit(type, perUserLimit)
        };

        await _redemptionCodeRepository.InsertAsync(redemptionCode, cancellationToken);

        Logger.LogInformation("Redemption code created. PromotionId: {PromotionId}, Quantity: {Quantity}", promotionId, quantity);

        return Ok<string>(code);
    }

    public async Task<Result<bool>> CanUseFirstSubscriptionDiscountAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // ★ 与核销守卫逐字同一个判定（PromotionService.IsFirstSubscriptionEligibleAsync）。
        // 此前这里另查「用没用过标了 FirstSubscriptionOnly 的券」：老订户被答成「可用」而核销拒绝，
        // 用过一张但订阅从没建成的人被答成「不可用」而核销放行 —— 预检不能单独多一条或少一条规则。
        return Ok(await _promotionService.IsFirstSubscriptionEligibleAsync(userId, cancellationToken));
    }

    /// <summary>兑换码的规范形态：去掉空白与连字符后转大写（生成的码只由大写字母与数字组成）。</summary>
    internal static string NormalizeRedemptionCode(string code)
        => new string(code.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();

    /// <summary>唯一码恒 1；通用码 null → 1、0 → 不限（存 null）、正数原样。</summary>
    private static int? ResolvePerUserLimit(RedemptionCodeType type, int? requested)
    {
        if (type == RedemptionCodeType.Unique)
            return 1;

        return requested switch
        {
            null => 1,
            0 => null,
            var limit => limit
        };
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

    /// <summary>
    /// 抢占一张该用户在该促销下可用的券，返回被抢到的券 Id；一张都没抢到时返回 null。
    /// </summary>
    /// <remarks>
    /// <b>条件更新而不是先读后写</b>：先读后写时，同一张券会被并发的多笔核销同时读到，
    /// 各自写一次「已用」—— 一张券换来多份折扣，而每条核销记录上写的都是同一个券 Id，
    /// 事后连「多用了几次」都数不出来。这里改成一次
    /// <c>WHERE Id = x AND Status = Available</c> 的更新，只有一个事务的 affected 会是 1。
    /// <para>
    /// 先挑一张再按 Id 抢，而不是直接对「该用户该促销下任意一张可用券」做更新：
    /// 后者在多张券时会把它们全部标成已用。挑选按到期时间升序（先用快过期的），
    /// 挑到的那张被别人抢走时返回 null，由调用方按促销是否公开决定拒绝还是放行。
    /// </para>
    /// </remarks>
    private async Task<Guid?> ClaimUserCouponAsync(Guid userId, Guid promotionId, CancellationToken cancellationToken)
    {
        var candidateIds = await _userCouponRepository.AsNoTracking()
            .Where(u => u.UserId == userId
                && u.PromotionId == promotionId
                && u.Status == UserCouponStatus.Available)
            .OrderBy(u => u.ExpireTime == null)
            .ThenBy(u => u.ExpireTime)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        var usedTime = DateTime.UtcNow;

        foreach (var candidateId in candidateIds)
        {
            var claimed = await _userCouponRepository.AsQueryable()
                .Where(u => u.Id == candidateId && u.Status == UserCouponStatus.Available)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(x => x.Status, UserCouponStatus.Used)
                    .SetProperty(x => x.UsedTime, usedTime), cancellationToken);

            if (claimed > 0)
                return candidateId;
        }

        return null;
    }

    /// <summary>
    /// 把刚才那一次总用量递增补回去。
    /// </summary>
    /// <remarks>
    /// <c>ExecuteInUnitOfWorkAsync</c> 只在**抛异常**时回滚，返回失败 Result 照样提交，
    /// 所以「递增之后才发现要拒绝」必须显式补偿 —— 否则每一次被拒的核销都白烧掉一个名额，
    /// 而促销的剩余次数会莫名其妙地少下去，谁也说不清少在哪。
    /// 条件里带 <c>UsedCount &gt; 0</c> 只是防御：本方法只在刚递增过之后调用。
    /// </remarks>
    private Task ReleaseTotalUsageAsync(Guid promotionId, CancellationToken cancellationToken)
        => _promotionRepository.AsQueryable()
            .Where(p => p.Id == promotionId && p.UsedCount > 0)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedCount, x => x.UsedCount - 1), cancellationToken);

}
