namespace Tnzi.Payment.Services;

/// <summary>
/// 退款服务实现
/// </summary>
public class RefundService : ApplicationService, IRefundService
{
    private readonly IRepository<Refund, Guid> _refundRepository;
    private readonly IRepository<PaymentEntity, Guid> _paymentRepository;
    private readonly IPaymentProviderFactory _paymentProviderFactory;
    private readonly IOptionsMonitor<PaymentOptions> _paymentOptionsMonitor;

    private const int ReconcileScanPageSize = 200;

    private PaymentOptions PaymentOptions => _paymentOptionsMonitor.CurrentValue;

    public RefundService(
        IRepository<Refund, Guid> refundRepository,
        IRepository<PaymentEntity, Guid> paymentRepository,
        IPaymentProviderFactory paymentProviderFactory,
        IOptionsMonitor<PaymentOptions> paymentOptionsMonitor,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _refundRepository = Check.NotNull(refundRepository);
        _paymentRepository = Check.NotNull(paymentRepository);
        _paymentProviderFactory = Check.NotNull(paymentProviderFactory);
        _paymentOptionsMonitor = Check.NotNull(paymentOptionsMonitor);
    }

    public async Task<Result<RefundDto>> CreateRefundAsync(CreateRefundDto request, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        if (request.RefundAmount <= 0)
            return Fail<RefundDto>(ErrorCodes.PaymentInvalidAmount, 400);

        var payment = await _paymentRepository.FirstOrDefaultAsync(
            p => p.TradeNo == request.TradeNo && (!ownerUserId.HasValue || p.UserId == ownerUserId.Value), cancellationToken);

        if (payment == null)
            return Fail<RefundDto>(ErrorCodes.PaymentNotFound, 404);

        // 已成功或部分退款的支付均可继续退款（剩余可退额由下方累计校验把关）；已全额退款则不可再退
        if (payment.Status != PaymentStatus.Succeeded && payment.Status != PaymentStatus.PartialRefunded)
            return Fail<RefundDto>(ErrorCodes.PaymentCannotRefund, 400);

        var options = PaymentOptions;

        // 事务保护：退款金额校验 + 退款记录创建原子操作，防止并发超退
        var refund = await ExecuteInUnitOfWorkAsync(async ct =>
        {
            // 校验退款金额不超过已付金额减去已退款金额
            var existingRefundAmount = await _refundRepository
                .Where(r => r.PaymentId == payment.Id && r.Status != RefundStatus.Cancelled && r.Status != RefundStatus.Failed)
                .SumAsync(r => r.RefundAmount, ct);

            if (request.RefundAmount > payment.PaidAmount - existingRefundAmount)
                return Fail<Refund>(ErrorCodes.PaymentRefundExceedAmount, 400);

            // 每日退款限额按币种统计：跨币种求和会把 100 JPY 和 100 USD 当成同一量级
            var todayStart = DateTime.UtcNow.Date;
            var todayEnd = todayStart.AddDays(1);
            var todayRefunds = await _refundRepository
                .Where(r => r.CreationTime >= todayStart && r.CreationTime < todayEnd
                            && r.Currency == payment.Currency
                            && r.Status != RefundStatus.Cancelled && r.Status != RefundStatus.Failed)
                .SumAsync(r => r.RefundAmount, ct);

            if (todayRefunds + request.RefundAmount > options.MaxRefundAmountPerDay)
                return Fail<Refund>(ErrorCodes.RefundDailyLimitExceeded, 400);

            bool needsApproval = options.EnableRefundApproval && request.RefundAmount >= options.RefundApprovalThreshold;

            var newRefund = new Refund
            {
                RefundNo = Refund.GenerateRefundNo(),
                PaymentId = payment.Id,
                RefundAmount = request.RefundAmount,
                Currency = payment.Currency,
                Reason = request.Reason,
                RefundType = request.RefundAmount == payment.PaidAmount ? RefundType.Full : RefundType.Partial,
                Status = needsApproval ? RefundStatus.Pending : RefundStatus.Processing,
                BusinessOrderNo = payment.BusinessOrderNo
            };

            await _refundRepository.InsertAsync(newRefund, ct);

            Logger.LogInformation("Refund created. TradeNo: {TradeNo}, RefundNo: {RefundNo}, Amount: {Amount}, Status: {Status}",
                request.TradeNo, newRefund.RefundNo, request.RefundAmount, newRefund.Status);

            return Ok(newRefund);
        }, cancellationToken);

        if (!refund.Succeeded || refund.Data == null)
            return Fail<RefundDto>(refund.Message ?? ErrorCodes.StripeRefundFailed, refund.Code ?? 400);

        // 不需要审批，直接执行退款（在事务外执行，因为涉及外部 API 调用）
        if (refund.Data.Status == RefundStatus.Processing)
        {
            var processResult = await ProcessRefundInternalAsync(refund.Data, payment, cancellationToken);
            if (!processResult.Succeeded)
                return Fail<RefundDto>(processResult.Message ?? ErrorCodes.StripeRefundFailed);
        }

        return Ok(refund.Data.MapTo<RefundDto>());
    }

    public async Task<Result> ApproveRefundAsync(Guid refundId, ApproveRefundDto request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        var refund = await _refundRepository.FirstOrDefaultAsync(r => r.Id == refundId, cancellationToken);
        if (refund == null)
            return Fail(ErrorCodes.RefundNotFound, 404);

        if (refund.Status != RefundStatus.Pending)
            return Fail(ErrorCodes.RefundCannotApprove, 400);

        refund.ApproverId = GetRequiredCurrentUser().Id;
        refund.ApproveTime = DateTime.UtcNow;
        refund.ApproveRemark = request.Remark;
        refund.Status = request.Approved ? RefundStatus.Approved : RefundStatus.Rejected;

        await _refundRepository.UpdateAsync(refund, cancellationToken);

        Logger.LogInformation("Refund {Status}. RefundNo: {RefundNo}, Approver: {Approver}",
            request.Approved ? "Approved" : "Rejected", refund.RefundNo, refund.ApproverId);

        if (request.Approved)
            return await ProcessRefundAsync(refundId, cancellationToken);

        return Ok();
    }

    public async Task<Result> ProcessRefundAsync(Guid refundId, CancellationToken cancellationToken = default)
    {
        var refund = await _refundRepository.FirstOrDefaultAsync(r => r.Id == refundId, cancellationToken);
        if (refund == null)
            return Fail(ErrorCodes.RefundNotFound, 404);

        if (refund.Status != RefundStatus.Approved && refund.Status != RefundStatus.Processing)
            return Fail(ErrorCodes.RefundCannotProcess, 400);

        var payment = await _paymentRepository.FirstOrDefaultAsync(p => p.Id == refund.PaymentId, cancellationToken);
        if (payment == null)
            return Fail(ErrorCodes.PaymentNotFound, 404);

        return await ProcessRefundInternalAsync(refund, payment, cancellationToken);
    }

    private async Task<Result> ProcessRefundInternalAsync(Refund refund, PaymentEntity payment, CancellationToken cancellationToken)
    {
        var provider = _paymentProviderFactory.GetProvider(payment.ChannelCode);
        if (provider == null)
            return Fail(ErrorCodes.PaymentChannelNotSupported, 400);

        // 原子抢占（CAS）：仅当退款仍为 Approved/Processing 时置为 Refunding，
        // 防止并发/重试重复调用渠道退款接口造成多次退款
        var claimed = await _refundRepository.AsQueryable()
            .Where(r => r.Id == refund.Id
                && (r.Status == RefundStatus.Approved || r.Status == RefundStatus.Processing))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, RefundStatus.Refunding), cancellationToken);

        if (claimed == 0)
            return Fail(ErrorCodes.RefundCannotProcess, 409);

        refund.Status = RefundStatus.Refunding;

        var result = await provider.RefundAsync(new PaymentProviderRefundDto
        {
            TradeNo = payment.TradeNo,
            ExternalTradeNo = payment.ExternalTradeNo,
            RefundNo = refund.RefundNo,
            RefundAmount = refund.RefundAmount,
            Currency = refund.Currency,
            Reason = refund.Reason
        });

        if (!result.Succeeded || result.Data == null)
        {
            refund.Status = RefundStatus.Failed;
            await _refundRepository.UpdateAsync(refund, cancellationToken);
            await PublishRefundEventAsync(refund, payment, succeeded: false, result.Message);
            return Fail(result.Message ?? ErrorCodes.StripeRefundFailed);
        }

        refund.ExternalRefundNo = result.Data.ExternalRefundNo;

        // 尊重渠道回报的真实状态：渠道说 pending 就落 Refunding，由对账扫描推进到终态。
        // 此前无条件写 Succeeded，会把数日后才可能失败的退款当场记成成功。
        await ApplyRefundStatusAsync(refund, payment, result.Data.Status, cancellationToken);

        return refund.Status == RefundStatus.Failed
            ? Fail(ErrorCodes.StripeRefundFailed)
            : Ok();
    }

    public async Task<Result<int>> ReconcilePendingRefundsAsync(CancellationToken cancellationToken = default)
    {
        var since = DateTime.UtcNow.AddDays(-PaymentOptions.RefundReconcileLookbackDays);

        var pending = await _refundRepository
            .Where(r => r.Status == RefundStatus.Refunding
                && r.ExternalRefundNo != null
                && r.CreationTime >= since)
            .OrderBy(r => r.CreationTime)
            .Take(ReconcileScanPageSize)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
            return Ok(0);

        var settled = 0;

        foreach (var refund in pending)
        {
            try
            {
                var payment = await _paymentRepository.FirstOrDefaultAsync(p => p.Id == refund.PaymentId, cancellationToken);
                if (payment == null)
                    continue;

                var provider = _paymentProviderFactory.GetProvider(payment.ChannelCode);
                if (provider == null)
                    continue;

                var query = await provider.QueryRefundAsync(refund.ExternalRefundNo!);
                if (!query.Succeeded || query.Data == null)
                    continue;

                if (query.Data.Status == RefundStatus.Refunding)
                    continue;

                await ApplyRefundStatusAsync(refund, payment, query.Data.Status, cancellationToken);
                settled++;
            }
            catch (Exception ex)
            {
                // 单笔对账失败不拖累整批
                Logger.LogError(ex, "Refund reconciliation failed. RefundNo: {RefundNo}", refund.RefundNo);
            }
        }

        if (settled > 0)
            Logger.LogInformation("Reconciled {Count} in-flight refunds", settled);

        return Ok(settled);
    }

    /// <summary>
    /// 落地渠道回报的退款状态，并在成功时回写支付状态、发布事件
    /// </summary>
    private async Task ApplyRefundStatusAsync(Refund refund, PaymentEntity payment, RefundStatus status, CancellationToken cancellationToken)
    {
        refund.Status = status;

        if (status == RefundStatus.Succeeded)
            refund.CompletedTime ??= DateTime.UtcNow;

        await _refundRepository.UpdateAsync(refund, cancellationToken);

        if (status != RefundStatus.Succeeded)
        {
            if (status is RefundStatus.Failed or RefundStatus.Cancelled)
            {
                Logger.LogWarning("Refund settled as {Status}. RefundNo: {RefundNo}", status, refund.RefundNo);
                await PublishRefundEventAsync(refund, payment, succeeded: false, $"Channel settled refund as {status}");
            }
            return;
        }

        // 回写支付状态：累计成功退款额达到已付额 → Refunded，否则 PartialRefunded
        await SyncPaymentRefundStatusAsync(payment, cancellationToken);
        await PublishRefundEventAsync(refund, payment, succeeded: true, null);

        Logger.LogInformation("Refund processed. RefundNo: {RefundNo}, Amount: {Amount}",
            refund.RefundNo, refund.RefundAmount);
    }

    /// <summary>
    /// 回写支付的退款状态：累计成功退款额 >= 已付额 → Refunded，否则 PartialRefunded
    /// </summary>
    private async Task SyncPaymentRefundStatusAsync(PaymentEntity payment, CancellationToken cancellationToken)
    {
        var totalRefunded = await _refundRepository.AsQueryable()
            .Where(r => r.PaymentId == payment.Id && r.Status == RefundStatus.Succeeded)
            .SumAsync(r => r.RefundAmount, cancellationToken);

        var newStatus = totalRefunded >= payment.PaidAmount
            ? PaymentStatus.Refunded
            : PaymentStatus.PartialRefunded;

        if (payment.Status != newStatus)
        {
            payment.Status = newStatus;
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }
    }

    private async Task PublishRefundEventAsync(Refund refund, PaymentEntity payment, bool succeeded, string? failReason)
    {
        if (EventBus == null)
            return;

        await EventBus.PublishAsync(new RefundProcessedEvent
        {
            RefundId = refund.Id,
            RefundNo = refund.RefundNo,
            PaymentId = payment.Id,
            TradeNo = payment.TradeNo,
            Amount = refund.RefundAmount,
            Currency = refund.Currency,
            CompletedTime = refund.CompletedTime ?? DateTime.UtcNow,
            Succeeded = succeeded,
            FailReason = failReason
        });
    }

    public async Task<Result> CancelRefundAsync(Guid refundId, string? reason, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var refund = await _refundRepository.FirstOrDefaultAsync(
            r => r.Id == refundId && (!ownerUserId.HasValue || r.Payment!.UserId == ownerUserId.Value), cancellationToken);
        if (refund == null)
            return Fail(ErrorCodes.RefundNotFound, 404);

        if (refund.Status != RefundStatus.Pending && refund.Status != RefundStatus.Approved)
            return Fail(ErrorCodes.RefundCannotCancel, 400);

        // CAS：避免与"审批通过后自动执行"竞态，把已在退款中的记录取消掉
        var affected = await _refundRepository.AsQueryable()
            .Where(r => r.Id == refundId
                && (r.Status == RefundStatus.Pending || r.Status == RefundStatus.Approved))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, RefundStatus.Cancelled), cancellationToken);

        if (affected == 0)
            return Fail(ErrorCodes.RefundCannotCancel, 409);

        Logger.LogInformation("Refund cancelled. RefundNo: {RefundNo}, Reason: {Reason}",
            refund.RefundNo, reason);

        return Ok();
    }

    public async Task<Result<RefundDto>> GetRefundAsync(Guid id, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var refund = await _refundRepository.FirstOrDefaultAsync(
            r => r.Id == id && (!ownerUserId.HasValue || r.Payment!.UserId == ownerUserId.Value), cancellationToken);
        if (refund == null)
            return Fail<RefundDto>(ErrorCodes.RefundNotFound, 404);

        return Ok(refund.MapTo<RefundDto>());
    }

    public async Task<Result<IPagedList<RefundDto>>> GetRefundListAsync(RefundQueryDto query, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var queryable = _refundRepository.AsNoTracking().Filter(query);

        if (ownerUserId.HasValue)
            queryable = queryable.Where(r => r.Payment!.UserId == ownerUserId.Value);

        var pagedList = await queryable
            .OrderByDescending(r => r.CreationTime)
            .ProjectTo<Refund, RefundDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        return Ok(pagedList);
    }

    public async Task<Result<List<RefundDto>>> GetRefundsByTradeNoAsync(string tradeNo, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var queryable = _refundRepository.AsNoTracking()
            .Where(r => r.Payment!.TradeNo == tradeNo);

        if (ownerUserId.HasValue)
            queryable = queryable.Where(r => r.Payment!.UserId == ownerUserId.Value);

        var refunds = await queryable
            .OrderByDescending(r => r.CreationTime)
            .ProjectTo<Refund, RefundDto>()
            .ToListAsync(cancellationToken);

        return Ok(refunds);
    }
}
