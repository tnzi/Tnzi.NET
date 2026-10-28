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
    private readonly IBillingAccountResolver _accountResolver;

    private const int ReconcileScanPageSize = 200;

    /// <summary>在途退款对账每轮最多翻的页数：一轮的渠道查询量有上界。闸门，不是调优项。</summary>
    private const int ReconcileScanMaxPages = 10;

    /// <summary>
    /// 仍然占着支付可退额度的退款状态。
    /// </summary>
    /// <remarks>
    /// 反过来说，<c>Rejected</c> / <c>Cancelled</c> / <c>Failed</c> 三个终态**不**占额度。
    /// 此前的写法是排除法且漏了 <c>Rejected</c>：一笔审批被拒的全额退款照样计入已退金额，
    /// 于是这笔支付**永久**变成不可退，而拒绝审批的人以为自己只是没批准这一次。
    /// 改成正列举，新增状态时必须显式决定它算不算占用。
    /// </remarks>
    private static readonly RefundStatus[] ActiveRefundStatuses =
    [
        RefundStatus.Pending,
        RefundStatus.Processing,
        RefundStatus.Approved,
        RefundStatus.Refunding,
        RefundStatus.Succeeded
    ];

    private PaymentOptions PaymentOptions => _paymentOptionsMonitor.CurrentValue;

    public RefundService(
        IRepository<Refund, Guid> refundRepository,
        IRepository<PaymentEntity, Guid> paymentRepository,
        IPaymentProviderFactory paymentProviderFactory,
        IOptionsMonitor<PaymentOptions> paymentOptionsMonitor,
        IBillingAccountResolver accountResolver,
        IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        _refundRepository = Check.NotNull(refundRepository);
        _paymentRepository = Check.NotNull(paymentRepository);
        _paymentProviderFactory = Check.NotNull(paymentProviderFactory);
        _paymentOptionsMonitor = Check.NotNull(paymentOptionsMonitor);
        _accountResolver = Check.NotNull(accountResolver);
    }

    public async Task<Result<RefundDto>> CreateRefundAsync(CreateRefundDto request, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        if (request.RefundAmount <= 0)
            return Fail<RefundDto>(ErrorCodes.PaymentInvalidAmount, 400);

        // 付款方自助退款可以整体关掉（按月收服务费的部署）：只挡用户面，客服代客退款（ownerUserId 为 null）不受影响
        if (ownerUserId.HasValue && !PaymentOptions.AllowCustomerRefundRequests)
            return Fail<RefundDto>(ErrorCodes.PaymentRefundRequestsDisabled, 403);

        var owner = await _accountResolver.ResolveScopeAsync(ownerUserId, manage: true, cancellationToken);
        if (!owner.Succeeded)
            return Fail<RefundDto>(owner.Message ?? ErrorCodes.BillingAccountAccessDenied, owner.Code ?? 403);

        var account = owner.Data;
        var payment = account == null
            ? await _paymentRepository.FirstOrDefaultAsync(p => p.TradeNo == request.TradeNo, cancellationToken)
            : await _paymentRepository.FirstOrDefaultAsync(
                p => p.TradeNo == request.TradeNo && p.BillingAccountType == account.Type && p.BillingAccountId == account.Id, cancellationToken);

        if (payment == null)
            return Fail<RefundDto>(ErrorCodes.PaymentNotFound, 404);

        // 已成功或部分退款的支付均可继续退款（剩余可退额由下方累计校验把关）；已全额退款则不可再退
        if (payment.Status != PaymentStatus.Succeeded && payment.Status != PaymentStatus.PartialRefunded)
            return Fail<RefundDto>(ErrorCodes.PaymentCannotRefund, 400);

        var options = PaymentOptions;

        // 事务保护：额度占用 + 退款记录创建原子操作，防止并发超退。
        // ★ 返回的 Fail 照样提交（ExecuteInUnitOfWorkAsync 只在抛异常时回滚）：占用之后的每一道会失败的校验
        // 都必须先把占用撤回，否则一次没发生的退款永久吃掉这笔支付的可退额度，此后谁来退都是「超出可退金额」。
        var refund = await ExecuteInUnitOfWorkAsync(async ct =>
        {
            // 裸 SQL 必须先强开事务：物理事务延迟到首次 SaveChanges 才 BEGIN，
            // 不开就跑在自动提交模式下 —— 回滚撤不掉这次占用，行锁也不持有到事务结束。
            await _paymentRepository.EnsureTransactionStartedAsync(ct);

            // 每日退款限额按币种统计：跨币种求和会把 100 JPY 和 100 USD 当成同一量级。
            // 排在占用之前：它是跨支付的软上限，不靠这一行的行锁串行，读已提交的行就够了；
            // 排在占用之后，它的拒绝会把占用一起提交掉。
            var todayStart = DateTime.UtcNow.Date;
            var todayEnd = todayStart.AddDays(1);
            var todayRefunds = await _refundRepository
                .Where(r => r.CreationTime >= todayStart && r.CreationTime < todayEnd
                            && r.Currency == payment.Currency
                            && ActiveRefundStatuses.Contains(r.Status))
                .SumAsync(r => r.RefundAmount, ct);

            if (todayRefunds + request.RefundAmount > options.MaxRefundAmountPerDay)
                return Fail<Refund>(ErrorCodes.RefundDailyLimitExceeded, 400);

            // ★ 额度占用：在支付这一行上做原子条件自增。这是并发超退唯一堵得住的地方 ——
            // 对退款表求和读不到别人未提交的行，两笔并发退款各自都会读到「还剩这么多」。
            // 第二笔在这条语句上等第一笔提交，然后对已提交的占用额重新求值。
            var reserved = await _paymentRepository.AsQueryable()
                .Where(p => p.Id == payment.Id
                    && p.ReservedRefundAmount + request.RefundAmount <= p.PaidAmount)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(x => x.ReservedRefundAmount, x => x.ReservedRefundAmount + request.RefundAmount),
                    ct);

            if (reserved == 0)
                return Fail<Refund>(ErrorCodes.PaymentRefundExceedAmount, 400);

            // 求和这一道**刻意保留**：既有部署迁移后 ReservedRefundAmount 从 0 起算，
            // 而退款表里可能已有历史行。没有它，那些支付会凭空多出一整份可退额度。
            // 它必须排在占用之后（由上面那条语句持有的行锁串行化，才看得到并发那一笔已提交的退款行），
            // 所以拒绝时在同一事务里把刚才的占用撤回。
            var existingRefundAmount = await _refundRepository
                .Where(r => r.PaymentId == payment.Id && ActiveRefundStatuses.Contains(r.Status))
                .SumAsync(r => r.RefundAmount, ct);

            if (request.RefundAmount > payment.PaidAmount - existingRefundAmount)
            {
                await _paymentRepository.AsQueryable()
                    .Where(p => p.Id == payment.Id)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(x => x.ReservedRefundAmount, x => x.ReservedRefundAmount - request.RefundAmount),
                        ct);
                return Fail<Refund>(ErrorCodes.PaymentRefundExceedAmount, 400);
            }

            // ★ 付款人自己发起的退款一律进审批，阈值只管客服代客退款（ownerUserId 为 null）。
            // 订阅、开票与业务订单对退款都没有反应：阈值以下当场执行，等于让每个付款人对自己的支付
            // 单方面即时退款而服务照用，订阅每一期都能重来一次。宿主显式关掉审批时尊重它的选择。
            var needsApproval = options.EnableRefundApproval
                && (ownerUserId.HasValue || request.RefundAmount >= options.RefundApprovalThreshold);

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

        // TradeNo 经 Refund.Payment 导航映射，而这条新退款上没有挂导航（支付是游离读出来的）：显式回填
        var dto = refund.Data.MapTo<RefundDto>();
        dto.TradeNo = payment.TradeNo;
        return Ok(dto);
    }

    public async Task<Result> ApproveRefundAsync(Guid refundId, ApproveRefundDto request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        var refund = await _refundRepository.FirstOrDefaultAsync(r => r.Id == refundId, cancellationToken);
        if (refund == null)
            return Fail(ErrorCodes.RefundNotFound, 404);

        if (refund.Status != RefundStatus.Pending)
            return Fail(ErrorCodes.RefundCannotApprove, 400);

        var approverId = GetRequiredCurrentUser().Id;
        var approveTime = DateTime.UtcNow;
        var approveRemark = request.Remark;
        var decided = request.Approved ? RefundStatus.Approved : RefundStatus.Rejected;

        // CAS：审批与取消（或另一位审批人）争的是同一笔待审退款。此前读出再整行写回：
        // 取消在两者之间落地（额度已归还）时，审批把它改回「已批准」，随后执行一笔已经取消的渠道退款。
        var affected = await _refundRepository.AsQueryable()
            .Where(r => r.Id == refundId && r.Status == RefundStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, decided)
                .SetProperty(r => r.ApproverId, approverId)
                .SetProperty(r => r.ApproveTime, approveTime)
                .SetProperty(r => r.ApproveRemark, approveRemark), cancellationToken);

        if (affected == 0)
            return Fail(ErrorCodes.RefundCannotApprove, 409);

        refund.Status = decided;
        refund.ApproverId = approverId;
        refund.ApproveTime = approveTime;
        refund.ApproveRemark = approveRemark;

        Logger.LogInformation("Refund {Status}. RefundNo: {RefundNo}, Approver: {Approver}",
            request.Approved ? "Approved" : "Rejected", refund.RefundNo, refund.ApproverId);

        if (request.Approved)
            return await ProcessRefundAsync(refundId, cancellationToken);

        // 拒绝审批 = 这笔退款不会再发生 → 把它占用的可退额度还给支付
        await ReleaseRefundReservationAsync(refund.PaymentId, cancellationToken);
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
            await ReleaseRefundReservationAsync(payment.Id, cancellationToken);
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

        // ★ 按 (CreationTime, Id) 游标往后翻，而不是每轮只取「最老的前 N 条」：渠道仍在处理的退款（银行卡退回常需数日）
        // 与渠道包已卸载的退款每轮原样留下，只取第一页时它们一直占满这一页，之后发起的退款永远对不上账。
        var inFlightQuery = _refundRepository
            .Where(r => r.Status == RefundStatus.Refunding
                && r.ExternalRefundNo != null
                && r.CreationTime >= since);

        var settled = 0;
        Refund? cursor = null;

        for (var page = 0; page < ReconcileScanMaxPages; page++)
        {
            var pageQuery = inFlightQuery;
            if (cursor != null)
            {
                var afterTime = cursor.CreationTime;
                var afterId = cursor.Id;
                pageQuery = pageQuery.Where(r => r.CreationTime > afterTime
                    || (r.CreationTime == afterTime && r.Id.CompareTo(afterId) > 0));
            }

            var pending = await pageQuery
                .OrderBy(r => r.CreationTime)
                .ThenBy(r => r.Id)
                .Take(ReconcileScanPageSize)
                .ToListAsync(cancellationToken);

            foreach (var refund in pending)
            {
                if (await TryReconcileRefundAsync(refund, cancellationToken))
                    settled++;
            }

            if (pending.Count < ReconcileScanPageSize)
                break;

            cursor = pending[^1];
        }

        if (settled > 0)
            Logger.LogInformation("Reconciled {Count} in-flight refunds", settled);

        return Ok(settled);
    }

    /// <summary>向渠道查询一笔在途退款，渠道已落定的就地落定。</summary>
    /// <returns>这一次是否把它推进到了终态；渠道仍在处理、查询失败、已被别处落定或处理出错时为 false。</returns>
    private async Task<bool> TryReconcileRefundAsync(Refund refund, CancellationToken cancellationToken)
    {
        try
        {
            var payment = await _paymentRepository.FirstOrDefaultAsync(p => p.Id == refund.PaymentId, cancellationToken);
            if (payment == null)
                return false;

            var provider = _paymentProviderFactory.GetProvider(payment.ChannelCode);
            if (provider == null)
                return false;

            var query = await provider.QueryRefundAsync(refund.ExternalRefundNo!);
            if (!query.Succeeded || query.Data == null)
                return false;

            if (query.Data.Status == RefundStatus.Refunding)
                return false;

            return await ApplyRefundStatusAsync(refund, payment, query.Data.Status, cancellationToken);
        }
        catch (Exception ex)
        {
            // 单笔对账失败不拖累整批
            Logger.LogError(ex, "Refund reconciliation failed. RefundNo: {RefundNo}", refund.RefundNo);
            return false;
        }
    }

    /// <summary>
    /// 落地渠道回报的退款状态，并在落到终态时回写支付状态 / 归还额度、发布事件
    /// </summary>
    /// <remarks>
    /// ★ 条件写：只有仍是 <c>Refunding</c> 的那一行由本次落定。后台扫描没有跨实例锁，两个实例可能对账同一笔退款；
    /// 此前整行写回、无条件发事件，同一笔退款发两次 <see cref="RefundProcessedEvent"/>。
    /// 落定权被别处拿走时什么都不做（回写、事件都是那一方的事）。
    /// </remarks>
    /// <returns>这一次是否把它推进到了终态。</returns>
    private async Task<bool> ApplyRefundStatusAsync(Refund refund, PaymentEntity payment, RefundStatus status, CancellationToken cancellationToken)
    {
        var completedTime = status == RefundStatus.Succeeded
            ? refund.CompletedTime ?? DateTime.UtcNow
            : refund.CompletedTime;
        var externalRefundNo = refund.ExternalRefundNo;

        var affected = await _refundRepository.AsQueryable()
            .Where(r => r.Id == refund.Id && r.Status == RefundStatus.Refunding)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, status)
                .SetProperty(r => r.CompletedTime, completedTime)
                .SetProperty(r => r.ExternalRefundNo, externalRefundNo), cancellationToken);

        if (affected == 0)
        {
            Logger.LogInformation("Refund {RefundNo} was settled elsewhere before this {Status} could be applied; nothing to do.",
                refund.RefundNo, status);
            return false;
        }

        refund.Status = status;
        refund.CompletedTime = completedTime;

        if (status != RefundStatus.Succeeded)
        {
            if (status is RefundStatus.Failed or RefundStatus.Cancelled)
            {
                await ReleaseRefundReservationAsync(payment.Id, cancellationToken);
                Logger.LogWarning("Refund settled as {Status}. RefundNo: {RefundNo}", status, refund.RefundNo);
                await PublishRefundEventAsync(refund, payment, succeeded: false, $"Channel settled refund as {status}");
                return true;
            }

            // 仍在途（渠道受理了、结果要等对账）：只记下渠道退款号
            return false;
        }

        // 回写支付状态：累计成功退款额达到已付额 → Refunded，否则 PartialRefunded
        await SyncPaymentRefundStatusAsync(payment, cancellationToken);
        await PublishRefundEventAsync(refund, payment, succeeded: true, null);

        Logger.LogInformation("Refund processed. RefundNo: {RefundNo}, Amount: {Amount}",
            refund.RefundNo, refund.RefundAmount);
        return true;
    }

    /// <summary>
    /// 重算支付上被退款单占用的额度。
    /// </summary>
    /// <remarks>
    /// 刻意是**重算**而不是「减掉这一笔」：同一笔退款可能被重复判为终态
    /// （渠道重投、对账扫描与执行路径同时收到结果），做减法会把额度还两次，
    /// 从此这笔支付可以退出超过它收到的钱。重算按定义幂等，重复调用不会出错。
    /// </remarks>
    private async Task ReleaseRefundReservationAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        var active = await _refundRepository.AsQueryable()
            .Where(r => r.PaymentId == paymentId && ActiveRefundStatuses.Contains(r.Status))
            .SumAsync(r => r.RefundAmount, cancellationToken);

        await _paymentRepository.AsQueryable()
            .Where(p => p.Id == paymentId && p.ReservedRefundAmount != active)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReservedRefundAmount, active), cancellationToken);
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
            // ★ 只写状态这一列。这份支付读在渠道退款请求之前（建单路径里甚至读在本笔额度占用之前）：
            // 此前整行写回，ReservedRefundAmount 被拨回读时的值（第一笔退款落定之后归零），
            // 支付行上的额度占用这道并发超退防线就此失效。已全额退款的不再退回部分退款（两笔并发落定时后写的那笔可能算少了）。
            await _paymentRepository.AsQueryable()
                .Where(p => p.Id == payment.Id && p.Status != PaymentStatus.Refunded)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, newStatus), cancellationToken);
            payment.Status = newStatus;
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

    public async Task<Result<int>> RecordChannelRefundsAsync(Guid paymentId, IReadOnlyList<PaymentProviderExternalRefund> refunds, CancellationToken cancellationToken = default)
    {
        Check.NotNull(refunds);

        var payment = await _paymentRepository.FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken);
        if (payment == null)
            return Fail<int>(ErrorCodes.PaymentNotFound, 404);

        var changed = 0;
        foreach (var external in refunds)
        {
            if (string.IsNullOrWhiteSpace(external.ExternalRefundNo))
                continue;

            if (!string.IsNullOrWhiteSpace(external.LocalRefundNo))
            {
                // 本模块发起的：只推进它自己的状态（还在 Refunding 才推进，CAS 保证与执行路径、对账扫描只落定一次）。
                // 找不到不新建：那一行可能还没提交（渠道回调比本地事务先到），新建就成了同一笔钱的两条退款
                var own = await _refundRepository.FirstOrDefaultAsync(r => r.RefundNo == external.LocalRefundNo && r.PaymentId == paymentId, cancellationToken);
                if (own != null && own.Status == RefundStatus.Refunding && external.Status != RefundStatus.Refunding)
                {
                    own.ExternalRefundNo ??= external.ExternalRefundNo;
                    if (await ApplyRefundStatusAsync(own, payment, external.Status, cancellationToken))
                        changed++;
                }
                continue;
            }

            var existing = await _refundRepository.FirstOrDefaultAsync(r => r.ExternalRefundNo == external.ExternalRefundNo, cancellationToken);
            if (existing != null)
            {
                // 同一笔渠道退款的后续状态（pending → succeeded / failed）
                if (existing.Status == RefundStatus.Refunding && external.Status != RefundStatus.Refunding
                    && await ApplyRefundStatusAsync(existing, payment, external.Status, cancellationToken))
                    changed++;
                continue;
            }

            if (await TryRecordChannelRefundAsync(payment, external, cancellationToken))
                changed++;
        }

        return Ok(changed);
    }

    /// <summary>
    /// 新记一条渠道后台手工退的退款。不走审批、不占每日限额（钱已经退了，本地只是如实记账）；
    /// 额度占用按仍在占用的退款行重算。并发的两次投递撞唯一索引时，输的那一边什么都不做。
    /// </summary>
    private async Task<bool> TryRecordChannelRefundAsync(PaymentEntity payment, PaymentProviderExternalRefund external, CancellationToken cancellationToken)
    {
        var status = external.Status is RefundStatus.Succeeded or RefundStatus.Failed or RefundStatus.Cancelled
            ? external.Status
            : RefundStatus.Refunding;
        var reason = string.IsNullOrWhiteSpace(external.Reason)
            ? "Refunded in the payment channel"
            : $"Refunded in the payment channel ({external.Reason})";

        var refund = new Refund
        {
            RefundNo = Refund.GenerateRefundNo(),
            PaymentId = payment.Id,
            RefundAmount = external.Amount,
            Currency = string.IsNullOrWhiteSpace(external.Currency) ? payment.Currency : external.Currency.ToUpperInvariant(),
            Reason = reason.Length > 500 ? reason[..500] : reason,
            RefundType = external.Amount >= payment.PaidAmount ? RefundType.Full : RefundType.Partial,
            Status = status,
            ExternalRefundNo = external.ExternalRefundNo,
            Origin = RefundOrigin.Channel,
            CompletedTime = status == RefundStatus.Succeeded ? DateTime.UtcNow : null,
            BusinessOrderNo = payment.BusinessOrderNo
        };

        await _refundRepository.InsertAsync(refund, cancellationToken);
        try
        {
            // 当场刷出去，让唯一索引的冲突在这里浮出来（两次并发投递同一笔渠道退款）
            await FlushAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            _refundRepository.Discard(refund);
            Logger.LogInformation("Channel refund {ExternalRefundNo} was recorded by a concurrent delivery; nothing to do.", external.ExternalRefundNo);
            return false;
        }

        await ReleaseRefundReservationAsync(payment.Id, cancellationToken);

        Logger.LogWarning("Refund {ExternalRefundNo} of {Amount} {Currency} was made in the payment channel for {TradeNo}; recorded as {RefundNo} ({Status}).",
            external.ExternalRefundNo, refund.RefundAmount, refund.Currency, payment.TradeNo, refund.RefundNo, status);

        if (status == RefundStatus.Succeeded)
        {
            await SyncPaymentRefundStatusAsync(payment, cancellationToken);
            await PublishRefundEventAsync(refund, payment, succeeded: true, null);
        }

        return true;
    }

    public async Task<Result> CancelRefundAsync(Guid refundId, string? reason, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var owner = await _accountResolver.ResolveScopeAsync(ownerUserId, manage: true, cancellationToken);
        if (!owner.Succeeded)
            return Fail(owner.Message ?? ErrorCodes.BillingAccountAccessDenied, owner.Code ?? 403);

        var refund = await OwnedBy(_refundRepository.AsQueryable(), owner.Data)
            .FirstOrDefaultAsync(r => r.Id == refundId, cancellationToken);
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

        // 取消 = 这笔退款不会再发生 → 把它占用的可退额度还给支付
        await ReleaseRefundReservationAsync(refund.PaymentId, cancellationToken);

        Logger.LogInformation("Refund cancelled. RefundNo: {RefundNo}, Reason: {Reason}",
            refund.RefundNo, reason);

        return Ok();
    }

    public async Task<Result<RefundDto>> GetRefundAsync(Guid id, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var owner = await _accountResolver.ResolveScopeAsync(ownerUserId, manage: false, cancellationToken);
        if (!owner.Succeeded)
            return Fail<RefundDto>(owner.Message ?? ErrorCodes.BillingAccountAccessDenied, owner.Code ?? 403);

        // 与列表同一条 SQL 投影：TradeNo 经 Refund.Payment 导航映射，映射一个没加载导航的实体得到的是 null
        var refund = await OwnedBy(_refundRepository.AsNoTracking(), owner.Data)
            .Where(r => r.Id == id)
            .ProjectTo<Refund, RefundDto>()
            .FirstOrDefaultAsync(cancellationToken);
        if (refund == null)
            return Fail<RefundDto>(ErrorCodes.RefundNotFound, 404);

        return Ok(refund);
    }

    public async Task<Result<IPagedList<RefundDto>>> GetRefundListAsync(RefundQueryDto query, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var owner = await _accountResolver.ResolveScopeAsync(ownerUserId, manage: false, cancellationToken);
        if (!owner.Succeeded)
            return Fail<IPagedList<RefundDto>>(owner.Message ?? ErrorCodes.BillingAccountAccessDenied, owner.Code ?? 403);

        var queryable = OwnedBy(_refundRepository.AsNoTracking().Filter(query), owner.Data);

        var pagedList = await queryable
            .OrderByDescending(r => r.CreationTime)
            .ProjectTo<Refund, RefundDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        return Ok(pagedList);
    }

    public async Task<Result<List<RefundDto>>> GetRefundsByTradeNoAsync(string tradeNo, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var owner = await _accountResolver.ResolveScopeAsync(ownerUserId, manage: false, cancellationToken);
        if (!owner.Succeeded)
            return Fail<List<RefundDto>>(owner.Message ?? ErrorCodes.BillingAccountAccessDenied, owner.Code ?? 403);

        var queryable = OwnedBy(_refundRepository.AsNoTracking(), owner.Data)
            .Where(r => r.Payment!.TradeNo == tradeNo);

        var refunds = await queryable
            .OrderByDescending(r => r.CreationTime)
            .ProjectTo<Refund, RefundDto>()
            .ToListAsync(cancellationToken);

        return Ok(refunds);
    }

    /// <summary>只留记在 <paramref name="owner"/> 账户上的支付的退款；为 null 时不限定（管理端）。</summary>
    private static IQueryable<Refund> OwnedBy(IQueryable<Refund> query, BillingAccountRef? owner)
        => owner == null
            ? query
            : query.Where(r => r.Payment!.BillingAccountType == owner.Type && r.Payment.BillingAccountId == owner.Id);
}
