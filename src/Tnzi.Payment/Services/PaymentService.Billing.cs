namespace Tnzi.Payment.Services;

/// <summary>
/// 支付服务（partial）：off-session 无人值守扣款与过期支付清扫。
/// </summary>
public partial class PaymentService
{
    public async Task<Result<PaymentDto>> ChargeOffSessionAsync(OffSessionChargeDto request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        // ★ 建单之前的每一种拒绝都带 ErrorCode = PaymentOffSessionNotAttempted：此时没有支付记录，
        // 不会有任何支付事件回流，依赖事件推进状态机的调用方（订阅续费）必须自己收口这次失败；
        // 建单之后的失败则由 ApplyFailedAsync 发出 PaymentFailedEvent，调用方再收口一次就是重复计数。
        const string notAttempted = ErrorCodes.PaymentOffSessionNotAttempted;

        if (request.Amount <= 0)
            return Fail<PaymentDto>(ErrorCodes.PaymentInvalidAmount, 400, notAttempted);

        if (string.IsNullOrWhiteSpace(request.PaymentMethodToken))
            return Fail<PaymentDto>(ErrorCodes.SubscriptionPaymentMethodMissing, 400, notAttempted);

        var provider = _paymentProviderFactory.GetProvider(string.IsNullOrWhiteSpace(request.ChannelCode)
            ? PaymentOptions.DefaultChannelCode
            : request.ChannelCode);
        if (provider == null)
            return Fail<PaymentDto>(ErrorCodes.PaymentChannelNotSupported, 400, notAttempted);

        // 与建单同一条：存渠道自己的写法，不存请求原样
        var channelCode = provider.ChannelCode;

        if (!provider.SupportsOffSessionCharge)
            return Fail<PaymentDto>(ErrorCodes.PaymentOffSessionNotSupported, 400, notAttempted);

        var currency = ResolveCurrency(request.Currency, channelCode);

        // 后台扣款不再走优惠券链路（续费价由订阅侧决定），但仍需计税
        var tax = await _taxCalculator.CalculateAsync(new TaxCalculationRequest
        {
            NetAmount = request.Amount,
            Currency = currency,
            BusinessType = request.BusinessType
        }, cancellationToken);

        if (!tax.Succeeded || tax.Data == null)
            return Fail<PaymentDto>(tax.Message ?? ErrorCodes.PaymentCreationFailed, tax.Code ?? 400, notAttempted);

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
            // 与建单同一条缺省规则：经 PayPal 保管凭据扣的款记 PayPal，不记信用卡
            PaymentMethod = ResolveDefaultPaymentMethod(provider),
            Description = request.Description,
            UserId = request.UserId,
            BillingAccountType = (request.BillingAccount ?? AccountOf(request.UserId))?.Type,
            BillingAccountId = (request.BillingAccount ?? AccountOf(request.UserId))?.Id,
            CustomerName = request.CustomerName,
            CustomerEmail = request.CustomerEmail,
            ExtraData = request.ExtraData,
            TaxLinesJson = SerializeTaxLines(tax.Data),
            PaymentMethodBrand = request.PaymentMethodBrand,
            PaymentMethodLast4 = request.PaymentMethodLast4
        };

        await _paymentRepository.InsertAsync(payment, cancellationToken);

        Result<PaymentProviderChargeResult> chargeResult;
        try
        {
            chargeResult = await provider.ChargeOffSessionAsync(new PaymentProviderChargeDto
            {
                TradeNo = payment.TradeNo,
                BusinessOrderNo = request.BusinessOrderNo,
                Amount = payableAmount,
                Currency = currency,
                Description = request.Description,
                ProviderCustomerId = request.ProviderCustomerId,
                PaymentMethodToken = request.PaymentMethodToken
            });
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // 请求可能已经到了渠道（超时、连接中断、响应解析失败）：结果未知，不能当成拒付。
            // HttpClient 超时抛的也是 TaskCanceledException，所以按调用方的取消令牌而不是异常类型区分「被取消」。
            Logger.LogError(ex, "Off-session charge {TradeNo} on {Channel} threw; its outcome is unknown and is left for the channel to settle.",
                payment.TradeNo, channelCode);
            chargeResult = Result.Failure<PaymentProviderChargeResult>(ex.Message);
        }

        var charged = chargeResult.Succeeded ? chargeResult.Data : null;

        if (charged?.Status == PaymentStatus.Succeeded)
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

        // ★ 只有确定没收到钱的结果才落失败：渠道明确拒付 / 作废，或渠道声明请求根本没有发出去。
        // 其余一律「未落定」（渠道回报处理中，或这次调用的结果无从得知），支付保持 Processing 等渠道落定。
        // 此前一切不是 Succeeded 的结果都落 Failed 并发失败事件：订阅降级 PastDue、下一轮换一个流水号再扣
        // （幂等键跟流水号走，护不住它），而第一笔稍后入账时撞上 Failed 终态，同一期扣了两次。
        var declined = charged?.Status is PaymentStatus.Failed or PaymentStatus.Cancelled
            || (!chargeResult.Succeeded && chargeResult.ErrorCode == ErrorCodes.PaymentOffSessionNotAttempted);

        if (declined)
        {
            var failReason = charged?.FailReason ?? chargeResult.Message ?? ErrorCodes.PaymentOffSessionChargeFailed;
            if (!string.IsNullOrEmpty(charged?.ExternalTradeNo))
            {
                // 渠道侧的单号（Stripe 的 PaymentIntent）是这笔失败唯一的溯源线索，失败的 CAS 只写状态与渠道回报
                payment.ExternalTradeNo = charged.ExternalTradeNo;
                await _paymentRepository.AsQueryable()
                    .Where(p => p.Id == payment.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.ExternalTradeNo, charged.ExternalTradeNo), cancellationToken);
            }

            await ApplyFailedAsync(payment, failReason, JsonSerializer.Serialize(charged), cancellationToken);

            Logger.LogWarning("Off-session charge failed. TradeNo: {TradeNo}, Reason: {Reason}", payment.TradeNo, failReason);
            return Fail<PaymentDto>(failReason);
        }

        return await HoldUnsettledChargeAsync(payment, charged, chargeResult, cancellationToken);
    }

    /// <summary>
    /// 结果还没落定的 off-session 扣款：支付保持 <c>Processing</c>，不发任何事件，留给渠道回调落定；
    /// 回调始终不来的，到 <see cref="PaymentEntity.ExpireTime"/> 由过期清扫先向渠道核实（已付记成功、能作废的先作废）再过期。
    /// </summary>
    /// <remarks>
    /// 有效期分两档：渠道明确回报「处理中」（钱已在路上）等 <see cref="PaymentConstants.OffSessionSettlementHours"/>；
    /// 调用异常或渠道返回失败（请求是否到达渠道无从得知）只等在线收银台的默认有效期，到点核实。
    /// 返回成功、<c>Status = Processing</c>：调用方据此知道「还没收到钱，也没有失败」，不能再发起一笔，也不能当成已付。
    /// </remarks>
    private async Task<Result<PaymentDto>> HoldUnsettledChargeAsync(
        PaymentEntity payment, PaymentProviderChargeResult? charged, Result<PaymentProviderChargeResult> chargeResult, CancellationToken cancellationToken)
    {
        var expireTime = charged != null
            ? DateTime.UtcNow.AddHours(PaymentConstants.OffSessionSettlementHours)
            : DateTime.UtcNow.AddMinutes(PaymentOptions.AutoCloseExpireMinutes);
        var externalTradeNo = string.IsNullOrEmpty(charged?.ExternalTradeNo) ? payment.ExternalTradeNo : charged.ExternalTradeNo;
        var channelResponse = charged != null ? JsonSerializer.Serialize(charged) : chargeResult.Message;

        // 条件写：渠道回调可能已经在另一个请求里把它落定了
        var held = await _paymentRepository.AsQueryable()
            .Where(p => p.Id == payment.Id && p.Status == PaymentStatus.Processing)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.ExpireTime, expireTime)
                .SetProperty(p => p.ExternalTradeNo, externalTradeNo)
                .SetProperty(p => p.ChannelResponse, channelResponse), cancellationToken);

        if (held == 0)
        {
            var settled = await _paymentRepository.AsNoTracking().FirstOrDefaultAsync(p => p.Id == payment.Id, cancellationToken);
            if (settled == null)
                return Fail<PaymentDto>(ErrorCodes.PaymentNotFound, 404);

            return settled.Status is PaymentStatus.Failed or PaymentStatus.Cancelled
                ? Fail<PaymentDto>(ErrorCodes.PaymentOffSessionChargeFailed)
                : Ok(settled.MapTo<PaymentDto>());
        }

        payment.ExpireTime = expireTime;
        payment.ExternalTradeNo = externalTradeNo;
        payment.ChannelResponse = channelResponse;

        Logger.LogWarning(
            "Off-session charge {TradeNo} on {Channel} has not settled (channel status {Status}, {Message}); it stays Processing until the channel reports the outcome or {ExpireTime:u}.",
            payment.TradeNo, payment.ChannelCode, charged?.Status, charged?.FailReason ?? chargeResult.Message, expireTime);

        return Ok(payment.MapTo<PaymentDto>());
    }

    public async Task<Result<int>> CloseExpiredPaymentsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var globalExpireTime = now.AddMinutes(-PaymentOptions.AutoCloseExpireMinutes);

        // 分页处理：一次性把全部过期支付读进内存，在积压场景下会直接打爆进程。
        // ★ 按 (CreationTime, Id) 游标往后翻，而不是每轮只取「最老的前 N 条」：渠道侧还落不定的单
        // （Stripe 的银行扣款在 processing 里停留数个工作日，期间拒绝作废）原样留在库里，
        // 只取第一页时它们一直占满这一页，之后过期的单永远轮不到：券不还、过期事件不发、订阅首期不收口。
        var expiredQuery = _paymentRepository
            .Where(p => (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Processing)
                && ((p.ExpireTime != null && p.ExpireTime <= now)
                    || (p.ExpireTime == null && p.CreationTime < globalExpireTime)));

        var closed = 0;
        PaymentEntity? cursor = null;

        for (var page = 0; page < ExpiredPaymentScanMaxPages; page++)
        {
            var pageQuery = expiredQuery;
            if (cursor != null)
            {
                var afterTime = cursor.CreationTime;
                var afterId = cursor.Id;
                pageQuery = pageQuery.Where(p => p.CreationTime > afterTime
                    || (p.CreationTime == afterTime && p.Id.CompareTo(afterId) > 0));
            }

            var expiredPayments = await pageQuery
                .OrderBy(p => p.CreationTime)
                .ThenBy(p => p.Id)
                .Take(ExpiredPaymentScanPageSize)
                .ToListAsync(cancellationToken);

            foreach (var payment in expiredPayments)
            {
                if (await TryExpirePaymentAsync(payment, now, cancellationToken))
                    closed++;
            }

            if (expiredPayments.Count < ExpiredPaymentScanPageSize)
                break;

            cursor = expiredPayments[^1];
        }

        if (closed > 0)
            Logger.LogInformation("Closed {Count} expired payments", closed);

        return Ok(closed);
    }

    /// <summary>
    /// 把一笔已过有效期的支付推进为 <c>Expired</c>：先让渠道侧失效（或确认它已付 / 已死），再 CAS、还券、发过期事件。
    /// </summary>
    /// <returns>这一轮是否把它置成了过期；渠道侧落不定、已被并发路径推进或处理出错时为 false，留给下一轮。</returns>
    private async Task<bool> TryExpirePaymentAsync(PaymentEntity payment, DateTime now, CancellationToken cancellationToken)
    {
        try
        {
            // 与手工关单同一条规则：先让渠道侧失效（或确认它已付 / 已死），再改本地状态。
            // 已在渠道侧付掉的记成功而不是过期；渠道不可达的留到下一轮。
            var channel = await EnsureChannelSideNotPayableAsync(payment, cancellationToken);
            if (!channel.Succeeded)
                return false;

            // CAS：并发回调可能正在把这笔支付置成功，条件更新保证不会把已付订单标记过期
            var affected = await _paymentRepository.AsQueryable()
                .Where(p => p.Id == payment.Id
                    && (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Processing))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, PaymentStatus.Expired), cancellationToken);

            if (affected == 0)
                return false;

            payment.Status = PaymentStatus.Expired;

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
                    UserId = payment.UserId,
                    BillingAccountType = payment.BillingAccountType,
                    BillingAccountId = payment.BillingAccountId,
                    TenantId = payment.TenantId,
                    ExpiredTime = now,
                    ExtraData = payment.ExtraData
                });
            }

            return true;
        }
        catch (Exception ex)
        {
            // 单笔失败不拖累整批。CAS 之后（还券 / 发事件）才失败的，这笔已经过期，照样计数
            Logger.LogError(ex, "Failed to expire payment. TradeNo: {TradeNo}", payment.TradeNo);
            return payment.Status == PaymentStatus.Expired;
        }
    }

    /// <summary>调用方没指定账户时，付款人的个人账户（off-session 扣款的旧调用方只带 UserId）。</summary>
    private static BillingAccountRef? AccountOf(Guid? userId)
        => userId.HasValue ? BillingAccountRef.ForUser(userId.Value) : null;
}
