namespace Tnzi.Payment.Promotions.Services;

/// <summary>
/// 促销服务实现
/// </summary>
public class PromotionService : ApplicationService, IPromotionService
{
    private readonly IRepository<Promotion, Guid> _promotionRepository;
    private readonly IRepository<CouponUsage, Guid> _couponUsageRepository;
    private readonly IRepository<UserCoupon, Guid> _userCouponRepository;
    private readonly IOptionsMonitor<PromotionOptions> _promotionOptions;

    /// <summary>
    /// 渠道侧优惠券同步的实现。未加载任何渠道包时为 null，此时同步端点返回 501 并指名要加载什么，
    /// 而不是当作已同步。
    /// </summary>
    private readonly IPaymentChannelCouponSync? _couponSync;

    /// <summary>
    /// 「这个用户以前订阅过吗」的回答者。未加载续费包时为 null，此时
    /// <c>FirstSubscriptionOnly</c> 的券对所有人成立 —— 那不是守卫失效，而是**事实**：
    /// 没有订阅表就没有任何人订阅过，「仅限首次订阅」在这台宿主上本来就退化成「所有人」。
    /// 这个探针只会让校验多拒绝，从不让它少拒绝。
    /// </summary>
    private readonly ISubscriptionHistoryProbe? _subscriptionHistory;

    /// <summary>没有渠道同步实现时给出的提示，同时用于日志与失败结果。</summary>
    private const string CouponSyncMissingMessage =
        "Syncing a promotion to Stripe requires an IPaymentChannelCouponSync implementation. "
        + "Load the Tnzi.Payment.Stripe module ([DependsOn(typeof(PaymentStripeModule))]) "
        + "or register your own IPaymentChannelCouponSync.";

    public PromotionService(
        IRepository<Promotion, Guid> promotionRepository,
        IRepository<CouponUsage, Guid> couponUsageRepository,
        IRepository<UserCoupon, Guid> userCouponRepository,
        IOptionsMonitor<PromotionOptions> promotionOptions,
        IServiceProvider serviceProvider,
        IPaymentChannelCouponSync? couponSync = null,
        ISubscriptionHistoryProbe? subscriptionHistory = null)
        : base(serviceProvider)
    {
        _promotionRepository = Check.NotNull(promotionRepository);
        _couponUsageRepository = Check.NotNull(couponUsageRepository);
        _userCouponRepository = Check.NotNull(userCouponRepository);
        _promotionOptions = Check.NotNull(promotionOptions);
        _couponSync = couponSync;
        _subscriptionHistory = subscriptionHistory;
    }

    public async Task<Result<PromotionDto>> CreateAsync(CreatePromotionDto request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        // 检查促销代码是否已存在（大小写不敏感）
        var existing = await _promotionRepository.FirstOrDefaultAsync(
            p => p.PromotionCode.ToLower() == request.PromotionCode.ToLower(), cancellationToken);

        if (existing != null)
            return Fail<PromotionDto>(ErrorCodes.PromotionCodeAlreadyExists, 409);

        if (request.ApplyScope != ApplyScope.Global && (request.ScopeIds == null || request.ScopeIds.Count == 0))
            return Fail<PromotionDto>(ErrorCodes.CouponScopeMismatch, 400);

        var promotion = new Promotion
        {
            PromotionCode = request.PromotionCode,
            Name = request.Name,
            Description = request.Description,
            Type = request.Type,
            DiscountValue = request.DiscountValue,
            DiscountType = request.DiscountType,
            Currency = request.Currency,
            MaxDiscountAmount = request.MaxDiscountAmount,
            MinimumOrderAmount = request.MinimumOrderAmount,
            ProductType = request.ProductType,
            ApplyScope = request.ApplyScope,
            ScopeIdsJson = SerializeScopeIds(request.ScopeIds),
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            TotalUsageLimit = request.TotalUsageLimit,
            UsedCount = 0,
            PerUserUsageLimit = request.PerUserUsageLimit,
            Stackable = request.Stackable,
            Priority = request.Priority,
            IsActive = true,
            IsPublic = request.IsPublic,
            FirstSubscriptionOnly = request.FirstSubscriptionOnly
        };

        await _promotionRepository.InsertAsync(promotion, cancellationToken);

        Logger.LogInformation("Promotion created. Code: {Code}, Name: {Name}", promotion.PromotionCode, promotion.Name);

        return Ok(ToDto(promotion));
    }

    public async Task<Result> UpdateAsync(Guid id, UpdatePromotionDto request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        var promotion = await _promotionRepository.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (promotion == null)
            return Fail(ErrorCodes.PromotionNotFound, 404);

        if (request.Name != null)
            promotion.Name = request.Name;

        if (request.Description != null)
            promotion.Description = request.Description;

        if (request.DiscountValue.HasValue)
            promotion.DiscountValue = request.DiscountValue.Value;

        if (request.MaxDiscountAmount.HasValue)
            promotion.MaxDiscountAmount = request.MaxDiscountAmount.Value;

        if (request.MinimumOrderAmount.HasValue)
            promotion.MinimumOrderAmount = request.MinimumOrderAmount.Value;

        if (request.EndTime.HasValue)
            promotion.EndTime = request.EndTime.Value;

        if (request.TotalUsageLimit.HasValue)
            promotion.TotalUsageLimit = request.TotalUsageLimit.Value;

        if (request.PerUserUsageLimit.HasValue)
            promotion.PerUserUsageLimit = request.PerUserUsageLimit.Value;

        if (request.Stackable.HasValue)
            promotion.Stackable = request.Stackable.Value;

        if (request.Priority.HasValue)
            promotion.Priority = request.Priority.Value;

        if (request.IsActive.HasValue)
            promotion.IsActive = request.IsActive.Value;

        if (request.IsPublic.HasValue)
            promotion.IsPublic = request.IsPublic.Value;

        await _promotionRepository.UpdateAsync(promotion, cancellationToken);

        Logger.LogInformation("Promotion updated. Id: {Id}", id);

        return Ok();
    }

    public async Task<Result> DeactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var promotion = await _promotionRepository.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (promotion == null)
            return Fail(ErrorCodes.PromotionNotFound, 404);

        promotion.IsActive = false;
        await _promotionRepository.UpdateAsync(promotion, cancellationToken);

        Logger.LogInformation("Promotion deactivated. Id: {Id}", id);

        return Ok();
    }

    public async Task<Result<PromotionDto>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var promotion = await _promotionRepository.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (promotion == null)
            return Fail<PromotionDto>(ErrorCodes.PromotionNotFound, 404);

        return Ok(ToDto(promotion));
    }

    public async Task<Result<PromotionDto>> GetByCodeAsync(string promotionCode, CancellationToken cancellationToken = default)
    {
        var promotion = await _promotionRepository.FirstOrDefaultAsync(
            p => p.PromotionCode.ToLower() == promotionCode.ToLower(), cancellationToken);

        if (promotion == null)
            return Fail<PromotionDto>(ErrorCodes.PromotionNotFound, 404);

        return Ok(ToDto(promotion));
    }

    public async Task<Result<IPagedList<PromotionDto>>> GetListAsync(PromotionQueryDto query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var pagedList = await _promotionRepository.AsNoTracking()
            .Filter(query)
            .OrderByDescending(p => p.Priority)
            .ThenByDescending(p => p.StartTime)
            .ProjectTo<Promotion, PromotionDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        foreach (var item in pagedList.Items)
        {
            item.IsValid = IsPromotionValid(item);
        }

        return Ok(pagedList);
    }

    public async Task<Result<CouponValidationResultDto>> ValidateCouponAsync(CouponApplyContext context, CancellationToken cancellationToken = default)
    {
        Check.NotNull(context);

        var promotion = await _promotionRepository.AsNoTracking().FirstOrDefaultAsync(
            p => p.PromotionCode.ToLower() == context.CouponCode.ToLower(), cancellationToken);

        if (promotion == null)
            return Ok(Invalid(context.CouponCode, ErrorCodes.CouponNotFound));

        var failure = await ValidateInternalAsync(promotion, context, cancellationToken);
        if (failure != null)
            return Ok(Invalid(context.CouponCode, failure));

        var discountAmount = CalculateDiscountAmount(promotion, context.OrderAmount, context.Currency);

        return Ok(new CouponValidationResultDto
        {
            IsValid = true,
            CouponCode = context.CouponCode,
            Promotion = ToDto(promotion),
            DiscountAmount = discountAmount
        });
    }

    public async Task<Result<DiscountCalculationResultDto>> CalculateDiscountAsync(CouponApplyContext context, CancellationToken cancellationToken = default)
    {
        Check.NotNull(context);

        var validationResult = await ValidateCouponAsync(context, cancellationToken);
        var validation = validationResult.Data;

        if (validation is not { IsValid: true })
            return Fail<DiscountCalculationResultDto>(validation?.ErrorMessage ?? ErrorCodes.CouponInvalid, 400);

        return Ok(new DiscountCalculationResultDto
        {
            CouponCode = context.CouponCode,
            OriginalAmount = context.OrderAmount,
            DiscountAmount = validation.DiscountAmount,
            FinalAmount = context.OrderAmount - validation.DiscountAmount,
            DiscountType = validation.Promotion?.DiscountType ?? DiscountType.Fixed
        });
    }

    /// <remarks>
    /// 渠道 SDK 不在本模块里：调用经 <see cref="IPaymentChannelCouponSync"/> 出去，实现住在
    /// 可选的 <c>Tnzi.Payment.Stripe</c> 包。没有实现时**当场失败**（501 + 指名要加载什么），
    /// 而不是报成功后什么也不做 —— 后者会让运营以为渠道侧已经有这张券了，直到某个用户
    /// 在渠道结账页输码被拒才暴露。
    /// </remarks>
    public async Task<Result> SyncToStripeAsync(Guid promotionId, CancellationToken cancellationToken = default)
    {
        if (!_promotionOptions.CurrentValue.EnableStripeCouponSync)
            return Fail("Stripe coupon sync is disabled.", 400);

        if (_couponSync == null)
        {
            Logger.LogError("Promotion sync refused. {Guidance}", CouponSyncMissingMessage);
            return Fail(CouponSyncMissingMessage, 501);
        }

        // 注册的实现必须真的是 Stripe：本方法把返回的标识写进 Promotion.StripeCouponId，
        // 打到别的渠道去而照写不误，是「同步成功了但券在另一家」这种最难查的错。
        if (!string.Equals(_couponSync.ChannelCode, PaymentConstants.StripeChannelCode, StringComparison.OrdinalIgnoreCase))
        {
            Logger.LogError(
                "The registered IPaymentChannelCouponSync serves channel '{ChannelCode}', not '{Expected}'.",
                _couponSync.ChannelCode, PaymentConstants.StripeChannelCode);
            return Fail(
                $"The registered IPaymentChannelCouponSync serves channel '{_couponSync.ChannelCode}', "
                + $"not '{PaymentConstants.StripeChannelCode}'.", 501);
        }

        var promotion = await _promotionRepository.FirstOrDefaultAsync(p => p.Id == promotionId, cancellationToken);
        if (promotion == null)
            return Fail(ErrorCodes.PromotionNotFound, 404);

        var syncResult = await _couponSync.SyncCouponAsync(new PaymentChannelCouponDto
        {
            PromotionId = promotionId,
            PromotionCode = promotion.PromotionCode,
            Name = promotion.Name,
            DiscountType = promotion.DiscountType,
            DiscountValue = promotion.DiscountValue,
            // 币种取促销自身的币种，而不是写死 usd：固定金额折扣与币种强相关
            Currency = promotion.Currency,
            RedeemBy = promotion.EndTime,
            MaxRedemptions = promotion.TotalUsageLimit
        }, cancellationToken);

        if (!syncResult.Succeeded)
        {
            Logger.LogError("Failed to sync promotion to Stripe. PromotionId: {Id}, Reason: {Reason}",
                promotionId, syncResult.Message);
            return Fail(syncResult.Message ?? "Stripe sync failed.", syncResult.Code ?? 400);
        }

        // 报了成功却不给券标识：没有标识就落不了库，也就无从判断渠道侧到底建没建。
        // 这里当失败处理而不是写一个空串，空串会让下一次同步以为「已经同步过了」。
        if (string.IsNullOrWhiteSpace(syncResult.Data))
        {
            Logger.LogError(
                "The coupon sync reported success but returned no channel coupon id. PromotionId: {Id}", promotionId);
            return Fail("The coupon sync reported success but returned no channel coupon id.", 502);
        }

        promotion.StripeCouponId = syncResult.Data;
        await _promotionRepository.UpdateAsync(promotion, cancellationToken);

        Logger.LogInformation("Promotion synced to Stripe. PromotionCode: {Code}", promotion.PromotionCode);
        return Ok();
    }

    /// <summary>
    /// 促销可用性校验的单一实现。返回 null 表示通过，否则返回错误码。
    /// </summary>
    private async Task<string?> ValidateInternalAsync(Promotion promotion, CouponApplyContext context, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        if (!promotion.IsActive)
            return ErrorCodes.CouponExpired;

        if (promotion.StartTime > now)
            return ErrorCodes.CouponNotYetActive;

        if (promotion.EndTime.HasValue && promotion.EndTime.Value < now)
            return ErrorCodes.CouponExpired;

        if (promotion.TotalUsageLimit.HasValue && promotion.UsedCount >= promotion.TotalUsageLimit.Value)
            return ErrorCodes.CouponUsageLimitReached;

        if (promotion.MinimumOrderAmount.HasValue && context.OrderAmount < promotion.MinimumOrderAmount.Value)
            return ErrorCodes.CouponMinimumAmountNotMet;

        // 固定金额折扣与币种绑定：跨币种使用会把 10 USD 当成 10 JPY 抵扣
        if (promotion.DiscountType == DiscountType.Fixed
            && !string.Equals(promotion.Currency, context.Currency, StringComparison.OrdinalIgnoreCase))
        {
            return ErrorCodes.CouponScopeMismatch;
        }

        if (!IsProductTypeApplicable(promotion, context.ProductType))
            return ErrorCodes.CouponScopeMismatch;

        if (!IsScopeApplicable(promotion, context.ScopeId))
            return ErrorCodes.CouponScopeMismatch;

        if (context.UserId == Guid.Empty)
            return null;

        // 每用户使用次数：促销未单独设置时用配置兜底，不再"完全不限"
        var perUserLimit = promotion.PerUserUsageLimit ?? _promotionOptions.CurrentValue.MaxCouponUsagePerUser;
        if (perUserLimit > 0)
        {
            var userUsageCount = await _couponUsageRepository.CountAsync(
                c => c.CouponId == promotion.Id && c.UserId == context.UserId, cancellationToken);

            if (userUsageCount >= perUserLimit)
                return ErrorCodes.CouponUsageLimitReached;
        }

        // 「仅限首次订阅」：判据向续费域提问，本模块不认识订阅表。
        // 没有回答者 = 这台宿主没有订阅 = 没有人是老订户，于是这一条不拒绝任何人。
        if (promotion.FirstSubscriptionOnly && _subscriptionHistory != null)
        {
            var hasSubscription = await _subscriptionHistory.HasAnySubscriptionAsync(context.UserId, cancellationToken);

            if (hasSubscription)
                return ErrorCodes.CouponFirstSubscriptionOnly;
        }

        // 非公开促销必须先通过兑换码领取，否则任何人猜到码就能用
        if (!promotion.IsPublic)
        {
            var holdsCoupon = await _userCouponRepository.AsNoTracking()
                .AnyAsync(u => u.UserId == context.UserId
                    && u.PromotionId == promotion.Id
                    && u.Status == UserCouponStatus.Available
                    && (u.ExpireTime == null || u.ExpireTime > now), cancellationToken);

            if (!holdsCoupon)
                return ErrorCodes.CouponNotHeld;
        }

        return null;
    }

    private static bool IsProductTypeApplicable(Promotion promotion, ProductType requested)
    {
        // 0 是枚举的未定义值（历史数据可能没有显式设置），按"不限"处理，
        // 否则一次校验收紧就会把存量促销全部判为不适用
        if (IsUnrestricted(promotion.ProductType) || IsUnrestricted(requested))
            return true;

        return promotion.ProductType == requested;

        static bool IsUnrestricted(ProductType value) => value == ProductType.All || (int)value == 0;
    }

    private static bool IsScopeApplicable(Promotion promotion, Guid? scopeId)
    {
        if (promotion.ApplyScope == ApplyScope.Global)
            return true;

        var scopeIds = DeserializeScopeIds(promotion.ScopeIdsJson);
        if (scopeIds.Count == 0)
        {
            // 限定了范围却没有配置目标：按"谁都不适用"处理，宁可拒绝也不放开
            return false;
        }

        return scopeId.HasValue && scopeIds.Contains(scopeId.Value);
    }

    /// <summary>
    /// 计算折扣金额
    /// </summary>
    private static decimal CalculateDiscountAmount(Promotion promotion, decimal orderAmount, string currency)
    {
        decimal discountAmount;

        if (promotion.DiscountType == DiscountType.Percentage)
        {
            discountAmount = orderAmount * promotion.DiscountValue / 100;

            if (promotion.MaxDiscountAmount.HasValue && discountAmount > promotion.MaxDiscountAmount.Value)
                discountAmount = promotion.MaxDiscountAmount.Value;
        }
        else
        {
            discountAmount = Math.Min(promotion.DiscountValue, orderAmount);
        }

        // 折扣额不得超过订单金额，且按币种小数位归一，避免出现负数应付额或不可支付的小数
        discountAmount = Math.Clamp(discountAmount, 0, orderAmount);
        return CurrencyInfo.Round(discountAmount, currency);
    }

    private static CouponValidationResultDto Invalid(string couponCode, string errorMessage) => new()
    {
        IsValid = false,
        CouponCode = couponCode,
        ErrorMessage = errorMessage
    };

    private static bool IsPromotionValid(PromotionDto promotion)
    {
        if (!promotion.IsActive)
            return false;

        if (promotion.StartTime > DateTime.UtcNow)
            return false;

        if (promotion.EndTime.HasValue && promotion.EndTime.Value < DateTime.UtcNow)
            return false;

        if (promotion.TotalUsageLimit.HasValue && promotion.UsedCount >= promotion.TotalUsageLimit.Value)
            return false;

        return true;
    }

    /// <summary>
    /// 实体转 DTO 并补齐 ScopeIds。
    /// 库里存的是 JSON 字符串（<see cref="Promotion.ScopeIdsJson"/>），无法在 SQL 投影里解析，
    /// 因此列表查询不带该字段，只在单条读取时于此补齐。
    /// </summary>
    private static PromotionDto ToDto(Promotion promotion)
    {
        var dto = promotion.MapTo<PromotionDto>();
        dto.ScopeIds = DeserializeScopeIds(promotion.ScopeIdsJson);
        dto.IsValid = IsPromotionValid(dto);
        return dto;
    }

    private static string? SerializeScopeIds(List<Guid>? scopeIds)
        => scopeIds is { Count: > 0 } ? JsonSerializer.Serialize(scopeIds) : null;

    private static List<Guid> DeserializeScopeIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<Guid>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
