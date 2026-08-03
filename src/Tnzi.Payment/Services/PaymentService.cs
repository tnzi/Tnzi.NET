namespace Tnzi.Payment.Services;

/// <summary>
/// 支付服务实现：建单（折扣 + 计税 + 渠道下单）、查询、关闭、同步与线下确认。
/// 回调处理见 PaymentService.Callback.cs，后台扣款与清扫见 PaymentService.Billing.cs。
/// </summary>
public partial class PaymentService : ApplicationService, IPaymentService
{
    private readonly IRepository<PaymentEntity, Guid> _paymentRepository;
    private readonly IRepository<CouponUsage, Guid> _couponUsageRepository;
    private readonly IPaymentProviderFactory _paymentProviderFactory;
    private readonly ITaxCalculator _taxCalculator;
    private readonly IPaymentMethodService _paymentMethodService;
    private readonly IOptionsMonitor<PaymentOptions> _paymentOptionsMonitor;
    private readonly ICouponService? _couponService;
    private readonly ICache? _cache;

    private const int ExpiredPaymentScanPageSize = 200;

    private PaymentOptions PaymentOptions => _paymentOptionsMonitor.CurrentValue;

    public PaymentService(
        IRepository<PaymentEntity, Guid> paymentRepository,
        IRepository<CouponUsage, Guid> couponUsageRepository,
        IPaymentProviderFactory paymentProviderFactory,
        ITaxCalculator taxCalculator,
        IPaymentMethodService paymentMethodService,
        IOptionsMonitor<PaymentOptions> paymentOptionsMonitor,
        IServiceProvider serviceProvider,
        ICouponService? couponService = null,
        ICache? cache = null)
        : base(serviceProvider)
    {
        _paymentRepository = Check.NotNull(paymentRepository);
        _couponUsageRepository = Check.NotNull(couponUsageRepository);
        _paymentProviderFactory = Check.NotNull(paymentProviderFactory);
        _taxCalculator = Check.NotNull(taxCalculator);
        _paymentMethodService = Check.NotNull(paymentMethodService);
        _paymentOptionsMonitor = Check.NotNull(paymentOptionsMonitor);
        _couponService = couponService;
        _cache = cache;
    }

    /// <summary>
    /// 生成交易流水号（使用 Snowflake ID 避免高并发冲突）
    /// </summary>
    private static string GenerateTradeNo()
    {
        return $"{PaymentConstants.TradeNoPrefix}{IdHelper.NextId()}";
    }

    public async Task<Result<PaymentOrderResultDto>> CreatePaymentAsync(CreatePaymentDto request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        if (request.Amount <= 0)
            return Fail<PaymentOrderResultDto>(ErrorCodes.PaymentInvalidAmount, 400);

        var channelCode = string.IsNullOrWhiteSpace(request.ChannelCode)
            ? PaymentOptions.DefaultChannelCode
            : request.ChannelCode;

        var provider = _paymentProviderFactory.GetProvider(channelCode);
        if (provider == null)
            return Fail<PaymentOrderResultDto>(ErrorCodes.PaymentChannelNotSupported, 400);

        var paymentMethod = request.PaymentMethod ?? PaymentMethod.CreditCard;
        if (!provider.IsSupported(paymentMethod))
            return Fail<PaymentOrderResultDto>(ErrorCodes.PaymentChannelNotSupported, 400);

        var currency = ResolveCurrency(request.Currency, channelCode);
        var userId = CurrentUser?.Id;

        // 1. 折扣：先试算，确认渠道能建单后再核销，避免"券被吃掉但订单没建成"
        var discount = await PreviewCouponAsync(request, currency, userId, cancellationToken);
        if (!discount.Succeeded)
            return Fail<PaymentOrderResultDto>(discount.Message ?? ErrorCodes.CouponInvalid, discount.Code ?? 400);

        var discountAmount = discount.Data?.DiscountAmount ?? 0;
        var netAmount = CurrencyInfo.Round(request.Amount - discountAmount, currency);

        // 2. 计税：应付额以计税结果为准，回调金额校验也以它为基准
        var tax = await _taxCalculator.CalculateAsync(new TaxCalculationRequest
        {
            NetAmount = netAmount,
            Currency = currency,
            BusinessType = request.BusinessType
        }, cancellationToken);

        if (!tax.Succeeded || tax.Data == null)
            return Fail<PaymentOrderResultDto>(tax.Message ?? ErrorCodes.PaymentCreationFailed, tax.Code ?? 400);

        var payableAmount = tax.Data.PayableAmount;
        if (payableAmount <= 0)
            return Fail<PaymentOrderResultDto>(ErrorCodes.PaymentInvalidAmount, 400);

        var payment = new PaymentEntity
        {
            TradeNo = GenerateTradeNo(),
            BusinessOrderNo = request.BusinessOrderNo,
            BusinessType = request.BusinessType,
            OriginalAmount = request.Amount,
            PaidAmount = 0,
            DiscountAmount = discountAmount,
            TaxAmount = tax.Data.TaxAmount,
            PayableAmount = payableAmount,
            Currency = currency,
            Status = PaymentStatus.Pending,
            ChannelCode = channelCode,
            PaymentMethod = paymentMethod,
            Description = request.Description,
            UserId = userId,
            CustomerName = CurrentUser?.UserName,
            CustomerEmail = CurrentUser?.Email,
            CouponId = discount.Data?.PromotionId,
            ExpireTime = ResolveExpireTime(request.ExpireMinutes, channelCode),
            ExtraData = request.ExtraData
        };

        await _paymentRepository.InsertAsync(payment, cancellationToken);

        // 3. 核销优惠券（拿到 paymentId 后写核销记录，失败即中止且不产生订单）
        Guid? couponUsageId = null;
        if (discount.Data != null && userId.HasValue)
        {
            var applied = await _couponService!.ApplyCouponAsync(
                BuildCouponContext(request, currency, userId.Value, payment.Id), cancellationToken);

            if (!applied.Succeeded || applied.Data == null)
            {
                payment.Status = PaymentStatus.Failed;
                payment.ChannelResponse = applied.Message;
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                return Fail<PaymentOrderResultDto>(applied.Message ?? ErrorCodes.CouponInvalid, applied.Code ?? 400);
            }

            couponUsageId = applied.Data.Id;
        }

        // 4. 渠道下单
        var result = await provider.CreatePaymentAsync(new PaymentProviderCreateDto
        {
            TradeNo = payment.TradeNo,
            BusinessOrderNo = request.BusinessOrderNo,
            Amount = payableAmount,
            Currency = currency,
            Description = request.Description,
            ExpireTime = payment.ExpireTime,
            ReturnUrl = request.ReturnUrl ?? PaymentOptions.DefaultReturnUrl,
            ExtraData = request.ExtraData
        });

        if (!result.Succeeded || result.Data == null)
        {
            payment.Status = PaymentStatus.Failed;
            payment.ChannelResponse = result.Message;
            await _paymentRepository.UpdateAsync(payment, cancellationToken);

            // 渠道建单失败必须把券还给用户，否则一次失败就白扣一张券
            if (couponUsageId.HasValue)
                await _couponService!.ReleaseCouponAsync(couponUsageId.Value, cancellationToken);

            return Fail<PaymentOrderResultDto>(result.Message ?? ErrorCodes.PaymentCreationFailed);
        }

        // 线下渠道保持 Pending 等待人工确认；在线渠道进入 Processing 等待回调
        payment.Status = IsOfflineChannel(channelCode) ? PaymentStatus.Pending : PaymentStatus.Processing;
        payment.ExternalTradeNo = result.Data.ExternalTradeNo;
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        Logger.LogInformation(
            "Payment created. TradeNo: {TradeNo}, Channel: {Channel}, Original: {Original}, Discount: {Discount}, Tax: {Tax}, Payable: {Payable} {Currency}",
            payment.TradeNo, channelCode, payment.OriginalAmount, discountAmount, payment.TaxAmount, payableAmount, currency);

        return Ok(new PaymentOrderResultDto
        {
            TradeNo = payment.TradeNo,
            PayParams = result.Data.PayParams,
            PayUrl = result.Data.PayUrl,
            ExpireTime = payment.ExpireTime,
            Amount = payableAmount,
            OriginalAmount = payment.OriginalAmount,
            DiscountAmount = discountAmount,
            TaxAmount = payment.TaxAmount,
            AppliedCouponCode = discount.Data?.CouponCode,
            Currency = currency
        });
    }

    public async Task<Result<PaymentDto>> GetPaymentAsync(string tradeNo, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var payment = await FindOwnedAsync(tradeNo, ownerUserId, cancellationToken);
        if (payment == null)
            return Fail<PaymentDto>(ErrorCodes.PaymentNotFound, 404);

        return Ok(payment.MapTo<PaymentDto>());
    }

    public async Task<Result<IPagedList<PaymentDto>>> GetPaymentListAsync(PaymentQueryDto query, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var queryable = _paymentRepository.AsNoTracking();
        if (ownerUserId.HasValue)
            queryable = queryable.Where(p => p.UserId == ownerUserId.Value);

        var pagedList = await queryable
            .Filter(query)
            .ProjectTo<PaymentEntity, PaymentDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        return Ok(pagedList);
    }

    public async Task<Result> ClosePaymentAsync(string tradeNo, string? reason, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var payment = await FindOwnedAsync(tradeNo, ownerUserId, cancellationToken);
        if (payment == null)
            return Fail(ErrorCodes.PaymentNotFound, 404);

        if (payment.Status != PaymentStatus.Pending && payment.Status != PaymentStatus.Processing)
            return Fail(ErrorCodes.PaymentCannotClose, 400);

        // CAS：并发回调可能正把这笔支付置为成功，条件更新确保不会把已成功的订单关掉
        var affected = await _paymentRepository.AsQueryable()
            .Where(p => p.Id == payment.Id
                && (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Processing))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PaymentStatus.Closed), cancellationToken);

        if (affected == 0)
            return Fail(ErrorCodes.PaymentCannotClose, 409);

        Logger.LogInformation("Payment closed. TradeNo: {TradeNo}, Reason: {Reason}", tradeNo, reason);

        return Ok();
    }

    public async Task<Result<PaymentParamsDto>> GetPaymentParamsAsync(string tradeNo, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var payment = await FindOwnedAsync(tradeNo, ownerUserId, cancellationToken);
        if (payment == null)
            return Fail<PaymentParamsDto>(ErrorCodes.PaymentNotFound, 404);

        var provider = _paymentProviderFactory.GetProvider(payment.ChannelCode);
        if (provider == null)
            return Fail<PaymentParamsDto>(ErrorCodes.PaymentChannelNotSupported, 400);

        var result = await provider.GetPaymentParamsAsync(payment.ExternalTradeNo ?? tradeNo);
        if (!result.Succeeded)
            return Fail<PaymentParamsDto>(result.Message ?? ErrorCodes.PaymentChannelNotSupported, result.Code ?? 400);

        var data = result.Data ?? new PaymentParamsDto();
        data.TradeNo = tradeNo;
        return Ok(data);
    }

    public async Task<Result> SyncOrderAsync(string tradeNo, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var payment = await FindOwnedAsync(tradeNo, ownerUserId, cancellationToken);
        if (payment == null)
            return Fail(ErrorCodes.PaymentNotFound, 404);

        var provider = _paymentProviderFactory.GetProvider(payment.ChannelCode);
        if (provider == null)
            return Fail(ErrorCodes.PaymentChannelNotSupported, 400);

        var result = await provider.SyncOrderAsync(payment.ExternalTradeNo ?? tradeNo);
        if (!result.Succeeded)
            return Fail(result.Message ?? ErrorCodes.PaymentCreationFailed);

        // 终态防回退（见 IsTerminalStatus）：已成功/已退款/已关闭的支付不再被渠道同步改写状态，
        // 否则 Refunded 会被同步回 Succeeded 从而放开二次退款。
        if (IsTerminalStatus(payment.Status))
        {
            if (!string.IsNullOrEmpty(result.Data?.ExternalTradeNo) && payment.ExternalTradeNo != result.Data.ExternalTradeNo)
            {
                payment.ExternalTradeNo = result.Data.ExternalTradeNo;
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
            }
            return Ok();
        }

        var syncedStatus = result.Data?.Status ?? payment.Status;

        // 同步到成功时必须与回调走同一条推进路径（含金额校验与事件发布），
        // 否则用户手动点"同步"就能绕开回调链路，订阅/发票都收不到通知。
        if (syncedStatus == PaymentStatus.Succeeded)
        {
            var paidAmount = result.Data!.Amount > 0 ? result.Data.Amount : payment.PayableAmount;
            return await ApplySucceededAsync(payment, paidAmount, result.Data.ExternalTradeNo, JsonSerializer.Serialize(result.Data), cancellationToken);
        }

        if (syncedStatus == PaymentStatus.Failed)
            return await ApplyFailedAsync(payment, result.Data?.FailReason ?? "Synced as failed", JsonSerializer.Serialize(result.Data), cancellationToken);

        // 中间态：只在确实有变化时写库。渠道对未完成订单常年回同一个中间态，
        // 无条件写会给每次同步产生一条无意义的 UPDATE 与审计记录。
        var externalChanged = !string.IsNullOrEmpty(result.Data?.ExternalTradeNo)
            && payment.ExternalTradeNo != result.Data.ExternalTradeNo;

        if (!externalChanged && syncedStatus == payment.Status)
            return Ok();

        if (externalChanged)
            payment.ExternalTradeNo = result.Data!.ExternalTradeNo;

        payment.Status = syncedStatus;
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        return Ok();
    }

    public async Task<Result<PaymentDto>> ConfirmOfflinePaymentAsync(string tradeNo, ConfirmOfflinePaymentDto request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);
        Check.NotNullOrWhiteSpace(request.Reference);

        var payment = await _paymentRepository.FirstOrDefaultAsync(p => p.TradeNo == tradeNo, cancellationToken);
        if (payment == null)
            return Fail<PaymentDto>(ErrorCodes.PaymentNotFound, 404);

        // 只允许线下渠道人工入账：在线渠道必须以渠道回调为准，
        // 否则运营就能在没有真实收款的情况下把订单标记为已付。
        if (!IsOfflineChannel(payment.ChannelCode))
            return Fail<PaymentDto>(ErrorCodes.PaymentManualConfirmChannelOnly, 400);

        if (payment.Status is not (PaymentStatus.Pending or PaymentStatus.Processing))
            return Fail<PaymentDto>(ErrorCodes.PaymentCannotConfirm, 400);

        var paidAmount = request.PaidAmount ?? payment.PayableAmount;
        var confirmation = JsonSerializer.Serialize(new
        {
            request.Reference,
            request.Remark,
            ConfirmedBy = CurrentUser?.Id,
            ConfirmedAt = DateTime.UtcNow
        });

        var result = await ApplySucceededAsync(
            payment,
            paidAmount,
            request.Reference,
            confirmation,
            cancellationToken,
            request.PaidTime);

        if (!result.Succeeded)
            return Fail<PaymentDto>(result.Message ?? ErrorCodes.PaymentCannotConfirm, result.Code ?? 400);

        Logger.LogInformation("Offline payment confirmed. TradeNo: {TradeNo}, Amount: {Amount}, Reference: {Reference}",
            tradeNo, paidAmount, request.Reference);

        return Ok(payment.MapTo<PaymentDto>());
    }

    private Task<PaymentEntity?> FindOwnedAsync(string tradeNo, Guid? ownerUserId, CancellationToken cancellationToken)
    {
        return _paymentRepository.FirstOrDefaultAsync(
            p => p.TradeNo == tradeNo && (!ownerUserId.HasValue || p.UserId == ownerUserId), cancellationToken);
    }

    /// <summary>
    /// 币种优先级：请求指定 &gt; 渠道配置 &gt; 全局默认。
    /// 渠道币种此前从未被读取，多币种部署下所有订单都会退化成全局默认币种。
    /// </summary>
    private string ResolveCurrency(string? requested, string channelCode)
    {
        if (!string.IsNullOrWhiteSpace(requested))
            return requested;

        var channelCurrency = PaymentOptions.Channels
            .FirstOrDefault(x => string.Equals(x.Key, channelCode, StringComparison.OrdinalIgnoreCase))
            .Value?.Currency;

        return !string.IsNullOrWhiteSpace(channelCurrency)
            ? channelCurrency
            : PaymentOptions.DefaultCurrency;
    }

    private static bool IsOfflineChannel(string channelCode)
        => string.Equals(channelCode, PaymentConstants.OfflineChannelCode, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 订单有效期：调用方显式指定优先；否则线下渠道按天计（等人工核对到账），在线渠道按分钟计。
    /// </summary>
    private DateTime ResolveExpireTime(int? requestedMinutes, string channelCode)
    {
        if (requestedMinutes.HasValue)
            return DateTime.UtcNow.AddMinutes(requestedMinutes.Value);

        return IsOfflineChannel(channelCode)
            ? DateTime.UtcNow.AddDays(PaymentOptions.OfflineExpireDays)
            : DateTime.UtcNow.AddMinutes(PaymentOptions.AutoCloseExpireMinutes);
    }

    private async Task<Result<CouponPreviewDto>> PreviewCouponAsync(
        CreatePaymentDto request, string currency, Guid? userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CouponCode))
            return Result.Success<CouponPreviewDto>(null!);

        if (_couponService == null)
            return Result.Failure<CouponPreviewDto>(ErrorCodes.CouponInvalid, 400);

        // 优惠券按用户维度限量与去重，没有用户上下文就无法保证不被反复使用
        if (!userId.HasValue)
            return Result.Failure<CouponPreviewDto>(ErrorCodes.PaymentCouponRequiresUser, 400);

        return await _couponService.PreviewAsync(
            BuildCouponContext(request, currency, userId.Value, null), cancellationToken);
    }

    private static CouponApplyContext BuildCouponContext(CreatePaymentDto request, string currency, Guid userId, Guid? paymentId) => new()
    {
        CouponCode = request.CouponCode!,
        UserId = userId,
        BusinessOrderNo = request.BusinessOrderNo,
        OrderAmount = request.Amount,
        Currency = currency,
        ProductType = MapProductType(request.BusinessType),
        // 试算与核销必须用同一套上下文，否则限定范围的券会"验得过、核销不掉"
        ScopeId = request.CouponScopeId,
        PaymentId = paymentId
    };

    private static ProductType MapProductType(BusinessType businessType) => businessType switch
    {
        BusinessType.Subscription => ProductType.Subscription,
        BusinessType.Order => ProductType.OneTime,
        BusinessType.Recharge => ProductType.Recharge,
        _ => ProductType.All
    };
}
