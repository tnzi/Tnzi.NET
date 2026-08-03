namespace Tnzi.Payment.Services;

/// <summary>
/// 支付服务（partial）：渠道回调处理与支付状态推进。
/// </summary>
/// <remarks>
/// 回调链路的三条不变量：
/// <list type="number">
/// <item>验签不过一律拒绝，绝不试图从报文里"猜"出订单；</item>
/// <item>状态推进走条件更新（CAS），并发回调与同步只有一个能生效；</item>
/// <item>失败要区分"确定性拒绝"（渠道不必重投）与"暂时性故障"（必须让渠道重投），
/// 后者通过抛出异常映射成 5xx，而不是回 200 把事件吞掉。</item>
/// </list>
/// </remarks>
public partial class PaymentService
{
    public async Task<Result> HandleCallbackAsync(PaymentCallbackDto request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);
        Check.NotNull(request.Parameters);

        var provider = _paymentProviderFactory.GetProvider(request.ChannelCode);
        if (provider == null)
            return Fail(ErrorCodes.PaymentChannelNotSupported, 400);

        if (!await provider.VerifySignatureAsync(request.Parameters))
            return Fail(ErrorCodes.PaymentInvalidSignature, 400);

        var result = await provider.HandleCallbackAsync(request.Parameters);
        if (!result.Succeeded || result.Data == null)
            return Fail(result.Message ?? ErrorCodes.PaymentInvalidSignature, result.Code ?? 400);

        var callback = result.Data;

        // 渠道推送的无关事件：已接收、无需处理，正常回 200 结束
        if (!callback.IsHandled)
        {
            Logger.LogDebug("Callback event {EventId} from {Channel} is not payment-related; ignored.",
                callback.EventId, request.ChannelCode);
            return Ok();
        }

        // 去重键用渠道事件ID。签名头每次投递都会重新生成，拿它做键永远命中不了重投。
        var eventId = callback.EventId;
        if (await IsCallbackProcessedAsync(eventId, cancellationToken))
        {
            Logger.LogInformation("Duplicate callback detected. EventId: {EventId}", eventId);
            return Ok();
        }

        // 支付方式在渠道侧被撤销：不涉及任何一笔支付，本地跟着失效即可。
        // 放在去重之后、订单号校验之前——这类事件天然没有 TradeNo。
        if (callback.Kind == PaymentCallbackKind.PaymentMethodRevoked)
        {
            var revoked = await _paymentMethodService.DeactivateByTokenAsync(
                request.ChannelCode, callback.PaymentMethodToken ?? string.Empty, cancellationToken);

            if (!revoked.Succeeded)
                return revoked;

            await MarkCallbackProcessedAsync(eventId, cancellationToken);
            return Ok();
        }

        if (string.IsNullOrWhiteSpace(callback.TradeNo))
            return Fail(ErrorCodes.PaymentNotFound, 404);

        var payment = await _paymentRepository.FirstOrDefaultAsync(
            p => p.TradeNo == callback.TradeNo, cancellationToken);

        if (payment == null)
        {
            // 支付记录在调用渠道之前就已落库，因此这里找不到只可能是外部/非本系统的单，重投也不会变好
            Logger.LogWarning("Callback references unknown trade no {TradeNo} from {Channel}.",
                callback.TradeNo, request.ChannelCode);
            return Fail(ErrorCodes.PaymentNotFound, 404);
        }

        // 幂等 + 防回退：任何终态支付都不再被回调改写
        if (IsTerminalStatus(payment.Status))
        {
            await MarkCallbackProcessedAsync(eventId, cancellationToken);
            return Ok();
        }

        var channelResponse = JsonSerializer.Serialize(callback);

        Result applied;
        switch (callback.Status)
        {
            case PaymentStatus.Succeeded:
                applied = await ApplySucceededAsync(payment, callback.PaidAmount, callback.ExternalTradeNo, channelResponse, cancellationToken);
                break;

            case PaymentStatus.Failed:
                applied = await ApplyFailedAsync(payment, callback.FailReason ?? "Unknown", channelResponse, cancellationToken);
                break;

            default:
                // 中间态（如 processing）不改写本地状态，等待终态事件
                applied = Ok();
                break;
        }

        if (!applied.Succeeded)
            return applied;

        await MarkCallbackProcessedAsync(eventId, cancellationToken);
        return Ok();
    }

    /// <summary>
    /// 把一笔支付推进为成功：金额校验 → CAS 抢占 → 发布完成事件。
    /// 回调、渠道同步、线下人工确认三条入口共用，确保任何一条路径都会触发下游（订阅状态机、开票）。
    /// </summary>
    private async Task<Result> ApplySucceededAsync(
        PaymentEntity payment,
        decimal paidAmount,
        string? externalTradeNo,
        string? channelResponse,
        CancellationToken cancellationToken,
        DateTime? paidTime = null)
    {
        // 金额一致性校验：到账金额需覆盖应付金额（防止少付/篡改）。
        // 容差一个最小货币单位，避免渠道侧取整造成的误判。
        var tolerance = CurrencyInfo.FromMinorUnits(1, payment.Currency);
        if (paidAmount + tolerance < payment.PayableAmount)
        {
            Logger.LogWarning("Payment amount mismatch. TradeNo: {TradeNo}, Expected: {Expected}, Paid: {Paid}",
                payment.TradeNo, payment.PayableAmount, paidAmount);
            return Fail(ErrorCodes.PaymentAmountMismatch, 400);
        }

        var completedTime = paidTime ?? DateTime.UtcNow;

        var affected = await _paymentRepository.AsQueryable()
            .Where(p => p.Id == payment.Id
                && (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Processing))
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, PaymentStatus.Succeeded)
                .SetProperty(p => p.PaidTime, completedTime)
                .SetProperty(p => p.PaidAmount, paidAmount)
                .SetProperty(p => p.ExternalTradeNo, externalTradeNo ?? payment.ExternalTradeNo)
                .SetProperty(p => p.ChannelResponse, channelResponse), cancellationToken);

        if (affected == 0)
        {
            // 并发路径已抢先处理，幂等返回
            return Ok();
        }

        // 同步内存值供事件使用
        payment.Status = PaymentStatus.Succeeded;
        payment.PaidTime = completedTime;
        payment.PaidAmount = paidAmount;
        payment.ExternalTradeNo = externalTradeNo ?? payment.ExternalTradeNo;

        if (EventBus != null)
            await EventBus.PublishAsync(BuildCompletedEvent(payment));

        Logger.LogInformation("Payment completed. TradeNo: {TradeNo}, Amount: {Amount} {Currency}",
            payment.TradeNo, payment.PaidAmount, payment.Currency);

        return Ok();
    }

    /// <summary>
    /// 把一笔支付推进为失败：CAS 抢占 → 释放已核销的优惠券 → 发布失败事件
    /// </summary>
    private async Task<Result> ApplyFailedAsync(
        PaymentEntity payment,
        string failReason,
        string? channelResponse,
        CancellationToken cancellationToken)
    {
        var affected = await _paymentRepository.AsQueryable()
            .Where(p => p.Id == payment.Id
                && (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Processing))
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, PaymentStatus.Failed)
                .SetProperty(p => p.ChannelResponse, channelResponse), cancellationToken);

        if (affected == 0)
            return Ok();

        payment.Status = PaymentStatus.Failed;

        await ReleaseCouponForPaymentAsync(payment, cancellationToken);

        if (EventBus != null)
            await EventBus.PublishAsync(BuildFailedEvent(payment, failReason));

        Logger.LogWarning("Payment failed. TradeNo: {TradeNo}, Reason: {Reason}", payment.TradeNo, failReason);

        return Ok();
    }

    /// <summary>
    /// 支付确定失败/过期后归还其占用的优惠券。
    /// 券在建单时就核销掉了，不还就等于用户付款失败还赔一张券。
    /// </summary>
    private async Task ReleaseCouponForPaymentAsync(PaymentEntity payment, CancellationToken cancellationToken)
    {
        if (_couponService == null || payment.CouponId == null)
            return;

        var usage = await _couponUsageRepository
            .FirstOrDefaultAsync(c => c.PaymentId == payment.Id, cancellationToken);

        if (usage != null)
            await _couponService.ReleaseCouponAsync(usage.Id, cancellationToken);
    }

    /// <summary>
    /// 终态判定：处于这些状态的支付不应再被回调/同步改写，防止状态回退
    /// </summary>
    private static bool IsTerminalStatus(PaymentStatus status)
        => status is PaymentStatus.Succeeded or PaymentStatus.Failed or PaymentStatus.Closed
            or PaymentStatus.Cancelled or PaymentStatus.Expired
            or PaymentStatus.Refunded or PaymentStatus.PartialRefunded;

    private async Task<bool> IsCallbackProcessedAsync(string? eventId, CancellationToken cancellationToken)
    {
        if (_cache == null || string.IsNullOrEmpty(eventId))
            return false;

        return await _cache.GetAsync<bool>(BuildCallbackCacheKey(eventId), cancellationToken);
    }

    private async Task MarkCallbackProcessedAsync(string? eventId, CancellationToken cancellationToken)
    {
        if (_cache != null && !string.IsNullOrEmpty(eventId))
            await _cache.SetAsync(BuildCallbackCacheKey(eventId), true, TimeSpan.FromHours(24), cancellationToken);
    }

    private static string BuildCallbackCacheKey(string eventId)
        => $"{PaymentConstants.PaymentCacheKeyPrefix}callback:{eventId}";

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
}
