namespace Tnzi.Payment.Services;

/// <summary>
/// 支付服务实现：建单（折扣 + 计税 + 渠道下单）、查询、关闭、同步与线下确认。
/// 回调处理见 PaymentService.Callback.cs，后台扣款与清扫见 PaymentService.Billing.cs。
/// </summary>
public partial class PaymentService : ApplicationService, IPaymentService
{
    private readonly IRepository<PaymentEntity, Guid> _paymentRepository;
    private readonly IPaymentProviderFactory _paymentProviderFactory;
    private readonly IPaymentTaxCalculator _taxCalculator;
    private readonly IPaymentMethodService _paymentMethodService;
    private readonly IOptionsMonitor<PaymentOptions> _paymentOptionsMonitor;

    /// <summary>
    /// 折扣能力。未加载促销包时为 null —— 那台宿主上不存在任何优惠券码，
    /// 于是带券码的建单被拒（400 <c>COUPON_INVALID</c>），不带券码的建单一个字节不差。
    /// </summary>
    private readonly ICouponService? _couponService;
    private readonly ICache? _cache;

    /// <summary>
    /// 渠道回调是匿名请求，没有任何租户线索；按流水号跨租户找到支付之后，
    /// 后续处理（CAS、事件发布、下游状态机）要切到那笔支付所属的租户里做。
    /// </summary>
    private readonly ICurrentTenant? _currentTenant;

    private const int ExpiredPaymentScanPageSize = 200;

    private PaymentOptions PaymentOptions => _paymentOptionsMonitor.CurrentValue;

    public PaymentService(
        IRepository<PaymentEntity, Guid> paymentRepository,
        IPaymentProviderFactory paymentProviderFactory,
        IPaymentTaxCalculator taxCalculator,
        IPaymentMethodService paymentMethodService,
        IOptionsMonitor<PaymentOptions> paymentOptionsMonitor,
        IServiceProvider serviceProvider,
        ICouponService? couponService = null,
        ICache? cache = null,
        ICurrentTenant? currentTenant = null)
        : base(serviceProvider)
    {
        _paymentRepository = Check.NotNull(paymentRepository);
        _paymentProviderFactory = Check.NotNull(paymentProviderFactory);
        _taxCalculator = Check.NotNull(taxCalculator);
        _paymentMethodService = Check.NotNull(paymentMethodService);
        _paymentOptionsMonitor = Check.NotNull(paymentOptionsMonitor);
        _couponService = couponService;
        _cache = cache;
        _currentTenant = currentTenant;
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

        // 有效期两端都要挡：负得足够多会让 AddMinutes 抛 ArgumentOutOfRangeException（一个畸形请求换来 500），
        // 大得足够多会让订单永不过期 —— 清扫扫不到它，为它核销的优惠券也就永远不归还。
        if (request.ExpireMinutes is { } minutes && (minutes < 1 || minutes > PaymentConstants.MaxPaymentExpireMinutes))
            return Fail<PaymentOrderResultDto>(ErrorCodes.PaymentInvalidExpireMinutes, 400);

        if (!EnsureRedirectAllowed(request.ReturnUrl, nameof(request.ReturnUrl)))
            return Fail<PaymentOrderResultDto>(ErrorCodes.PaymentReturnUrlNotAllowed, 400);

        // 订阅支付只由订阅模块建（首付 / 补差），它的 ExtraData 驱动订阅状态机。
        // 用户面端点建出来的这种单不能静默剥掉元数据放行：那会让一次前端误传看起来像
        // 「付了钱但订阅没动」。直接拒绝，症状当场可见。
        if (request.BusinessType == BusinessType.Subscription && !request.IsSystemInitiated)
        {
            Logger.LogWarning(
                "Refused a user-initiated payment with BusinessType=Subscription. BusinessOrderNo: {BusinessOrderNo}, UserId: {UserId}. "
                + "Subscription payments are created by the subscriptions module only.",
                request.BusinessOrderNo, CurrentUser?.Id);
            return Fail<PaymentOrderResultDto>(ErrorCodes.PaymentBusinessTypeSystemOnly, 400);
        }

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
                return Fail<PaymentOrderResultDto>(applied.Message ?? ErrorCodes.CouponInvalid, applied.Code ?? 400, applied.ErrorCode);
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

        // ★ 先让渠道侧失效，再改本地状态。只改本地，渠道侧的支付意图原样活着：付款人正开着收银台时
        // 本地关了单，他接着付掉，成功回调撞上本地终态被幂等守卫吞掉 —— 钱收了，没有事件、没有告警。
        var channel = await EnsureChannelSideNotPayableAsync(payment, cancellationToken);
        if (!channel.Succeeded)
            return channel;

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

    /// <summary>
    /// 本地关单 / 过期之前确认渠道侧再也收不到这张单的钱。三层：能作废的渠道先作废；作废不了（渠道没这能力，
    /// 或渠道拒绝作废 —— Stripe 对已付 / 已作废的 intent 都会拒绝）就查渠道侧状态：已付则把它记成功并拒绝关单，
    /// 已失败 / 已作废则放行；仍可付款而渠道又不能作废的（PayPal 订单没有作废接口）放行并告警，回调侧留痕兜底；
    /// 渠道侧读不到时不放行 —— 宁可让这张单多开一轮，也不能在渠道侧仍可付款时本地关掉。
    /// </summary>
    /// <returns>成功 = 可以本地关单；失败 = 不能关（已记成功 409 <c>PAYMENT_ALREADY_PAID</c>，或渠道不可达 409 <c>PAYMENT_CHANNEL_STATE_UNKNOWN</c>）。</returns>
    private async Task<Result> EnsureChannelSideNotPayableAsync(PaymentEntity payment, CancellationToken cancellationToken)
    {
        // 线下渠道没有渠道侧；ExternalTradeNo 为空的在线单也可能在渠道侧存在（建单后落库前进程死掉），
        // 因此按 SyncOrderAsync 的口径用内部 TradeNo 兜底，渠道按元数据查得到就查。
        if (IsOfflineChannel(payment.ChannelCode))
            return Ok();

        var provider = _paymentProviderFactory.GetProvider(payment.ChannelCode);
        if (provider == null)
        {
            // 渠道包没加载：它的回调也验不了签，本地状态就是全部事实。留下告警而不是把这张单永远卡住。
            Logger.LogWarning(
                "Closing payment {TradeNo} on channel {Channel} without voiding it: the channel is not loaded, so its order {ExternalTradeNo} could neither be voided nor checked.",
                payment.TradeNo, payment.ChannelCode, payment.ExternalTradeNo);
            return Ok();
        }

        var channelTradeNo = payment.ExternalTradeNo ?? payment.TradeNo;

        if (provider.SupportsPaymentCancellation)
        {
            var cancelled = await provider.CancelPaymentAsync(channelTradeNo);
            if (cancelled.Succeeded)
                return Ok();

            Logger.LogInformation("Channel {Channel} refused to void payment {TradeNo} ({Message}); checking its state before closing.",
                payment.ChannelCode, payment.TradeNo, cancelled.Message);
        }

        var state = await provider.SyncOrderAsync(channelTradeNo);
        if (!state.Succeeded)
        {
            if (provider.SupportsPaymentCancellation)
            {
                // 作废与查询都失败 = 渠道不可达。这个渠道平时答得上来，等下一轮
                Logger.LogWarning(
                    "Payment {TradeNo} left open: channel {Channel} could not void it and its state could not be read ({Message}).",
                    payment.TradeNo, payment.ChannelCode, state.Message);
                return Fail(ErrorCodes.PaymentChannelStateUnknown, 409);
            }

            // 不能作废的渠道读不到状态：留着这张单下一轮也不会多知道什么（渠道把订单清掉后会永远 404），
            // 而关掉之后钱若真的到了，回调侧留痕告警 —— 与「仍可付款」那条放行的残余形态相同
            Logger.LogWarning(
                "Closing payment {TradeNo} locally: channel {Channel} cannot void order {ExternalTradeNo} and its state could not be read ({Message}); a payment arriving after this close is recorded on the payment row and needs a manual refund.",
                payment.TradeNo, payment.ChannelCode, channelTradeNo, state.Message);
            return Ok();
        }

        switch (state.Data?.Status)
        {
            case PaymentStatus.Succeeded:
            {
                // 渠道已经收到钱：走与回调同一条推进路径（金额校验 + 事件），这不是一次关单
                var paidAmount = state.Data.Amount > 0 ? state.Data.Amount : payment.PayableAmount;
                await ApplySucceededAsync(payment, paidAmount, state.Data.ExternalTradeNo, JsonSerializer.Serialize(state.Data), cancellationToken);
                Logger.LogWarning("Payment {TradeNo} was already paid on channel {Channel} when it was about to be closed; recorded as succeeded instead.",
                    payment.TradeNo, payment.ChannelCode);
                return Fail(ErrorCodes.PaymentAlreadyPaid, 409);
            }

            case PaymentStatus.Failed:
            case PaymentStatus.Cancelled:
                return Ok();

            default:
                if (provider.SupportsPaymentCancellation)
                {
                    // 作废被拒而渠道侧仍可付款（Stripe 的 processing 这类中间态）：等它落定，下一轮再来
                    Logger.LogWarning("Payment {TradeNo} left open: channel {Channel} refused to void it while it is still payable (channel status {Status}).",
                        payment.TradeNo, payment.ChannelCode, state.Data?.Status);
                    return Fail(ErrorCodes.PaymentChannelStateUnknown, 409);
                }

                Logger.LogWarning(
                    "Closing payment {TradeNo} locally while channel {Channel} cannot void order {ExternalTradeNo}: it stays payable on the channel side until it expires there; a payment arriving after this close is recorded on the payment row and needs a manual refund.",
                    payment.TradeNo, payment.ChannelCode, channelTradeNo);
                return Ok();
        }
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
            if (result.Data != null)
                await RecordPaidAfterLocalCloseAsync(payment, result.Data.Status, JsonSerializer.Serialize(result.Data), cancellationToken);
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

    /// <summary>
    /// 校验调用方给的回跳地址；不通过时记一条指名配置项的 Error 再返回 false。
    /// </summary>
    /// <remarks>
    /// 日志是必须的：没配 <c>Payment:AllowedRedirectHosts</c> 与「用户提交了一个恶意地址」
    /// 在客户端看起来完全一样，少了这条，一次部署疏漏会被读成「前端老是传错回跳地址」。
    /// </remarks>
    private bool EnsureRedirectAllowed(string? url, string fieldName)
    {
        if (PaymentRedirectPolicy.IsAllowed(url, PaymentOptions))
            return true;

        Logger.LogError(
            "Rejected {Field} '{Url}': the host is not in Payment:AllowedRedirectHosts (currently {Hosts}). "
            + "Add the payer-facing host there, otherwise every caller-supplied redirect is refused.",
            fieldName, url, string.Join(", ", PaymentRedirectPolicy.AllowedHosts(PaymentOptions)));

        return false;
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

    /// <summary>
    /// 试算券码带来的折扣。没带券码时直接放行（返回一个成功但无数据的结果）。
    /// </summary>
    /// <remarks>
    /// 未加载促销包时返回 400 <c>COUPON_INVALID</c> 而不是 501：501 是「本服务器不提供此功能」，
    /// 适合一个整体只讲促销的端点；而这里是<b>建单</b>端点，它本身好端端的，
    /// 只是收到了一个在这台宿主上不可能有效的券码 —— 那正是 <c>COUPON_INVALID</c> 的意思。
    /// 但服务端要把话说全：记一条 Error 指名要加载哪个包，否则一次部署疏漏会被读成
    /// 「用户老是输错码」，而两者在客户端看起来完全一样。
    /// </remarks>
    private async Task<Result<CouponPreviewDto>> PreviewCouponAsync(
        CreatePaymentDto request, string currency, Guid? userId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CouponCode))
            return Result.Success<CouponPreviewDto>(null!);

        if (_couponService == null)
        {
            Logger.LogError(
                "A payment named coupon code '{CouponCode}', but no ICouponService is registered, so every coupon code "
                + "is rejected. Load the Tnzi.Payment.Promotions module ([DependsOn(typeof(PaymentPromotionsModule))]) "
                + "or register your own ICouponService.", request.CouponCode);

            return Result.Failure<CouponPreviewDto>(ErrorCodes.CouponInvalid, 400);
        }

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
