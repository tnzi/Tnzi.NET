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

        // 支付方式在渠道侧被更新（有效期、发卡行自动换卡）：只刷新展示信息，同样不涉及任何一笔支付
        if (callback.Kind == PaymentCallbackKind.PaymentMethodUpdated)
        {
            if (callback.UpdatedPaymentMethod != null)
            {
                var refreshed = await _paymentMethodService.RefreshByTokenAsync(
                    provider.ChannelCode, callback.UpdatedPaymentMethod, cancellationToken);
                if (!refreshed.Succeeded)
                    return refreshed;
            }

            await MarkCallbackProcessedAsync(eventId, cancellationToken);
            return Ok();
        }

        // 渠道侧的退款与争议：事件不带本模块的流水号，按渠道交易号定位那笔支付
        if (callback.Kind is PaymentCallbackKind.ExternalRefund or PaymentCallbackKind.Dispute)
            return await HandleChannelPaymentEventAsync(provider.ChannelCode, callback, eventId, cancellationToken);

        if (string.IsNullOrWhiteSpace(callback.TradeNo))
            return Fail(ErrorCodes.PaymentNotFound, 404);

        // ★ 跨租户按流水号定位。回调是匿名请求，中间件解析不出租户（Stripe / PayPal 不带任何租户线索），
        // 多租户开启时过滤器就成了 TenantId IS NULL —— 别的租户的每一笔支付在这里都是 404，
        // 渠道记投递失败并重投直到禁用端点，那些支付永远停在 Processing。
        // TradeNo 全局唯一，跨租户查它不会串到别人的单上。IgnoreQueryFilters 会把软删过滤器一起关掉，故显式补上。
        var payment = await _paymentRepository.AsQueryable()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.TradeNo == callback.TradeNo && !p.IsDeleted, cancellationToken);

        if (payment == null)
        {
            // 支付记录在调用渠道之前就已落库，因此这里找不到只可能是外部/非本系统的单，重投也不会变好
            Logger.LogWarning("Callback references unknown trade no {TradeNo} from {Channel}.",
                callback.TradeNo, request.ChannelCode);
            return Fail(ErrorCodes.PaymentNotFound, 404);
        }

        // 后续的 CAS、事件发布与下游状态机都要在这笔支付所属的租户里做：
        // 状态推进是带过滤器的条件更新，租户不对它就安静地影响 0 行。
        using (_currentTenant?.Change(payment.TenantId))
        {
            return await AdvanceFromCallbackAsync(payment, callback, eventId, cancellationToken);
        }
    }

    /// <summary>
    /// 渠道后台发生的退款、付款人发起的争议：按渠道交易号跨租户找到那笔支付，切到它的租户里记下。
    /// </summary>
    /// <remarks>
    /// 找不到不是错误：同一个渠道账户可能还服务着别的应用，那是别人的支付。安静地答 200（4xx 会让渠道一直重投）。
    /// </remarks>
    private async Task<Result> HandleChannelPaymentEventAsync(string channelCode, PaymentProviderCallbackResult callback, string? eventId, CancellationToken cancellationToken)
    {
        var externalTradeNo = callback.ExternalTradeNo;
        var payment = string.IsNullOrWhiteSpace(externalTradeNo)
            ? null
            : await _paymentRepository.AsQueryable()
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => p.ExternalTradeNo == externalTradeNo && p.ChannelCode == channelCode && !p.IsDeleted, cancellationToken);

        if (payment == null)
        {
            Logger.LogDebug("{Kind} event {EventId} from {Channel} references no local payment ({ExternalTradeNo}); ignored.",
                callback.Kind, eventId, channelCode, externalTradeNo);
            await MarkCallbackProcessedAsync(eventId, cancellationToken);
            return Ok();
        }

        using (_currentTenant?.Change(payment.TenantId))
        {
            if (callback.Kind == PaymentCallbackKind.ExternalRefund)
            {
                if (_refundService == null)
                {
                    Logger.LogError("A refund made in {Channel} for {TradeNo} cannot be recorded: no refund service is registered.", channelCode, payment.TradeNo);
                    return Fail("Refund service is not registered.", 500);
                }

                var recorded = await _refundService.RecordChannelRefundsAsync(payment.Id, callback.Refunds ?? [], cancellationToken);
                if (!recorded.Succeeded)
                    return recorded;
            }
            else if (callback.Dispute != null)
            {
                await ApplyDisputeAsync(payment, callback.Dispute, cancellationToken);
            }

            await MarkCallbackProcessedAsync(eventId, cancellationToken);
            return Ok();
        }
    }

    /// <summary>
    /// 记下争议状态并发 <see cref="PaymentDisputedEvent"/>。已结案（胜诉 / 败诉）的争议不会被一条迟到的进行中状态改回去；
    /// 状态没变时不发事件。不改动支付状态、不碰订阅：怎么处理被拒付的服务费是消费方的事。
    /// </summary>
    private async Task ApplyDisputeAsync(PaymentEntity payment, PaymentProviderDispute dispute, CancellationToken cancellationToken)
    {
        var previous = payment.DisputeStatus;
        var sameDispute = string.Equals(payment.DisputeId, dispute.DisputeId, StringComparison.Ordinal);
        if (sameDispute && previous == dispute.Status)
            return;
        if (sameDispute && previous is PaymentDisputeStatus.Won or PaymentDisputeStatus.Lost && !IsClosedDispute(dispute.Status))
        {
            Logger.LogInformation("Late {Status} for dispute {DisputeId} on {TradeNo} ignored: it is already {Previous}.",
                dispute.Status, dispute.DisputeId, payment.TradeNo, previous);
            return;
        }

        var now = DateTime.UtcNow;
        var reason = dispute.Reason is { Length: > 64 } r ? r[..64] : dispute.Reason;
        var expectedId = payment.DisputeId;
        var updated = await _paymentRepository.AsQueryable()
            .Where(p => p.Id == payment.Id && p.DisputeId == expectedId && p.DisputeStatus == previous)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.DisputeId, dispute.DisputeId)
                .SetProperty(p => p.DisputeStatus, dispute.Status)
                .SetProperty(p => p.DisputeReason, reason)
                .SetProperty(p => p.DisputeAmount, dispute.Amount)
                .SetProperty(p => p.DisputeUpdatedTime, now), cancellationToken);
        if (updated == 0)
            return;

        Logger.LogWarning("Payment {TradeNo} is disputed: {DisputeId} {Previous} -> {Status} ({Reason}), {Amount} {Currency}.",
            payment.TradeNo, dispute.DisputeId, previous, dispute.Status, reason, dispute.Amount, dispute.Currency ?? payment.Currency);

        if (EventBus != null)
        {
            await EventBus.PublishAsync(new PaymentDisputedEvent
            {
                PaymentId = payment.Id,
                TradeNo = payment.TradeNo,
                BusinessType = payment.BusinessType,
                BusinessOrderNo = payment.BusinessOrderNo,
                BillingAccountType = payment.BillingAccountType,
                BillingAccountId = payment.BillingAccountId,
                DisputeId = dispute.DisputeId,
                PreviousStatus = sameDispute ? previous : PaymentDisputeStatus.None,
                Status = dispute.Status,
                Reason = reason,
                Amount = dispute.Amount,
                Currency = string.IsNullOrWhiteSpace(dispute.Currency) ? payment.Currency : dispute.Currency.ToUpperInvariant()
            });
        }
    }

    private static bool IsClosedDispute(PaymentDisputeStatus status)
        => status is PaymentDisputeStatus.Won or PaymentDisputeStatus.Lost or PaymentDisputeStatus.WarningClosed;

    private async Task<Result> AdvanceFromCallbackAsync(PaymentEntity payment, PaymentProviderCallbackResult callback, string? eventId, CancellationToken cancellationToken)
    {
        var channelResponse = JsonSerializer.Serialize(callback);

        // 幂等 + 防回退：任何终态支付都不再被回调改写
        if (IsTerminalStatus(payment.Status))
        {
            await RecordPaidAfterLocalEndAsync(payment, callback.Status, channelResponse, cancellationToken);
            await MarkCallbackProcessedAsync(eventId, cancellationToken);
            return Ok();
        }

        Result applied;
        switch (callback.Status)
        {
            case PaymentStatus.Succeeded:
                applied = await ApplySucceededAsync(
                    payment, callback.PaidAmount, callback.ExternalTradeNo, channelResponse, cancellationToken,
                    paidCurrency: callback.Currency, savedPaymentMethodToken: callback.SavedPaymentMethodToken);
                break;

            // 渠道取消（Stripe payment_intent.canceled / PayPal VOIDED）与失败一样是终态：
            // 钱没有收到，券要还回去，订阅状态机要拿到失败信号。此前它落在 default 分支里
            // 被当成中间态忽略 —— 支付永远停在 Pending，占着的券要等过期扫描才归还，
            // 而扫描按 ExpireTime 走，那可能是几天之后。
            case PaymentStatus.Failed:
            case PaymentStatus.Cancelled:
                applied = await ApplyFailedAsync(
                    payment, callback.FailReason ?? "Unknown", channelResponse, cancellationToken, callback.Status);
                break;

            default:
                // 中间态（如 processing）不改写本地状态，等待终态事件；
                // 渠道回报「付款人已批准、等商户收款」的当场收款（见 FinalizeFromCallbackAsync）
                applied = callback.RequiresFinalization
                    ? await FinalizeFromCallbackAsync(payment, cancellationToken)
                    : Ok();
                break;
        }

        if (!applied.Succeeded)
            return applied;

        await MarkCallbackProcessedAsync(eventId, cancellationToken);
        return Ok();
    }

    /// <summary>
    /// 付款人已经批准、渠道在等商户收款：调用 <see cref="IPaymentProvider.FinalizePaymentAsync"/> 收下来，按结果推进。
    /// </summary>
    /// <remarks>
    /// PayPal 的一次性订单（<c>intent=CAPTURE</c>）被批准后停在 <c>APPROVED</c>，此前这条回调落进中间态分支被忽略，
    /// 没有任何路径去 capture：订单到期作废，付款人以为付了，商户一分钱没收到，本地过期扫描只留一条 Warning。
    /// 走到这里之前已经确认这笔支付还没结束（终态的在上面就返回了），不会替一张已关掉的单收钱。
    /// 收款请求没走通（渠道暂时不可用）答 502 不记已处理，让渠道重投：收款按流水号幂等，重投不会收两次。
    /// </remarks>
    private async Task<Result> FinalizeFromCallbackAsync(PaymentEntity payment, CancellationToken cancellationToken)
    {
        var provider = _paymentProviderFactory.GetProvider(payment.ChannelCode);
        if (provider == null)
            return Ok();

        var finalized = await provider.FinalizePaymentAsync(payment.ExternalTradeNo ?? payment.TradeNo);
        if (!finalized.Succeeded || finalized.Data == null)
        {
            Logger.LogWarning("Payment {TradeNo} was approved on channel {Channel} but could not be collected yet ({Message}); asking the channel to redeliver.",
                payment.TradeNo, payment.ChannelCode, finalized.Message);
            return Fail(finalized.Message ?? ErrorCodes.PaymentChannelStateUnknown, 502);
        }

        var state = finalized.Data;
        var channelResponse = JsonSerializer.Serialize(state);
        return state.Status switch
        {
            PaymentStatus.Succeeded => await ApplySucceededAsync(
                payment, state.Amount > 0 ? state.Amount : payment.PayableAmount, state.ExternalTradeNo, channelResponse, cancellationToken,
                paidCurrency: state.Currency, savedPaymentMethodToken: state.SavedPaymentMethodToken),
            PaymentStatus.Failed or PaymentStatus.Cancelled => await ApplyFailedAsync(
                payment, state.FailReason ?? "Collection refused by the channel", channelResponse, cancellationToken, state.Status),
            _ => Ok()
        };
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
        DateTime? paidTime = null,
        string? paidCurrency = null,
        string? savedPaymentMethodToken = null)
    {
        // 币种一致性校验：只比数值不比币种，就是把「收到 100 JPY」判成「收到 100 USD」——
        // 差两个数量级，而这笔支付会被记成完全成功。渠道没报币种时跳过（自定义渠道 / 早期实现）：
        // 一律拒绝会让经这类渠道的每一笔付款都卡住，代价远大于它挡下的那一类错配。
        if (!string.IsNullOrWhiteSpace(paidCurrency)
            && !string.Equals(paidCurrency, payment.Currency, StringComparison.OrdinalIgnoreCase))
        {
            Logger.LogWarning("Payment currency mismatch. TradeNo: {TradeNo}, Expected: {Expected}, Reported: {Reported}",
                payment.TradeNo, payment.Currency, paidCurrency);
            return Fail(ErrorCodes.PaymentAmountMismatch, 400);
        }

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
            // 并发路径已抢先处理：已记成功的幂等返回。
            // ★ 但抢先的那一方也可能是关单 / 过期：读这一行之后、渠道回报「已付」之前它以「没收到钱」结束了。
            // 收款（FinalizePaymentAsync）让这个窗口里的钱是本系统自己收下来的，安静返回就是一笔没人知道的入账。
            // 与回调撞上本地终态同一个处理：状态不回退，渠道回报写在支付行上并告警，供人工退款。
            var current = await _paymentRepository.AsNoTracking()
                .Where(p => p.Id == payment.Id)
                .Select(p => (PaymentStatus?)p.Status)
                .FirstOrDefaultAsync(cancellationToken);

            if (current is { } ended && IsUnpaidTerminalStatus(ended))
                await RecordPaidAfterLocalEndAsync(payment, ended, PaymentStatus.Succeeded, channelResponse, cancellationToken);

            return Ok();
        }

        // 同步内存值供事件使用
        payment.Status = PaymentStatus.Succeeded;
        payment.PaidTime = completedTime;
        payment.PaidAmount = paidAmount;
        payment.ExternalTradeNo = externalTradeNo ?? payment.ExternalTradeNo;

        // 付款时要求留存的卡：先登记，再发完成事件，下游（订阅）才能在同一次回流里把它换成续费用的卡
        var savedMethodId = await SavePaymentMethodFromPaymentAsync(payment, savedPaymentMethodToken, cancellationToken);

        if (EventBus != null)
            await EventBus.PublishAsync(BuildCompletedEvent(payment, savedMethodId));

        Logger.LogInformation("Payment completed. TradeNo: {TradeNo}, Amount: {Amount} {Currency}",
            payment.TradeNo, payment.PaidAmount, payment.Currency);

        return Ok();
    }

    /// <summary>
    /// 把付款时留存下来的支付方式登记到这笔支付所属的账户上，设为默认卡；返回登记出的支付方式 Id。
    /// </summary>
    /// <remarks>
    /// ★ 钱已经收到，存卡失败（渠道暂时不可达、归属校验不过）<b>不影响这笔支付的成功</b>，只记一条 Warning：
    /// 订阅照常激活 / 恢复，只是下一次续费没有新卡可扣，会进入催款，由用户再到订阅页换一次卡。
    /// 只有赢得成功 CAS 的那一方走到这里，所以同一笔付款至多登记一次。
    /// </remarks>
    private async Task<Guid?> SavePaymentMethodFromPaymentAsync(PaymentEntity payment, string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var account = BillingAccountRef.From(payment.BillingAccountType, payment.BillingAccountId);
        if (account == null)
            return null;

        try
        {
            var bound = await _paymentMethodService.BindEntityAsync(
                account, payment.UserId ?? Guid.Empty, payment.ChannelCode, token, payment.CustomerName, payment.CustomerEmail,
                setAsDefault: true, cancellationToken);

            if (bound.Succeeded && bound.Data != null)
            {
                // 收据上要印付款用的卡：这一笔付的就是刚存下的这张
                var brand = bound.Data.Brand;
                var last4 = bound.Data.Last4;
                if (!string.IsNullOrWhiteSpace(last4))
                {
                    await _paymentRepository.AsQueryable()
                        .Where(p => p.Id == payment.Id && p.PaymentMethodLast4 == null)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(p => p.PaymentMethodBrand, brand)
                            .SetProperty(p => p.PaymentMethodLast4, last4), cancellationToken);
                }

                return bound.Data.Id;
            }

            Logger.LogWarning("Payment {TradeNo} was paid but the card it was asked to save could not be stored ({Reason}).",
                payment.TradeNo, bound.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning(ex, "Payment {TradeNo} was paid but the card it was asked to save could not be stored.", payment.TradeNo);
        }

        return null;
    }

    /// <summary>
    /// 把一笔支付推进为失败或取消：CAS 抢占 → 释放已核销的优惠券 → 发布失败事件。
    /// </summary>
    /// <remarks>
    /// 「取消」写的是 <c>Cancelled</c> 而不是 <c>Failed</c>（对账与报表要分得出这两件事），
    /// 但发出去的仍是 <c>PaymentFailedEvent</c>：对下游而言两者是同一件事 ——
    /// 钱没有收到，订阅要降级，券要还回去。
    /// </remarks>
    private async Task<Result> ApplyFailedAsync(
        PaymentEntity payment,
        string failReason,
        string? channelResponse,
        CancellationToken cancellationToken,
        PaymentStatus terminalStatus = PaymentStatus.Failed)
    {
        var affected = await _paymentRepository.AsQueryable()
            .Where(p => p.Id == payment.Id
                && (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Processing))
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, terminalStatus)
                .SetProperty(p => p.ChannelResponse, channelResponse), cancellationToken);

        if (affected == 0)
            return Ok();

        payment.Status = terminalStatus;

        await ReleaseCouponForPaymentAsync(payment, cancellationToken);

        if (EventBus != null)
            await EventBus.PublishAsync(BuildFailedEvent(payment, failReason));

        Logger.LogWarning("Payment {Status}. TradeNo: {TradeNo}, Reason: {Reason}",
            terminalStatus, payment.TradeNo, failReason);

        return Ok();
    }

    /// <summary>
    /// 支付确定失败/过期后归还其占用的优惠券。
    /// 券在建单时就核销掉了，不还就等于用户付款失败还赔一张券。
    /// </summary>
    /// <remarks>
    /// 「按支付找到那条核销记录」这一步在契约那一侧（<see cref="ICouponService.ReleaseCouponForPaymentAsync"/>）。
    /// 拆分前这里持有 <c>IRepository&lt;CouponUsage&gt;</c> 自己查，那是父模块对促销表的直接读取。
    /// <c>CouponId == null</c> 的快速返回留着：这笔支付压根没用券，连问都不用问。
    /// </remarks>
    private async Task ReleaseCouponForPaymentAsync(PaymentEntity payment, CancellationToken cancellationToken)
    {
        if (_couponService == null || payment.CouponId == null)
            return;

        await _couponService.ReleaseCouponForPaymentAsync(payment.Id, cancellationToken);
    }

    /// <summary>
    /// 本地已经以「没收到钱」结束的单（关闭 / 过期 / 失败 / 取消），渠道却报「已付」：钱收了但本地状态不能回退
    /// （那会让一张已结束的业务单收到完成事件）。这笔钱只能退，所以必须留下线索，把渠道回报写在支付行上并告警，
    /// 而不是像其它终态那样静静吞掉。
    /// 关单前的作废 / 查渠道状态是第一道，渠道侧「拒付不是终态」的映射（Stripe 收银台上的拒付读作处理中）是第二道，
    /// 这里是它们管不到的那部分：不能作废的渠道、回调与关单同时发生、渠道把一次可重试的拒付报成了终态失败。
    /// </summary>
    private Task RecordPaidAfterLocalEndAsync(PaymentEntity payment, PaymentStatus reportedStatus, string channelResponse, CancellationToken cancellationToken)
        => RecordPaidAfterLocalEndAsync(payment, payment.Status, reportedStatus, channelResponse, cancellationToken);

    /// <inheritdoc cref="RecordPaidAfterLocalEndAsync(PaymentEntity, PaymentStatus, string, CancellationToken)"/>
    /// <remarks><c>localStatus</c> 是支付此刻在库里的状态（调用方手里那份可能读在它结束之前）。</remarks>
    private async Task RecordPaidAfterLocalEndAsync(PaymentEntity payment, PaymentStatus localStatus, PaymentStatus reportedStatus, string? channelResponse, CancellationToken cancellationToken)
    {
        if (reportedStatus != PaymentStatus.Succeeded || !IsUnpaidTerminalStatus(localStatus))
            return;

        if (channelResponse != null)
        {
            await _paymentRepository.AsQueryable()
                .Where(p => p.Id == payment.Id
                    && (p.Status == PaymentStatus.Closed || p.Status == PaymentStatus.Expired
                        || p.Status == PaymentStatus.Failed || p.Status == PaymentStatus.Cancelled))
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.ChannelResponse, channelResponse), cancellationToken);
        }

        Logger.LogWarning(
            "Channel {Channel} reports payment {TradeNo} (channel order {ExternalTradeNo}) as paid after it was locally {Status}: the money was captured for an order this system had already ended and needs a manual refund. Channel response recorded on the payment row.",
            payment.ChannelCode, payment.TradeNo, payment.ExternalTradeNo, localStatus);
    }

    /// <summary>以「没收到钱」结束的终态：关闭、过期、失败、取消。</summary>
    private static bool IsUnpaidTerminalStatus(PaymentStatus status)
        => status is PaymentStatus.Closed or PaymentStatus.Expired or PaymentStatus.Failed or PaymentStatus.Cancelled;

    /// <summary>
    /// 终态判定：处于这些状态的支付不应再被回调/同步改写，防止状态回退
    /// </summary>
    private static bool IsTerminalStatus(PaymentStatus status)
        => status is PaymentStatus.Succeeded or PaymentStatus.Failed or PaymentStatus.Closed
            or PaymentStatus.Cancelled or PaymentStatus.Expired
            or PaymentStatus.Refunded or PaymentStatus.PartialRefunded;

    /// <summary>
    /// 这条事件是不是已经处理过了。
    /// </summary>
    /// <remarks>
    /// ★ <b>去重是一层短路，不承载正确性</b>：状态推进走条件更新（CAS），终态支付一律不再被
    /// 回调改写，所以同一条事件被处理两次不会产生第二次状态变更。缓存缺席 / 只在本进程有效
    /// （框架默认就是进程内缓存，多实例下每个实例各存一份）都只意味着重复工作与更吵的日志，
    /// 不意味着重复扣款或重复推进。这条区分写在这里，是为了下次有人想「把它做可靠」之前
    /// 先知道它在保什么 —— 以及别把它当成唯一防线。缺席与作用范围由
    /// <c>PaymentModule.OnApplicationInitializationAsync</c> 在启动期报出来。
    /// </remarks>
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

    /// <summary>
    /// 支付事件显式带上付款人与租户：付款人是下游状态机核对归属的依据；租户是因为匿名回调与
    /// 后台扫描的发布方没有请求上下文，事件总线在新 scope 里捕获到的是空租户，处理器随后会在
    /// 「TenantId IS NULL」的过滤器下找不到它要推进的那条业务记录。
    /// </summary>
    private static PaymentCompletedEvent BuildCompletedEvent(PaymentEntity payment, Guid? savedPaymentMethodId = null) => new()
    {
        PaymentId = payment.Id,
        TradeNo = payment.TradeNo,
        BusinessOrderNo = payment.BusinessOrderNo,
        BusinessType = payment.BusinessType,
        UserId = payment.UserId,
        BillingAccountType = payment.BillingAccountType,
        BillingAccountId = payment.BillingAccountId,
        SavedPaymentMethodId = savedPaymentMethodId,
        TenantId = payment.TenantId,
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
        UserId = payment.UserId,
        BillingAccountType = payment.BillingAccountType,
        BillingAccountId = payment.BillingAccountId,
        TenantId = payment.TenantId,
        FailReason = failReason,
        ExtraData = payment.ExtraData
    };
}
