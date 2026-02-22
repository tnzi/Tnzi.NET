namespace Tnzi.Payment.Services;

/// <summary>
/// 退款服务实现
/// </summary>
public class RefundService : ApplicationService, IRefundService
{
    private readonly IRepository<Refund, Guid> _refundRepository;
    private readonly IRepository<PaymentEntity, Guid> _paymentRepository;
    private readonly IPaymentProviderFactory _paymentProviderFactory;
    private readonly IOptions<PaymentOptions> _paymentOptions;

    public RefundService(
        IRepository<Refund, Guid> refundRepository,
        IRepository<PaymentEntity, Guid> paymentRepository,
        IPaymentProviderFactory paymentProviderFactory,
        IOptions<PaymentOptions> paymentOptions,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _refundRepository = Check.NotNull(refundRepository);
        _paymentRepository = Check.NotNull(paymentRepository);
        _paymentProviderFactory = Check.NotNull(paymentProviderFactory);
        _paymentOptions = Check.NotNull(paymentOptions);
    }

    public async Task<Result<RefundDto>> CreateRefundAsync(CreateRefundDto request, CancellationToken cancellationToken = default)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(
            p => p.TradeNo == request.TradeNo, cancellationToken);

        if (payment == null)
            return Fail<RefundDto>(ErrorCodes.PaymentNotFound, 404);

        if (payment.Status != PaymentStatus.Succeeded)
            return Fail<RefundDto>(ErrorCodes.PaymentCannotRefund, 400);

        // 校验退款金额不超过已付金额减去已退款金额
        var existingRefundAmount = await _refundRepository.AsNoTracking()
            .Where(r => r.PaymentId == payment.Id && r.Status != RefundStatus.Cancelled && r.Status != RefundStatus.Failed)
            .SumAsync(r => r.RefundAmount, cancellationToken);

        if (request.RefundAmount > payment.PaidAmount - existingRefundAmount)
            return Fail<RefundDto>(ErrorCodes.PaymentRefundExceedAmount, 400);

        // 检查每日退款限额
        var options = _paymentOptions.Value;
        var todayRefunds = await _refundRepository.AsNoTracking()
            .Where(r => r.CreationTime.Date == DateTime.UtcNow.Date &&
                        r.Status != RefundStatus.Cancelled && r.Status != RefundStatus.Failed)
            .SumAsync(r => r.RefundAmount, cancellationToken);

        if (todayRefunds + request.RefundAmount > options.MaxRefundAmountPerDay)
            return Fail<RefundDto>(ErrorCodes.RefundDailyLimitExceeded, 400);

        bool needsApproval = options.EnableRefundApproval && request.RefundAmount >= options.RefundApprovalThreshold;

        var refund = new Refund
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

        await _refundRepository.InsertAsync(refund, cancellationToken);

        Logger.LogInformation("Refund created. TradeNo: {TradeNo}, RefundNo: {RefundNo}, Amount: {Amount}, Status: {Status}",
            request.TradeNo, refund.RefundNo, request.RefundAmount, refund.Status);

        // 不需要审批，直接执行退款
        if (!needsApproval)
        {
            var processResult = await ProcessRefundInternalAsync(refund, payment, cancellationToken);
            if (!processResult.Succeeded)
                return Fail<RefundDto>(processResult.Message ?? ErrorCodes.StripeRefundFailed);
        }

        return Ok(refund.MapTo<RefundDto>());
    }

    public async Task<Result> ApproveRefundAsync(Guid refundId, ApproveRefundDto request, CancellationToken cancellationToken = default)
    {
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

        // 标记为退款中
        if (refund.Status != RefundStatus.Refunding)
        {
            refund.Status = RefundStatus.Refunding;
            await _refundRepository.UpdateAsync(refund, cancellationToken);
        }

        var result = await provider.RefundAsync(new PaymentProviderRefundDto
        {
            TradeNo = payment.TradeNo,
            RefundNo = refund.RefundNo,
            RefundAmount = refund.RefundAmount,
            Reason = refund.Reason
        });

        if (!result.Succeeded)
        {
            refund.Status = RefundStatus.Failed;
            await _refundRepository.UpdateAsync(refund, cancellationToken);
            return Fail(result.Message ?? ErrorCodes.StripeRefundFailed);
        }

        refund.Status = RefundStatus.Succeeded;
        refund.ExternalRefundNo = result.Data?.ExternalRefundNo;
        refund.CompletedTime = DateTime.UtcNow;
        await _refundRepository.UpdateAsync(refund, cancellationToken);

        // 发布退款完成事件
        if (EventBus != null)
        {
            await EventBus.PublishAsync(new RefundProcessedEvent
            {
                RefundId = refund.Id,
                RefundNo = refund.RefundNo,
                PaymentId = payment.Id,
                TradeNo = payment.TradeNo,
                Amount = refund.RefundAmount,
                Currency = refund.Currency,
                CompletedTime = refund.CompletedTime.Value,
                Succeeded = true
            });
        }

        Logger.LogInformation("Refund processed. RefundNo: {RefundNo}, Amount: {Amount}",
            refund.RefundNo, refund.RefundAmount);

        return Ok();
    }

    public async Task<Result> CancelRefundAsync(Guid refundId, string? reason, CancellationToken cancellationToken = default)
    {
        var refund = await _refundRepository.FirstOrDefaultAsync(r => r.Id == refundId, cancellationToken);
        if (refund == null)
            return Fail(ErrorCodes.RefundNotFound, 404);

        if (refund.Status != RefundStatus.Pending && refund.Status != RefundStatus.Approved)
            return Fail(ErrorCodes.RefundCannotCancel, 400);

        refund.Status = RefundStatus.Cancelled;
        await _refundRepository.UpdateAsync(refund, cancellationToken);

        Logger.LogInformation("Refund cancelled. RefundNo: {RefundNo}, Reason: {Reason}",
            refund.RefundNo, reason);

        return Ok();
    }

    public async Task<Result<RefundDto>> GetRefundAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var refund = await _refundRepository.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (refund == null)
            return Fail<RefundDto>(ErrorCodes.RefundNotFound, 404);

        return Ok(refund.MapTo<RefundDto>());
    }

    public async Task<Result<IPagedList<RefundDto>>> GetRefundListAsync(RefundQueryDto query, CancellationToken cancellationToken = default)
    {
        var pagedList = await _refundRepository.AsNoTracking()
            .Filter(query)
            .OrderByDescending(r => r.CreationTime)
            .ProjectTo<Refund, RefundDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        return Ok(pagedList);
    }

    public async Task<Result<List<RefundDto>>> GetRefundsByTradeNoAsync(string tradeNo, CancellationToken cancellationToken = default)
    {
        var refunds = await _refundRepository.AsNoTracking()
            .Where(r => r.Payment!.TradeNo == tradeNo)
            .OrderByDescending(r => r.CreationTime)
            .ProjectTo<Refund, RefundDto>()
            .ToListAsync(cancellationToken);

        return Ok(refunds);
    }
}
