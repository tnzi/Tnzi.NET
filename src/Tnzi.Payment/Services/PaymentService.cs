namespace Tnzi.Payment.Services;

/// <summary>
/// 支付服务实现
/// </summary>
public class PaymentService : ApplicationService, IPaymentService
{
    private readonly IRepository<PaymentEntity, Guid> _paymentRepository;
    private readonly IPaymentProviderFactory _paymentProviderFactory;
    private readonly IOptions<PaymentOptions> _paymentOptions;

    public PaymentService(
        IRepository<PaymentEntity, Guid> paymentRepository,
        IPaymentProviderFactory paymentProviderFactory,
        IOptions<PaymentOptions> paymentOptions,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _paymentRepository = Check.NotNull(paymentRepository);
        _paymentProviderFactory = Check.NotNull(paymentProviderFactory);
        _paymentOptions = Check.NotNull(paymentOptions);
    }

    /// <summary>
    /// 生成交易流水号
    /// </summary>
    private static string GenerateTradeNo()
    {
        return $"{PaymentConstants.TradeNoPrefix}{DateTime.UtcNow:yyyyMMddHHmmss}{Random.Shared.Next(1000, 9999)}";
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

        await _paymentRepository.InsertAsync(payment, cancellationToken);

        // 获取支付渠道
        var provider = _paymentProviderFactory.GetProvider(payment.ChannelCode);
        if (provider == null)
            return Fail<PaymentOrderResultDto>(ErrorCodes.PaymentChannelNotSupported, 400);

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

        if (!result.Succeeded)
            return Fail<PaymentOrderResultDto>(result.Message ?? ErrorCodes.PaymentCreationFailed);

        // 更新支付记录
        payment.Status = PaymentStatus.Processing;
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        Logger.LogInformation("Payment created successfully. TradeNo: {TradeNo}, Channel: {Channel}", tradeNo, payment.ChannelCode);

        return Ok(new PaymentOrderResultDto
        {
            TradeNo = tradeNo,
            PayParams = result.Data?.PayParams,
            PayUrl = result.Data?.PayUrl,
            ExpireTime = payment.ExpireTime,
            Amount = request.Amount,
            Currency = payment.Currency
        });
    }

    public async Task<Result<PaymentDto>> GetPaymentAsync(string tradeNo, CancellationToken cancellationToken = default)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(p => p.TradeNo == tradeNo, cancellationToken);
        if (payment == null)
            return Fail<PaymentDto>(ErrorCodes.PaymentNotFound, 404);

        return Ok(payment.MapTo<PaymentDto>());
    }

    public async Task<Result<IPagedList<PaymentDto>>> GetPaymentListAsync(PaymentQueryDto query, CancellationToken cancellationToken = default)
    {
        var pagedList = await _paymentRepository.AsNoTracking()
            .Filter(query)
            .ProjectTo<PaymentEntity, PaymentDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        return Ok(pagedList);
    }

    public async Task<Result> ClosePaymentAsync(string tradeNo, string? reason, CancellationToken cancellationToken = default)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(p => p.TradeNo == tradeNo, cancellationToken);
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
        if (!provider.VerifySignature(request.Parameters))
            return Fail(ErrorCodes.PaymentInvalidSignature, 400);

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

        if (result.Data?.Status == PaymentStatus.Succeeded)
        {
            payment.Status = PaymentStatus.Succeeded;
            payment.PaidTime = DateTime.UtcNow;
            payment.PaidAmount = result.Data.PaidAmount;
            payment.ExternalTradeNo = result.Data.ExternalTradeNo;
            payment.ChannelResponse = JsonSerializer.Serialize(result.Data);

            await _paymentRepository.UpdateAsync(payment, cancellationToken);

            // 发布支付完成事件
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
                    PaidTime = payment.PaidTime.Value,
                    ExternalTradeNo = payment.ExternalTradeNo
                });
            }

            Logger.LogInformation("Payment completed. TradeNo: {TradeNo}, Amount: {Amount}",
                payment.TradeNo, payment.PaidAmount);
        }
        else if (result.Data?.Status == PaymentStatus.Failed)
        {
            payment.Status = PaymentStatus.Failed;
            payment.ChannelResponse = JsonSerializer.Serialize(result.Data);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);

            // 发布支付失败事件
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

        return Ok();
    }

    public async Task<Result<PaymentParamsDto>> GetPaymentParamsAsync(string tradeNo, CancellationToken cancellationToken = default)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(p => p.TradeNo == tradeNo, cancellationToken);
        if (payment == null)
            return Fail<PaymentParamsDto>(ErrorCodes.PaymentNotFound, 404);

        var provider = _paymentProviderFactory.GetProvider(payment.ChannelCode);
        if (provider == null)
            return Fail<PaymentParamsDto>(ErrorCodes.PaymentChannelNotSupported, 400);

        var result = await provider.GetPaymentParamsAsync(tradeNo);
        return Ok(result.Data ?? new PaymentParamsDto { TradeNo = tradeNo });
    }

    public async Task<Result> SyncOrderAsync(string tradeNo, CancellationToken cancellationToken = default)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(p => p.TradeNo == tradeNo, cancellationToken);
        if (payment == null)
            return Fail(ErrorCodes.PaymentNotFound, 404);

        var provider = _paymentProviderFactory.GetProvider(payment.ChannelCode);
        if (provider == null)
            return Fail(ErrorCodes.PaymentChannelNotSupported, 400);

        var result = await provider.SyncOrderAsync(tradeNo);
        if (!result.Succeeded)
            return Fail(result.Message ?? ErrorCodes.PaymentCreationFailed);

        // 更新本地状态
        payment.Status = result.Data?.Status ?? payment.Status;
        if (!string.IsNullOrEmpty(result.Data?.ExternalTradeNo))
            payment.ExternalTradeNo = result.Data.ExternalTradeNo;

        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        return Ok();
    }
}
