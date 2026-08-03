namespace Tnzi.Payment.Services;

/// <summary>
/// 支付服务（partial）：off-session 无人值守扣款与过期支付清扫。
/// </summary>
public partial class PaymentService
{
    public async Task<Result<PaymentDto>> ChargeOffSessionAsync(OffSessionChargeDto request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        if (request.Amount <= 0)
            return Fail<PaymentDto>(ErrorCodes.PaymentInvalidAmount, 400);

        if (string.IsNullOrWhiteSpace(request.PaymentMethodToken))
            return Fail<PaymentDto>(ErrorCodes.SubscriptionPaymentMethodMissing, 400);

        var channelCode = string.IsNullOrWhiteSpace(request.ChannelCode)
            ? PaymentOptions.DefaultChannelCode
            : request.ChannelCode;

        var provider = _paymentProviderFactory.GetProvider(channelCode);
        if (provider == null)
            return Fail<PaymentDto>(ErrorCodes.PaymentChannelNotSupported, 400);

        if (!provider.SupportsOffSessionCharge)
            return Fail<PaymentDto>(ErrorCodes.PaymentOffSessionNotSupported, 400);

        var currency = ResolveCurrency(request.Currency, channelCode);

        // 后台扣款不再走优惠券链路（续费价由订阅侧决定），但仍需计税
        var tax = await _taxCalculator.CalculateAsync(new TaxCalculationRequest
        {
            NetAmount = request.Amount,
            Currency = currency,
            BusinessType = request.BusinessType
        }, cancellationToken);

        if (!tax.Succeeded || tax.Data == null)
            return Fail<PaymentDto>(tax.Message ?? ErrorCodes.PaymentCreationFailed, tax.Code ?? 400);

        var payableAmount = tax.Data.PayableAmount;

        var payment = new PaymentEntity
        {
            TradeNo = GenerateTradeNo(),
            BusinessOrderNo = request.BusinessOrderNo,
            BusinessType = request.BusinessType,
            OriginalAmount = request.Amount,
            PaidAmount = 0,
            DiscountAmount = 0,
            TaxAmount = tax.Data.TaxAmount,
            PayableAmount = payableAmount,
            Currency = currency,
            Status = PaymentStatus.Processing,
            ChannelCode = channelCode,
            PaymentMethod = PaymentMethod.CreditCard,
            Description = request.Description,
            UserId = request.UserId,
            CustomerName = request.CustomerName,
            CustomerEmail = request.CustomerEmail,
            ExtraData = request.ExtraData
        };

        await _paymentRepository.InsertAsync(payment, cancellationToken);

        var chargeResult = await provider.ChargeOffSessionAsync(new PaymentProviderChargeDto
        {
            TradeNo = payment.TradeNo,
            BusinessOrderNo = request.BusinessOrderNo,
            Amount = payableAmount,
            Currency = currency,
            Description = request.Description,
            ProviderCustomerId = request.ProviderCustomerId,
            PaymentMethodToken = request.PaymentMethodToken
        });

        var charged = chargeResult.Data;

        if (chargeResult.Succeeded && charged?.Status == PaymentStatus.Succeeded)
        {
            var paidAmount = charged.PaidAmount > 0 ? charged.PaidAmount : payableAmount;

            // 走与回调同一条推进路径：金额校验、CAS、事件发布三件事只有一份实现
            var applied = await ApplySucceededAsync(
                payment, paidAmount, charged.ExternalTradeNo, JsonSerializer.Serialize(charged), cancellationToken);

            if (!applied.Succeeded)
                return Fail<PaymentDto>(applied.Message ?? ErrorCodes.PaymentAmountMismatch, applied.Code ?? 400);

            Logger.LogInformation("Off-session charge succeeded. TradeNo: {TradeNo}, Amount: {Amount}",
                payment.TradeNo, payment.PaidAmount);

            return Ok(payment.MapTo<PaymentDto>());
        }

        // 失败（含渠道拒付 / requires_action 无法无人值守完成）
        var failReason = charged?.FailReason ?? chargeResult.Message ?? ErrorCodes.PaymentOffSessionChargeFailed;
        payment.ExternalTradeNo = charged?.ExternalTradeNo;
        await ApplyFailedAsync(payment, failReason, JsonSerializer.Serialize(charged), cancellationToken);

        Logger.LogWarning("Off-session charge failed. TradeNo: {TradeNo}, Reason: {Reason}", payment.TradeNo, failReason);
        return Fail<PaymentDto>(failReason);
    }

    public async Task<Result<int>> CloseExpiredPaymentsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var globalExpireTime = now.AddMinutes(-PaymentOptions.AutoCloseExpireMinutes);

        // 分页处理：一次性把全部过期支付读进内存，在积压场景下会直接打爆进程。
        // 与订阅侧的三个后台扫描保持同一形态（有界批次 + 逐条推进）。
        var expiredPayments = await _paymentRepository
            .Where(p => (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Processing)
                && ((p.ExpireTime != null && p.ExpireTime <= now)
                    || (p.ExpireTime == null && p.CreationTime < globalExpireTime)))
            .OrderBy(p => p.CreationTime)
            .Take(ExpiredPaymentScanPageSize)
            .ToListAsync(cancellationToken);

        if (expiredPayments.Count == 0)
            return Ok(0);

        var closed = 0;

        foreach (var payment in expiredPayments)
        {
            try
            {
                // CAS：并发回调可能正在把这笔支付置成功，条件更新保证不会把已付订单标记过期
                var affected = await _paymentRepository.AsQueryable()
                    .Where(p => p.Id == payment.Id
                        && (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Processing))
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PaymentStatus.Expired), cancellationToken);

                if (affected == 0)
                    continue;

                payment.Status = PaymentStatus.Expired;
                closed++;

                // 过期同样要还券
                await ReleaseCouponForPaymentAsync(payment, cancellationToken);

                if (EventBus != null)
                {
                    await EventBus.PublishAsync(new PaymentExpiredEvent
                    {
                        PaymentId = payment.Id,
                        TradeNo = payment.TradeNo,
                        BusinessOrderNo = payment.BusinessOrderNo,
                        BusinessType = payment.BusinessType,
                        ExpiredTime = now,
                        ExtraData = payment.ExtraData
                    });
                }
            }
            catch (Exception ex)
            {
                // 单笔失败不拖累整批
                Logger.LogError(ex, "Failed to expire payment. TradeNo: {TradeNo}", payment.TradeNo);
            }
        }

        if (closed > 0)
            Logger.LogInformation("Closed {Count} expired payments", closed);

        return Ok(closed);
    }
}
