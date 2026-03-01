namespace Tnzi.Payment.Services;

/// <summary>
/// 促销服务实现
/// </summary>
public class PromotionService : ApplicationService, IPromotionService
{
    private readonly IRepository<Promotion, Guid> _promotionRepository;
    private readonly IRepository<CouponUsage, Guid> _couponUsageRepository;
    private readonly IOptions<PromotionOptions> _promotionOptions;
    private readonly IPaymentProviderFactory? _providerFactory;

    public PromotionService(
        IRepository<Promotion, Guid> promotionRepository,
        IRepository<CouponUsage, Guid> couponUsageRepository,
        IOptions<PromotionOptions> promotionOptions,
        IServiceProvider serviceProvider,
        IPaymentProviderFactory? providerFactory = null)
        : base(serviceProvider)
    {
        _promotionRepository = Check.NotNull(promotionRepository);
        _couponUsageRepository = Check.NotNull(couponUsageRepository);
        _promotionOptions = Check.NotNull(promotionOptions);
        _providerFactory = providerFactory;
    }

    public async Task<Result<PromotionDto>> CreateAsync(CreatePromotionDto request, CancellationToken cancellationToken = default)
    {
        // 检查促销代码是否已存在（大小写不敏感）
        var existing = await _promotionRepository.FirstOrDefaultAsync(
            p => p.PromotionCode.ToLower() == request.PromotionCode.ToLower(), cancellationToken);

        if (existing != null)
            return Fail<PromotionDto>(ErrorCodes.PromotionCodeAlreadyExists, 409);

        var promotion = new Promotion
        {
            PromotionCode = request.PromotionCode,
            Name = request.Name,
            Description = request.Description,
            Type = request.Type,
            DiscountValue = request.DiscountValue,
            DiscountType = request.DiscountType,
            MaxDiscountAmount = request.MaxDiscountAmount,
            MinimumOrderAmount = request.MinimumOrderAmount,
            ProductType = request.ProductType,
            ApplyScope = request.ApplyScope,
            ScopeIds = request.ScopeIds != null ? JsonSerializer.Serialize(request.ScopeIds) : null,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            TotalUsageLimit = request.TotalUsageLimit,
            UsedCount = 0,
            PerUserUsageLimit = request.PerUserUsageLimit,
            Stackable = request.Stackable,
            Priority = request.Priority,
            IsActive = true,
            FirstSubscriptionOnly = request.FirstSubscriptionOnly
        };

        await _promotionRepository.InsertAsync(promotion, cancellationToken);

        Logger.LogInformation("Promotion created. Code: {Code}, Name: {Name}", promotion.PromotionCode, promotion.Name);

        return Ok(promotion.MapTo<PromotionDto>());
    }

    public async Task<Result> UpdateAsync(Guid id, UpdatePromotionDto request, CancellationToken cancellationToken = default)
    {
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

        return Ok(promotion.MapTo<PromotionDto>());
    }

    public async Task<Result<PromotionDto>> GetByCodeAsync(string promotionCode, CancellationToken cancellationToken = default)
    {
        var promotion = await _promotionRepository.FirstOrDefaultAsync(
            p => p.PromotionCode.ToLower() == promotionCode.ToLower(), cancellationToken);

        if (promotion == null)
            return Fail<PromotionDto>(ErrorCodes.PromotionNotFound, 404);

        return Ok(promotion.MapTo<PromotionDto>());
    }

    public async Task<Result<IPagedList<PromotionDto>>> GetListAsync(PromotionQueryDto query, CancellationToken cancellationToken = default)
    {
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

    public async Task<Result<CouponValidationResultDto>> ValidateCouponAsync(string couponCode, Guid? userId, CancellationToken cancellationToken = default)
    {
        var promotion = await _promotionRepository.FirstOrDefaultAsync(
            p => p.PromotionCode.ToLower() == couponCode.ToLower(), cancellationToken);

        if (promotion == null)
            return Ok(new CouponValidationResultDto { IsValid = false, ErrorMessage = ErrorCodes.CouponNotFound });

        if (!promotion.IsActive)
            return Ok(new CouponValidationResultDto { IsValid = false, CouponCode = couponCode, ErrorMessage = ErrorCodes.CouponExpired });

        if (promotion.StartTime > DateTime.UtcNow)
            return Ok(new CouponValidationResultDto { IsValid = false, CouponCode = couponCode, ErrorMessage = ErrorCodes.CouponNotYetActive });

        if (promotion.EndTime.HasValue && promotion.EndTime.Value < DateTime.UtcNow)
            return Ok(new CouponValidationResultDto { IsValid = false, CouponCode = couponCode, ErrorMessage = ErrorCodes.CouponExpired });

        if (promotion.TotalUsageLimit.HasValue && promotion.UsedCount >= promotion.TotalUsageLimit.Value)
            return Ok(new CouponValidationResultDto { IsValid = false, CouponCode = couponCode, ErrorMessage = ErrorCodes.CouponUsageLimitReached });

        if (userId.HasValue && promotion.PerUserUsageLimit.HasValue)
        {
            var userUsageCount = await _couponUsageRepository.CountAsync(
                c => c.CouponId == promotion.Id && c.UserId == userId.Value, cancellationToken);

            if (userUsageCount >= promotion.PerUserUsageLimit.Value)
                return Ok(new CouponValidationResultDto { IsValid = false, CouponCode = couponCode, ErrorMessage = ErrorCodes.CouponUsageLimitReached });
        }

        return Ok(new CouponValidationResultDto
        {
            IsValid = true,
            CouponCode = couponCode,
            Promotion = promotion.MapTo<PromotionDto>(),
            DiscountAmount = 0
        });
    }

    public async Task<Result<DiscountCalculationResultDto>> CalculateDiscountAsync(string couponCode, decimal orderAmount, CancellationToken cancellationToken = default)
    {
        var validationResult = await ValidateCouponAsync(couponCode, null, cancellationToken);
        if (!validationResult.Data?.IsValid ?? true)
            return Fail<DiscountCalculationResultDto>(validationResult.Data?.ErrorMessage ?? ErrorCodes.CouponInvalid, 400);

        var promotion = validationResult.Data?.Promotion;
        if (promotion == null)
            return Fail<DiscountCalculationResultDto>(ErrorCodes.PromotionNotFound, 404);

        // 检查最低订单金额
        if (promotion.MinimumOrderAmount.HasValue && orderAmount < promotion.MinimumOrderAmount.Value)
            return Fail<DiscountCalculationResultDto>(ErrorCodes.CouponMinimumAmountNotMet, 400);

        // 计算折扣（按优先级选择最优折扣）
        var discountAmount = CalculateDiscountAmount(promotion, orderAmount);

        return Ok(new DiscountCalculationResultDto
        {
            CouponCode = couponCode,
            OriginalAmount = orderAmount,
            DiscountAmount = discountAmount,
            FinalAmount = orderAmount - discountAmount,
            DiscountType = promotion.DiscountType
        });
    }

    public async Task<Result> SyncToStripeAsync(Guid promotionId, CancellationToken cancellationToken = default)
    {
        if (!_promotionOptions.Value.EnableStripeCouponSync)
            return Fail("Stripe coupon sync is disabled.", 400);

        var provider = _providerFactory?.GetProvider("Stripe");
        if (provider is not StripeProvider stripeProvider)
            return Fail("Stripe provider is not available.", 400);

        var promotion = await _promotionRepository.FirstOrDefaultAsync(p => p.Id == promotionId, cancellationToken);
        if (promotion == null)
            return Fail(ErrorCodes.PromotionNotFound, 404);

        try
        {
            var stripeClient = stripeProvider.GetStripeClient();
            var couponService = new Stripe.CouponService(stripeClient);

            var couponOptions = new Stripe.CouponCreateOptions
            {
                Id = promotion.PromotionCode,
                Name = promotion.Name,
                Metadata = new Dictionary<string, string>
                {
                    { "PromotionId", promotionId.ToString() },
                    { "PromotionCode", promotion.PromotionCode }
                }
            };

            if (promotion.DiscountType == DiscountType.Percentage)
            {
                couponOptions.PercentOff = promotion.DiscountValue;
            }
            else
            {
                couponOptions.AmountOff = (long)(promotion.DiscountValue * 100);
                couponOptions.Currency = "usd";
            }

            if (promotion.EndTime.HasValue)
                couponOptions.RedeemBy = promotion.EndTime.Value;

            if (promotion.TotalUsageLimit.HasValue)
                couponOptions.MaxRedemptions = promotion.TotalUsageLimit.Value;

            await couponService.CreateAsync(couponOptions, cancellationToken: cancellationToken);

            Logger.LogInformation("Promotion synced to Stripe. PromotionCode: {Code}", promotion.PromotionCode);
            return Ok();
        }
        catch (Stripe.StripeException ex)
        {
            Logger.LogError(ex, "Failed to sync promotion to Stripe. PromotionId: {Id}", promotionId);
            return Fail($"Stripe sync failed: {ex.Message}", 400);
        }
    }

    /// <summary>
    /// 计算折扣金额
    /// </summary>
    private static decimal CalculateDiscountAmount(PromotionDto promotion, decimal orderAmount)
    {
        decimal discountAmount;

        if (promotion.DiscountType == DiscountType.Percentage)
        {
            discountAmount = orderAmount * promotion.DiscountValue / 100;

            if (promotion.MaxDiscountAmount.HasValue && discountAmount > promotion.MaxDiscountAmount.Value)
                discountAmount = promotion.MaxDiscountAmount.Value;
        }
        else if (promotion.DiscountType == DiscountType.Fixed)
        {
            discountAmount = Math.Min(promotion.DiscountValue, orderAmount);
        }
        else
        {
            discountAmount = 0;
        }

        return Math.Max(0, discountAmount);
    }

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
}
