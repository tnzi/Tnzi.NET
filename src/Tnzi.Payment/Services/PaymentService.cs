namespace Tnzi.Payment.Services;

/// <summary>
/// 支付服务实现
/// </summary>
public class PaymentService : ApplicationService, IPaymentService
{
    private readonly IRepository<PaymentEntity, Guid> _paymentRepository;
    private readonly IPaymentProviderFactory _paymentProviderFactory;
    private readonly IOptions<PaymentOptions> _paymentOptions;
    private readonly ICache? _cache;

    public PaymentService(
        IRepository<PaymentEntity, Guid> paymentRepository,
        IPaymentProviderFactory paymentProviderFactory,
        IOptions<PaymentOptions> paymentOptions,
        IServiceProvider serviceProvider,
        ICache? cache = null)
        : base(serviceProvider)
    {
        _paymentRepository = Check.NotNull(paymentRepository);
        _paymentProviderFactory = Check.NotNull(paymentProviderFactory);
        _paymentOptions = Check.NotNull(paymentOptions);
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
            Currency = request.Currency ?? _paymentOptions.Value.DefaultCurrency,
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
            request.ReturnUrl = _paymentOptions.Value.DefaultNotifyUrl;

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

        // 幂等性：已完成的支付不再处理
        if (payment.Status == PaymentStatus.Succeeded || payment.Status == PaymentStatus.Failed)
            return Ok();

        // 事务保护：状态更新原子操作，防止并发回调重复处理
        await ExecuteInUnitOfWorkAsync(async ct =>
        {
            if (result.Data?.Status == PaymentStatus.Succeeded)
            {
                payment.Status = PaymentStatus.Succeeded;
                payment.PaidTime = DateTime.UtcNow;
                payment.PaidAmount = result.Data.PaidAmount;
                payment.ExternalTradeNo = result.Data.ExternalTradeNo;
                payment.ChannelResponse = JsonSerializer.Serialize(result.Data);

                await _paymentRepository.UpdateAsync(payment, ct);
            }
            else if (result.Data?.Status == PaymentStatus.Failed)
            {
                payment.Status = PaymentStatus.Failed;
                payment.ChannelResponse = JsonSerializer.Serialize(result.Data);
                await _paymentRepository.UpdateAsync(payment, ct);
            }

            return Ok<object?>(null);
        }, cancellationToken);

        // 事件发布在事务外，避免事务回滚后事件已发出
        if (result.Data?.Status == PaymentStatus.Succeeded)
        {
            if (EventBus != null)
            {
                await EventBus.PublishAsync(new PaymentCompletedEvent
                {
                    PaymentId = payment.Id,
                    TradeNo = payment.TradeNo,
                    BusinessOrderNo = payment.BusinessOrderNo,
                    Amount = payment.PaidAmount,
                    Currency = payment.Currency,
                    ChannelCode = payment.ChannelCode,
                    PaidTime = payment.PaidTime!.Value,
                    ExternalTradeNo = payment.ExternalTradeNo
                });
            }

            Logger.LogInformation("Payment completed. TradeNo: {TradeNo}, Amount: {Amount}",
                payment.TradeNo, payment.PaidAmount);
        }
        else if (result.Data?.Status == PaymentStatus.Failed)
        {
            if (EventBus != null)
            {
                await EventBus.PublishAsync(new PaymentFailedEvent
                {
                    PaymentId = payment.Id,
                    TradeNo = payment.TradeNo,
                    BusinessOrderNo = payment.BusinessOrderNo,
                    FailReason = result.Data?.FailReason ?? "Unknown"
                });
            }

            Logger.LogWarning("Payment failed. TradeNo: {TradeNo}, Reason: {Reason}",
                payment.TradeNo, result.Data?.FailReason);
        }

        // 标记回调已处理，24小时内不再重复处理
        if (_cache != null && !string.IsNullOrEmpty(eventId))
        {
            await _cache.SetAsync($"payment:callback:{eventId}", true, TimeSpan.FromHours(24), cancellationToken);
        }

        return Ok();
    }

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

        // 更新本地状态
        payment.Status = result.Data?.Status ?? payment.Status;
        if (!string.IsNullOrEmpty(result.Data?.ExternalTradeNo))
            payment.ExternalTradeNo = result.Data.ExternalTradeNo;

        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        return Ok();
    }

    public async Task<Result<int>> CloseExpiredPaymentsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var globalExpireTime = now.AddMinutes(-_paymentOptions.Value.AutoCloseExpireMinutes);

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
                    ExpiredTime = DateTime.UtcNow
                });
            }
        }

        Logger.LogInformation("Closed {Count} expired payments", expiredPayments.Count);
        return Ok(expiredPayments.Count);
    }
}
