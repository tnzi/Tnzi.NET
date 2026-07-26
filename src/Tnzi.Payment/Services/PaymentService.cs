namespace Tnzi.Payment.Services;

/// <summary>
/// 支付服务实现
/// </summary>
public class PaymentService : ApplicationService, IPaymentService
{
    private readonly IRepository<PaymentEntity, Guid> _paymentRepository;
    private readonly IPaymentProviderFactory _paymentProviderFactory;
    private readonly IOptionsMonitor<PaymentOptions> _paymentOptionsMonitor;
    private readonly ICache? _cache;

    private PaymentOptions PaymentOptions => _paymentOptionsMonitor.CurrentValue;

    public PaymentService(
        IRepository<PaymentEntity, Guid> paymentRepository,
        IPaymentProviderFactory paymentProviderFactory,
        IOptionsMonitor<PaymentOptions> paymentOptionsMonitor,
        IServiceProvider serviceProvider,
        ICache? cache = null)
        : base(serviceProvider)
    {
        _paymentRepository = Check.NotNull(paymentRepository);
        _paymentProviderFactory = Check.NotNull(paymentProviderFactory);
        _paymentOptionsMonitor = Check.NotNull(paymentOptionsMonitor);
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
        if (request.Amount <= 0)
            return Fail<PaymentOrderResultDto>(ErrorCodes.PaymentInvalidAmount, 400);

        var tradeNo = GenerateTradeNo();

        var payment = new PaymentEntity
        {
            TradeNo = tradeNo,
            BusinessOrderNo = request.BusinessOrderNo,
            BusinessType = request.BusinessType,
            OriginalAmount = request.Amount,
            PaidAmount = 0,
            DiscountAmount = 0,
            Currency = request.Currency ?? PaymentOptions.DefaultCurrency,
            Status = PaymentStatus.Pending,
            ChannelCode = request.ChannelCode ?? PaymentConstants.DefaultPaymentChannel,
            PaymentMethod = request.PaymentMethod ?? PaymentMethod.CreditCard,
            Description = request.Description,
            ExpireTime = request.ExpireMinutes.HasValue ? DateTime.UtcNow.AddMinutes(request.ExpireMinutes.Value) : null,
            ExtraData = request.ExtraData
        };

        // 获取支付渠道
        var provider = _paymentProviderFactory.GetProvider(payment.ChannelCode);
        if (provider == null)
            return Fail<PaymentOrderResultDto>(ErrorCodes.PaymentChannelNotSupported, 400);

        if (!provider.IsSupported(payment.PaymentMethod))
            return Fail<PaymentOrderResultDto>(ErrorCodes.PaymentChannelNotSupported, 400);

        await _paymentRepository.InsertAsync(payment, cancellationToken);

        // 使用 DefaultNotifyUrl 作为回调地址的 fallback
        if (string.IsNullOrEmpty(request.ReturnUrl))
            request.ReturnUrl = PaymentOptions.DefaultNotifyUrl;

        // 创建渠道支付订单
        var input = new PaymentProviderCreateDto
        {
            TradeNo = tradeNo,
            BusinessOrderNo = request.BusinessOrderNo,
            Amount = request.Amount,
            Currency = payment.Currency,
            Description = request.Description,
            ExpireTime = payment.ExpireTime,
            ReturnUrl = request.ReturnUrl,
            ExtraData = request.ExtraData
        };

        var result = await provider.CreatePaymentAsync(input);

        if (!result.Succeeded || result.Data == null)
        {
            payment.Status = PaymentStatus.Failed;
            payment.ChannelResponse = result.Message;
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            return Fail<PaymentOrderResultDto>(result.Message ?? ErrorCodes.PaymentCreationFailed);
        }

        // 更新支付记录
        payment.Status = PaymentStatus.Processing;
        payment.ExternalTradeNo = result.Data.ExternalTradeNo;
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        Logger.LogInformation("Payment created successfully. TradeNo: {TradeNo}, Channel: {Channel}", tradeNo, payment.ChannelCode);

        return Ok(new PaymentOrderResultDto
        {
            TradeNo = tradeNo,
            PayParams = result.Data.PayParams,
            PayUrl = result.Data.PayUrl,
            ExpireTime = payment.ExpireTime,
            Amount = request.Amount,
            Currency = payment.Currency
        });
    }

    public async Task<Result<PaymentDto>> GetPaymentAsync(string tradeNo, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(
            p => p.TradeNo == tradeNo && (!ownerUserId.HasValue || p.CreatorId == ownerUserId),
            cancellationToken);
        if (payment == null)
            return Fail<PaymentDto>(ErrorCodes.PaymentNotFound, 404);

        return Ok(payment.MapTo<PaymentDto>());
    }

    public async Task<Result<IPagedList<PaymentDto>>> GetPaymentListAsync(PaymentQueryDto query, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var queryable = _paymentRepository.AsNoTracking();
        if (ownerUserId.HasValue)
            queryable = queryable.Where(p => p.CreatorId == ownerUserId.Value);

        var pagedList = await queryable
            .Filter(query)
            .ProjectTo<PaymentEntity, PaymentDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        return Ok(pagedList);
    }

    public async Task<Result> ClosePaymentAsync(string tradeNo, string? reason, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(
            p => p.TradeNo == tradeNo && (!ownerUserId.HasValue || p.CreatorId == ownerUserId),
            cancellationToken);
        if (payment == null)
            return Fail(ErrorCodes.PaymentNotFound, 404);

        if (payment.Status != PaymentStatus.Pending && payment.Status != PaymentStatus.Processing)
            return Fail(ErrorCodes.PaymentCannotClose, 400);

        payment.Status = PaymentStatus.Closed;
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        Logger.LogInformation("Payment closed. TradeNo: {TradeNo}, Reason: {Reason}", tradeNo, reason);

        return Ok();
    }

    public async Task<Result> HandleCallbackAsync(PaymentCallbackDto request, CancellationToken cancellationToken = default)
    {
        var provider = _paymentProviderFactory.GetProvider(request.ChannelCode);
        if (provider == null)
            return Fail(ErrorCodes.PaymentChannelNotSupported, 400);

        // 验证签名
        if (!await provider.VerifySignatureAsync(request.Parameters))
            return Fail(ErrorCodes.PaymentInvalidSignature, 400);

        // 提取事件ID用于回放防护
        string? eventId = null;
        if (request.Parameters.TryGetValue("__stripe_signature", out var sig))
            eventId = sig;
        else if (request.Parameters.TryGetValue("__paypal_transmission_id", out var txId))
            eventId = txId;

        // 检查是否为重复回调
        if (_cache != null && !string.IsNullOrEmpty(eventId))
        {
            var cacheKey = $"payment:callback:{eventId}";
            var processed = await _cache.GetAsync<bool>(cacheKey, cancellationToken);
            if (processed)
            {
                Logger.LogInformation("Duplicate callback detected. EventId: {EventId}", eventId);
                return Ok();
            }
        }

        var result = await provider.HandleCallbackAsync(request.Parameters);
        if (!result.Succeeded)
            return Fail(result.Message ?? ErrorCodes.PaymentCreationFailed);

        var callbackTradeNo = result.Data?.TradeNo;
        if (string.IsNullOrWhiteSpace(callbackTradeNo))
            return Fail(ErrorCodes.PaymentNotFound, 404);

        // 获取支付记录
        var payment = await _paymentRepository.FirstOrDefaultAsync(
            p => p.TradeNo == callbackTradeNo, cancellationToken);

        if (payment == null)
            return Fail(ErrorCodes.PaymentNotFound, 404);

        // 幂等性 + 防回退：任何终态支付都不再被回调改写
        if (IsTerminalStatus(payment.Status))
            return Ok();

        var newStatus = result.Data?.Status;

        if (newStatus == PaymentStatus.Succeeded)
        {
            // 金额一致性校验：到账金额需覆盖订单应付金额（防止少付/篡改）
            var expectedAmount = payment.OriginalAmount - payment.DiscountAmount;
            if (result.Data!.PaidAmount + 0.01m < expectedAmount)
            {
                Logger.LogWarning("Payment callback amount mismatch. TradeNo: {TradeNo}, Expected: {Expected}, Paid: {Paid}",
                    payment.TradeNo, expectedAmount, result.Data.PaidAmount);
                return Fail(ErrorCodes.PaymentAmountMismatch, 400);
            }

            var paidTime = DateTime.UtcNow;
            var channelResponse = JsonSerializer.Serialize(result.Data);

            // 原子状态条件更新（CAS）：仅当仍为 Pending/Processing 才置成功，收敛并发回调
            var affected = await _paymentRepository.AsQueryable()
                .Where(p => p.Id == payment.Id
                    && (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Processing))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Status, PaymentStatus.Succeeded)
                    .SetProperty(p => p.PaidTime, paidTime)
                    .SetProperty(p => p.PaidAmount, result.Data.PaidAmount)
                    .SetProperty(p => p.ExternalTradeNo, result.Data.ExternalTradeNo)
                    .SetProperty(p => p.ChannelResponse, channelResponse), cancellationToken);

            if (affected == 0)
            {
                // 并发回调已抢先处理，直接幂等返回
                await MarkCallbackProcessedAsync(eventId, cancellationToken);
                return Ok();
            }

            // 同步内存值供事件使用
            payment.Status = PaymentStatus.Succeeded;
            payment.PaidTime = paidTime;
            payment.PaidAmount = result.Data.PaidAmount;
            payment.ExternalTradeNo = result.Data.ExternalTradeNo;

            if (EventBus != null)
                await EventBus.PublishAsync(BuildCompletedEvent(payment));

            Logger.LogInformation("Payment completed. TradeNo: {TradeNo}, Amount: {Amount}",
                payment.TradeNo, payment.PaidAmount);
        }
        else if (newStatus == PaymentStatus.Failed)
        {
            var channelResponse = JsonSerializer.Serialize(result.Data);

            var affected = await _paymentRepository.AsQueryable()
                .Where(p => p.Id == payment.Id
                    && (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Processing))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Status, PaymentStatus.Failed)
                    .SetProperty(p => p.ChannelResponse, channelResponse), cancellationToken);

            if (affected == 0)
            {
                await MarkCallbackProcessedAsync(eventId, cancellationToken);
                return Ok();
            }

            payment.Status = PaymentStatus.Failed;

            if (EventBus != null)
                await EventBus.PublishAsync(BuildFailedEvent(payment, result.Data?.FailReason ?? "Unknown"));

            Logger.LogWarning("Payment failed. TradeNo: {TradeNo}, Reason: {Reason}",
                payment.TradeNo, result.Data?.FailReason);
        }

        await MarkCallbackProcessedAsync(eventId, cancellationToken);
        return Ok();
    }

    /// <summary>
    /// 终态判定：处于这些状态的支付不应再被回调/同步改写，防止状态回退
    /// </summary>
    private static bool IsTerminalStatus(PaymentStatus status)
        => status is PaymentStatus.Succeeded or PaymentStatus.Failed or PaymentStatus.Closed
            or PaymentStatus.Cancelled or PaymentStatus.Expired
            or PaymentStatus.Refunded or PaymentStatus.PartialRefunded;

    private async Task MarkCallbackProcessedAsync(string? eventId, CancellationToken cancellationToken)
    {
        if (_cache != null && !string.IsNullOrEmpty(eventId))
            await _cache.SetAsync($"payment:callback:{eventId}", true, TimeSpan.FromHours(24), cancellationToken);
    }

    private static PaymentCompletedEvent BuildCompletedEvent(PaymentEntity payment) => new()
    {
        PaymentId = payment.Id,
        TradeNo = payment.TradeNo,
        BusinessOrderNo = payment.BusinessOrderNo,
        BusinessType = payment.BusinessType,
        Amount = payment.PaidAmount,
        Currency = payment.Currency,
        ChannelCode = payment.ChannelCode,
        PaidTime = payment.PaidTime ?? DateTime.UtcNow,
        ExternalTradeNo = payment.ExternalTradeNo,
        ExtraData = payment.ExtraData
    };

    private static PaymentFailedEvent BuildFailedEvent(PaymentEntity payment, string failReason) => new()
    {
        PaymentId = payment.Id,
        TradeNo = payment.TradeNo,
        BusinessOrderNo = payment.BusinessOrderNo,
        BusinessType = payment.BusinessType,
        FailReason = failReason,
        ExtraData = payment.ExtraData
    };

    public async Task<Result<PaymentParamsDto>> GetPaymentParamsAsync(string tradeNo, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(
            p => p.TradeNo == tradeNo && (!ownerUserId.HasValue || p.CreatorId == ownerUserId),
            cancellationToken);
        if (payment == null)
            return Fail<PaymentParamsDto>(ErrorCodes.PaymentNotFound, 404);

        var provider = _paymentProviderFactory.GetProvider(payment.ChannelCode);
        if (provider == null)
            return Fail<PaymentParamsDto>(ErrorCodes.PaymentChannelNotSupported, 400);

        var result = await provider.GetPaymentParamsAsync(payment.ExternalTradeNo ?? tradeNo);
        if (result.Data != null)
            result.Data.TradeNo = tradeNo;

        return Ok(result.Data ?? new PaymentParamsDto { TradeNo = tradeNo });
    }

    public async Task<Result> SyncOrderAsync(string tradeNo, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(
            p => p.TradeNo == tradeNo && (!ownerUserId.HasValue || p.CreatorId == ownerUserId),
            cancellationToken);
        if (payment == null)
            return Fail(ErrorCodes.PaymentNotFound, 404);

        var provider = _paymentProviderFactory.GetProvider(payment.ChannelCode);
        if (provider == null)
            return Fail(ErrorCodes.PaymentChannelNotSupported, 400);

        var result = await provider.SyncOrderAsync(payment.ExternalTradeNo ?? tradeNo);
        if (!result.Succeeded)
            return Fail(result.Message ?? ErrorCodes.PaymentCreationFailed);

        // 更新本地状态。终态防回退（见 IsTerminalStatus）：已成功/已退款/已关闭的支付
        // 不再被渠道同步改写状态，否则 Refunded 会被同步回 Succeeded 从而放开二次退款。
        if (!IsTerminalStatus(payment.Status))
            payment.Status = result.Data?.Status ?? payment.Status;

        if (!string.IsNullOrEmpty(result.Data?.ExternalTradeNo))
            payment.ExternalTradeNo = result.Data.ExternalTradeNo;

        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        return Ok();
    }

    public async Task<Result<PaymentDto>> ChargeOffSessionAsync(OffSessionChargeDto request, CancellationToken cancellationToken = default)
    {
        if (request.Amount <= 0)
            return Fail<PaymentDto>(ErrorCodes.PaymentInvalidAmount, 400);

        if (string.IsNullOrWhiteSpace(request.PaymentMethodToken))
            return Fail<PaymentDto>(ErrorCodes.SubscriptionPaymentMethodMissing, 400);

        var channelCode = request.ChannelCode ?? PaymentConstants.DefaultPaymentChannel;
        var provider = _paymentProviderFactory.GetProvider(channelCode);
        if (provider == null)
            return Fail<PaymentDto>(ErrorCodes.PaymentChannelNotSupported, 400);

        if (!provider.SupportsOffSessionCharge)
            return Fail<PaymentDto>(ErrorCodes.PaymentOffSessionNotSupported, 400);

        var tradeNo = GenerateTradeNo();
        var currency = request.Currency ?? PaymentOptions.DefaultCurrency;

        var payment = new PaymentEntity
        {
            TradeNo = tradeNo,
            BusinessOrderNo = request.BusinessOrderNo,
            BusinessType = request.BusinessType,
            OriginalAmount = request.Amount,
            PaidAmount = 0,
            DiscountAmount = 0,
            Currency = currency,
            Status = PaymentStatus.Processing,
            ChannelCode = channelCode,
            PaymentMethod = PaymentMethod.CreditCard,
            Description = request.Description,
            ExtraData = request.ExtraData
        };

        await _paymentRepository.InsertAsync(payment, cancellationToken);

        var chargeResult = await provider.ChargeOffSessionAsync(new PaymentProviderChargeDto
        {
            TradeNo = tradeNo,
            BusinessOrderNo = request.BusinessOrderNo,
            Amount = request.Amount,
            Currency = currency,
            Description = request.Description,
            ProviderCustomerId = request.ProviderCustomerId,
            PaymentMethodToken = request.PaymentMethodToken
        });

        // 金额一致性校验：渠道回报的实际扣款额需覆盖请求额（防止短款捕获被记为全额成功）
        if (chargeResult.Succeeded
            && chargeResult.Data?.Status == PaymentStatus.Succeeded
            && chargeResult.Data.PaidAmount > 0
            && chargeResult.Data.PaidAmount + 0.01m < request.Amount)
        {
            Logger.LogWarning("Off-session charge captured less than requested. TradeNo: {TradeNo}, Requested: {Requested}, Captured: {Captured}",
                tradeNo, request.Amount, chargeResult.Data.PaidAmount);
            payment.Status = PaymentStatus.Failed;
            payment.ChannelResponse = ErrorCodes.PaymentAmountMismatch;
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            if (EventBus != null)
                await EventBus.PublishAsync(BuildFailedEvent(payment, ErrorCodes.PaymentAmountMismatch));
            return Fail<PaymentDto>(ErrorCodes.PaymentAmountMismatch, 400);
        }

        if (chargeResult.Succeeded && chargeResult.Data?.Status == PaymentStatus.Succeeded)
        {
            payment.Status = PaymentStatus.Succeeded;
            payment.PaidTime = DateTime.UtcNow;
            payment.PaidAmount = chargeResult.Data.PaidAmount > 0 ? chargeResult.Data.PaidAmount : request.Amount;
            payment.ExternalTradeNo = chargeResult.Data.ExternalTradeNo;
            await _paymentRepository.UpdateAsync(payment, cancellationToken);

            if (EventBus != null)
                await EventBus.PublishAsync(BuildCompletedEvent(payment));

            Logger.LogInformation("Off-session charge succeeded. TradeNo: {TradeNo}, Amount: {Amount}", tradeNo, payment.PaidAmount);
            return Ok(payment.MapTo<PaymentDto>());
        }

        // 失败（含渠道拒付 / requires_action 无法无人值守完成）
        var failReason = chargeResult.Data?.FailReason ?? chargeResult.Message ?? ErrorCodes.PaymentOffSessionChargeFailed;
        payment.Status = PaymentStatus.Failed;
        payment.ChannelResponse = failReason;
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        if (EventBus != null)
            await EventBus.PublishAsync(BuildFailedEvent(payment, failReason));

        Logger.LogWarning("Off-session charge failed. TradeNo: {TradeNo}, Reason: {Reason}", tradeNo, failReason);
        return Fail<PaymentDto>(failReason);
    }

    public async Task<Result<int>> CloseExpiredPaymentsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var globalExpireTime = now.AddMinutes(-PaymentOptions.AutoCloseExpireMinutes);

        // 优先使用订单级 ExpireTime，没设置时按全局配置 + CreationTime 兜底
        var expiredPayments = await _paymentRepository
            .Where(p => (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Processing)
                && ((p.ExpireTime != null && p.ExpireTime <= now)
                    || (p.ExpireTime == null && p.CreationTime < globalExpireTime)))
            .ToListAsync(cancellationToken);

        if (expiredPayments.Count == 0)
            return Ok(0);

        foreach (var payment in expiredPayments)
        {
            payment.Status = PaymentStatus.Expired;
        }

        await _paymentRepository.UpdateManyAsync(expiredPayments, cancellationToken);

        if (EventBus != null)
        {
            foreach (var payment in expiredPayments)
            {
                await EventBus.PublishAsync(new PaymentExpiredEvent
                {
                    PaymentId = payment.Id,
                    TradeNo = payment.TradeNo,
                    BusinessOrderNo = payment.BusinessOrderNo,
                    BusinessType = payment.BusinessType,
                    ExpiredTime = DateTime.UtcNow,
                    ExtraData = payment.ExtraData
                });
            }
        }

        Logger.LogInformation("Closed {Count} expired payments", expiredPayments.Count);
        return Ok(expiredPayments.Count);
    }
}
