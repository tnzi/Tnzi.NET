namespace Tnzi.Payment.Subscriptions.Services;

/// <summary>
/// 订阅服务实现：生命周期（创建 / 取消 / 暂停 / 恢复）与计划管理。
/// 计划变更与按比例计费见 SubscriptionService.PlanChange.cs；
/// 计费引擎（扣款、状态机回流、后台扫描）见 SubscriptionService.Billing.cs。
/// </summary>
public partial class SubscriptionService : ApplicationService, ISubscriptionService
{
    private readonly IRepository<Subscription, Guid> _subscriptionRepository;
    private readonly IRepository<SubscriptionPlan, Guid> _planRepository;
    private readonly IRepository<SubscriptionChange, Guid> _changeRepository;
    private readonly IPaymentService _paymentService;
    private readonly IPaymentProviderFactory _paymentProviderFactory;
    private readonly IPaymentMethodService _paymentMethodService;
    private readonly IOptionsMonitor<PaymentOptions> _paymentOptionsMonitor;
    private readonly IOptionsMonitor<SubscriptionOptions> _subscriptionOptionsMonitor;
    private readonly IBillingAccountResolver _accountResolver;
    private readonly IBillingAccountService _billingAccountService;
    private readonly ICouponService? _couponService;
    private readonly INotificationService? _notificationService;
    private readonly IPaymentTaxCalculator? _taxCalculator;
    private readonly ISubscriptionStorageMeter? _storageMeter;

    private const int BillingScanPageSize = 200;

    private PaymentOptions PaymentOptions => _paymentOptionsMonitor.CurrentValue;

    /// <summary>
    /// 订阅侧配置。拆分前它是 <c>PaymentOptions.Subscription</c> 这条嵌套属性，
    /// 现在由本模块自己绑同一个绝对节 <c>Payment:Subscription</c>，单独注入。
    /// 配置 JSON 的形状与热更新语义都不变（仍是 <c>IOptionsMonitor</c> 的 <c>CurrentValue</c>）。
    /// </summary>
    private SubscriptionOptions SubscriptionOptions => _subscriptionOptionsMonitor.CurrentValue;

    public SubscriptionService(
        IRepository<Subscription, Guid> subscriptionRepository,
        IRepository<SubscriptionPlan, Guid> planRepository,
        IRepository<SubscriptionChange, Guid> changeRepository,
        IPaymentService paymentService,
        IPaymentProviderFactory paymentProviderFactory,
        IPaymentMethodService paymentMethodService,
        IOptionsMonitor<PaymentOptions> paymentOptionsMonitor,
        IOptionsMonitor<SubscriptionOptions> subscriptionOptionsMonitor,
        IBillingAccountResolver accountResolver,
        IBillingAccountService billingAccountService,
        IServiceProvider serviceProvider,
        ICouponService? couponService = null,
        INotificationService? notificationService = null,
        IPaymentTaxCalculator? taxCalculator = null,
        ISubscriptionStorageMeter? storageMeter = null)
        : base(serviceProvider)
    {
        _subscriptionRepository = Check.NotNull(subscriptionRepository);
        _planRepository = Check.NotNull(planRepository);
        _changeRepository = Check.NotNull(changeRepository);
        _paymentService = Check.NotNull(paymentService);
        _paymentProviderFactory = Check.NotNull(paymentProviderFactory);
        _paymentMethodService = Check.NotNull(paymentMethodService);
        _paymentOptionsMonitor = Check.NotNull(paymentOptionsMonitor);
        _subscriptionOptionsMonitor = Check.NotNull(subscriptionOptionsMonitor);
        _accountResolver = Check.NotNull(accountResolver);
        _billingAccountService = Check.NotNull(billingAccountService);
        _couponService = couponService;
        _notificationService = notificationService;
        _taxCalculator = taxCalculator;
        _storageMeter = storageMeter;
    }

    public async Task<Result<SubscriptionCreateResultDto>> CreateSubscriptionAsync(CreateSubscriptionDto request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        var currentUser = GetRequiredCurrentUser();
        var userId = currentUser.Id!.Value;

        // 订阅记在经办人所代表的计费账户上；开通要能管理那个账户（User 范围下恒成立）
        var scope = await _accountResolver.ResolveScopeAsync(userId, manage: true, cancellationToken);
        if (!scope.Succeeded || scope.Data == null)
            return Fail<SubscriptionCreateResultDto>(scope.Message ?? ErrorCodes.BillingAccountAccessDenied, scope.Code ?? 403);
        var account = scope.Data;

        var plan = await _planRepository.FirstOrDefaultAsync(p => p.Id == request.PlanId, cancellationToken);
        if (plan == null)
            return Fail<SubscriptionCreateResultDto>(ErrorCodes.SubscriptionPlanNotFound, 404);

        if (!plan.IsActive)
            return Fail<SubscriptionCreateResultDto>(ErrorCodes.SubscriptionPlanNotActive, 400);

        // 判重按"账户 + 产品"：不同产品的订阅可以并存；同一个机构的两位管理者各点一次开通，得到的是同一条订阅
        var existingSubscription = await _subscriptionRepository.FirstOrDefaultAsync(
            s => s.BillingAccountType == account.Type
                && s.BillingAccountId == account.Id
                && s.ProductCode == plan.ProductCode
                && s.Status != SubscriptionStatus.Cancelled
                && s.Status != SubscriptionStatus.Expired,
            cancellationToken);

        if (existingSubscription != null && !await TryEndAbandonedPendingAsync(existingSubscription, cancellationToken))
            return DuplicateSubscription(plan);

        var requestedChannel = string.IsNullOrWhiteSpace(request.ChannelCode)
            ? PaymentOptions.DefaultChannelCode
            : request.ChannelCode;

        // 存渠道自己的写法而不是请求原样：渠道查找不区分大小写，而支付方式、默认卡与撤卡回调按存下的值等值比较，
        // 存成 "stripe" 的订阅在区分大小写的数据库上找不到 "Stripe" 渠道下的默认卡。
        var channelCode = _paymentProviderFactory.GetProvider(requestedChannel)?.ChannelCode ?? requestedChannel;

        // 账单联系人：账户填了资料用资料，否则用经办人自己（与此前相同）
        var contact = await _billingAccountService.ResolveContactAsync(account, currentUser.UserName, currentUser.Email, userId, cancellationToken);

        var subscription = new Subscription
        {
            SubscriptionNo = Subscription.GenerateSubscriptionNo(),
            UserId = userId,
            BillingAccountType = account.Type,
            BillingAccountId = account.Id,
            CustomerName = contact.Name,
            CustomerEmail = contact.PrimaryEmail,
            ProductCode = plan.ProductCode,
            PlanId = plan.Id,
            Status = SubscriptionStatus.Pending,
            CycleType = plan.CycleType,
            CycleValue = plan.CycleValue,
            StartTime = DateTime.UtcNow,
            EndTime = null,
            OriginalPrice = plan.Price,
            PaidAmount = 0,
            DiscountAmount = 0,
            Currency = plan.Currency,
            ChannelCode = channelCode,
            AutoRenew = true
            // ★ 刻意不设 Plan 导航：仓储读出来的实体是游离态（no-tracking），
            // 把它挂到待保存的实体上会让 EF 把它当成**新计划**一并 INSERT，
            // 直接撞 PlanCode 唯一索引。计划名单独回填到 DTO（见下方 ToDto）。
        };

        // 绑定支付方式：没有它，自动续费/试用转正必然失败并降级 PastDue
        var bindResult = await ResolvePaymentMethodAsync(account, userId, channelCode, request.PaymentMethodId, request.PaymentMethodToken, contact, cancellationToken);
        if (!bindResult.Succeeded)
            return Fail<SubscriptionCreateResultDto>(bindResult.Message ?? ErrorCodes.PaymentMethodBindingFailed, bindResult.Code ?? 400);

        ApplyPaymentMethod(subscription, bindResult.Data);

        var startsTrial = ShouldStartTrial(request, plan);

        // 试用期不产生首期支付，优惠券无处可核销。此时**明确拒绝**而不是静默忽略：
        // 悄悄丢掉用户输入的促销码，用户会以为自己拿到了折扣，直到转正扣款才发现没有。
        if (startsTrial && !string.IsNullOrWhiteSpace(request.CouponCode))
            return Fail<SubscriptionCreateResultDto>(ErrorCodes.CouponNotApplicableToTrial, 400);

        // 优惠券作用于首期价格
        var couponResult = await PreviewSubscriptionCouponAsync(request.CouponCode, userId, subscription, plan, cancellationToken);
        if (!couponResult.Succeeded)
            return Fail<SubscriptionCreateResultDto>(couponResult.Message ?? ErrorCodes.CouponInvalid, couponResult.Code ?? 400);

        var couponDiscount = couponResult.Data?.DiscountAmount ?? 0;

        PaymentOrderResultDto? paymentOrder = null;

        if (startsTrial)
        {
            var trialDays = plan.TrialDays > 0 ? plan.TrialDays : SubscriptionOptions.DefaultTrialDays;

            subscription.Status = SubscriptionStatus.Trial;
            subscription.TrialStartTime = DateTime.UtcNow;
            subscription.TrialEndTime = DateTime.UtcNow.AddDays(trialDays);
            subscription.DiscountAmount = plan.TrialDiscount ?? 0;
            subscription.PaidAmount = 0;
            // 试用期结束后的首次计费时间
            subscription.NextBillingTime = subscription.TrialEndTime;

            if (!await TryInsertSubscriptionAsync(subscription, plan, cancellationToken))
                return DuplicateSubscription(plan);

            await PublishStandingTransitionAsync(subscription, SubscriptionStatus.Pending, null, cancellationToken);
        }
        else
        {
            subscription.DiscountAmount = couponDiscount;
            subscription.CouponId = couponResult.Data?.PromotionId;
            // ★ NextBillingTime 刻意留空：它是「已付到何时」，而此刻一分钱都还没收。首付回流
            // （ApplyPaymentCompletedAsync 的 Initial 分支）从付款那一刻起算一个周期。此前这里预写了
            // 「现在 + 一个周期」，取消再恢复的路径读到一个在未来的「已付到」，于是不扣款直接回 Active。

            // 先落库拿到订阅ID，支付单的 BusinessOrderNo 用订阅号，回流时凭它找回订阅
            if (!await TryInsertSubscriptionAsync(subscription, plan, cancellationToken))
                return DuplicateSubscription(plan);

            // 创建首次支付：订阅保持 Pending，待支付完成事件回流后才激活（见 ApplyPaymentCompletedAsync）
            var paymentResult = await _paymentService.CreatePaymentAsync(new CreatePaymentDto
            {
                BusinessOrderNo = subscription.SubscriptionNo,
                BusinessType = BusinessType.Subscription,
                Amount = plan.Price,
                Currency = plan.Currency,
                ChannelCode = channelCode,
                CouponCode = request.CouponCode,
                // 与上面的试算同源：限定计划的促销靠它在核销时通过范围校验
                CouponScopeId = plan.Id,
                Description = $"Subscription: {plan.PlanName}",
                ExtraData = new SubscriptionBillingMetadata
                {
                    Purpose = SubscriptionBillingPurpose.Initial,
                    SubscriptionId = subscription.Id,
                    // 首期从付款起算；激活发生在回流时，与这里差的是付款那几分钟
                    PeriodStart = DateTime.UtcNow,
                    PeriodEnd = CalculateNextBillingTime(DateTime.UtcNow, plan.CycleType, plan.CycleValue),
                    PlanName = plan.PlanName
                }.ToExtraData(),
                IsSystemInitiated = true,
                BillingAccount = account,
                // ★ 还没有可扣的卡：首付这一次（有人在场，能做 3-D Secure）就把卡留下来，否则第一次续费必然无卡可扣
                SavePaymentMethod = subscription.StoredPaymentMethodId == null
            }, cancellationToken);

            if (!paymentResult.Succeeded || paymentResult.Data == null)
            {
                // 首单建不出来就不该留下一条永远激活不了的订阅
                await _subscriptionRepository.DeleteAsync(subscription, cancellationToken);
                return Fail<SubscriptionCreateResultDto>(paymentResult.Message ?? ErrorCodes.PaymentCreationFailed);
            }

            paymentOrder = paymentResult.Data;
        }

        // 发布订阅创建事件
        if (EventBus != null)
        {
            await EventBus.PublishAsync(new SubscriptionCreatedEvent
            {
                SubscriptionId = subscription.Id,
                SubscriptionNo = subscription.SubscriptionNo,
                UserId = userId,
                PlanId = plan.Id,
                PlanName = plan.PlanName,
                StartTime = subscription.StartTime,
                EndTime = subscription.EndTime,
                IsTrial = subscription.Status == SubscriptionStatus.Trial,
                TrialEndTime = subscription.TrialEndTime
            });
        }

        Logger.LogInformation("Subscription created. UserId: {UserId}, Plan: {PlanName}, Trial: {IsTrial}, HasPaymentMethod: {HasPaymentMethod}",
            userId, plan.PlanName, subscription.Status == SubscriptionStatus.Trial, subscription.StoredPaymentMethodId != null);

        var dto = ToDto(subscription);
        dto.PlanName = plan.PlanName;

        return Ok(new SubscriptionCreateResultDto
        {
            Subscription = dto,
            Payment = paymentOrder
        });
    }

    /// <summary>
    /// 挡住新开通的那条有效订阅若是一条再也激活不了的 Pending（名下没有任何还能付、或已经付掉的支付），收口它并返回 true。
    /// </summary>
    /// <remarks>
    /// 首期单失败 / 过期的回流会把这种 Pending 收口（见 <see cref="EndUnpaidPendingSubscriptionAsync"/>）；
    /// 这里兜的是回流没能落地的那些：修复之前留下的、以及回流处理失败进了死信的。不兜，用户在收银台放弃一次，
    /// 同一产品就永远订不回来。仍有待付或处理中的首期单 → 那是一次活的开通；已经付掉 → 激活在路上，两种都照旧算已订阅。
    /// 查不清就当它还活着。
    /// </remarks>
    private async Task<bool> TryEndAbandonedPendingAsync(Subscription existing, CancellationToken cancellationToken)
    {
        if (existing.Status != SubscriptionStatus.Pending)
            return false;

        foreach (var liveStatus in new[] { PaymentStatus.Pending, PaymentStatus.Processing, PaymentStatus.Succeeded })
        {
            var payments = await _paymentService.GetPaymentListAsync(new PaymentQueryDto
            {
                BusinessOrderNo = existing.SubscriptionNo,
                BusinessType = BusinessType.Subscription,
                Status = liveStatus,
                PageIndex = 1,
                PageSize = 1
            }, cancellationToken: cancellationToken);

            if (!payments.Succeeded || payments.Data == null || payments.Data.TotalCount > 0)
                return false;
        }

        return await EndUnpaidPendingSubscriptionAsync(existing, failReason: null, cancellationToken);
    }

    public async Task<Result> CancelSubscriptionAsync(Guid subscriptionId, CancelSubscriptionDto request, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        // ★ 先抢计费锁再取消。后台扣款抢到这条订阅之后（TryClaimAsync 写下 BillingLockedUntil），
        // 它与渠道之间那次往返期间订阅行照旧可写 —— 此刻取消会当场成功，而钱几秒后照扣不误。
        // 用户看到的是「已取消」外加一笔扣款，日志里只有一句 orphan payment。抢不到答 409（见 ClaimForUserChangeAsync）。
        var claim = await ClaimForUserChangeAsync(subscriptionId, ownerUserId, cancellationToken);
        if (!claim.Succeeded || claim.Data == null)
            return Fail(claim.Message ?? ErrorCodes.SubscriptionNotFound, claim.Code ?? 404);

        var subscription = claim.Data;

        if (subscription.Status == SubscriptionStatus.Cancelled || subscription.Status == SubscriptionStatus.Expired)
        {
            await ReleaseBillingLockAsync(subscription.Id, cancellationToken);
            return Fail(ErrorCodes.SubscriptionAlreadyCancelledOrExpired, 400);
        }

        // ★ 还没付过首期（Pending）的订阅没有「本期」可等：到期后取消与立即取消是同一件事，一律当场终结。
        // 此前「到期后取消」把它置成 PendingRenewal，再打开自动续费就直接变 Active，一分钱没收。
        var neverStarted = subscription.Status == SubscriptionStatus.Pending;
        var immediate = request.Immediate || neverStarted;

        var now = DateTime.UtcNow;

        if (neverStarted)
        {
            // 先关首期单、再落取消：关单发现渠道侧已经收到钱时（记成功、订阅随之激活），取消必须被拒，
            // 而不是留下一条已取消的订阅外加一笔只能人工退的钱。
            var firstPayments = await CloseOpenFirstPaymentsAsync(subscription, cancellationToken);
            if (!firstPayments.Succeeded)
            {
                // 裸 SQL 释放：首付回流可能刚在别的 scope 里把这条订阅激活，跟踪实体上是旧状态，不能整行写回
                await ReleaseBillingLockAsync(subscription.Id, cancellationToken);
                return firstPayments;
            }
        }

        // ★ 暂停中取消：先把暂停时长折回计费时钟。暂停期间 NextBillingTime 刻意冻结在暂停前的值（恢复时才顺延），
        // 此前取消直接置 PendingRenewal 而不折算，NextBillingTime 早已在过去，下一轮过期扫描当场把它过期，
        // 暂停时还剩的那段已付时间一天都没还给用户；立即取消再恢复同理，取消之后的时间也被当成了暂停。
        var pausedFor = subscription.Status == SubscriptionStatus.Paused ? FoldPause(subscription, now) : TimeSpan.Zero;

        subscription.CancelReason = request.Reason;
        subscription.CancelTime = now;
        // 取消完成即释放计费锁：留着它只会让后台扫描白等一个锁窗口
        subscription.BillingLockedUntil = null;

        if (immediate)
        {
            subscription.Status = SubscriptionStatus.Cancelled;
            subscription.EndTime = now;
            subscription.AutoRenew = false;
            // ★ 刻意保留 NextBillingTime：它是「已付到何时」唯一的记录。此前这里把它清空，
            // 恢复时对 null 只能从现在起算一个新周期且不扣款 —— 取消再恢复 = 白送一个完整周期，
            // 每期末重复即无限免费。续费扫描按 Status 排除 Cancelled（上面又已抢到计费锁），
            // 「缩小与在途扣款的竞态面」这个理由已经不成立。
            // 从没付过款的订阅例外：它什么都没付到，修复前开通时预写的那个值不是事实。
            if (neverStarted)
                subscription.NextBillingTime = null;
        }
        else
        {
            // 到期后取消：关闭自动续费，到期自然过期
            subscription.Status = SubscriptionStatus.PendingRenewal;
            subscription.AutoRenew = false;
        }

        await _subscriptionRepository.UpdateAsync(subscription, cancellationToken);
        await ShiftPendingChangesAsync(subscription.Id, pausedFor, cancellationToken);

        // ★ 立即取消结束的是当前周期，与续费 / 过期结束周期是同一件事：仍在等补差款的变更是对这个周期
        // 剩余段算的，周期没了它就失效，连它的待付单一起关掉。留着它，用户取消之后再付那张单，
        // 钱收到了却撞上终态守卫，只剩一行「孤儿付款需退款」的告警。
        // 到期后取消不关：当前周期仍归用户所有，补差覆盖的正是这段剩余期，付了就该生效；
        // 周期真的结束时由逾期扫描（ExpireSubscriptionAsync）收口。
        if (immediate)
            await CancelAwaitingChangesForEndedPeriodAsync(subscription.Id, cancellationToken);

        if (EventBus != null)
        {
            await EventBus.PublishAsync(new SubscriptionCancelledEvent
            {
                SubscriptionId = subscription.Id,
                SubscriptionNo = subscription.SubscriptionNo,
                UserId = subscription.UserId,
                CancelReason = request.Reason,
                Immediate = immediate,
                ExpireTime = subscription.EndTime
            });
        }

        Logger.LogInformation("Subscription cancelled. SubscriptionNo: {SubscriptionNo}, Immediate: {Immediate}",
            subscription.SubscriptionNo, immediate);

        return Ok();
    }

    /// <summary>
    /// 关掉一条还没付过首期的订阅仍可付款的支付单（首期单），全部关掉才返回成功。
    /// </summary>
    /// <remarks>
    /// 不关它，用户开着收银台在另一个标签页取消、再回来付掉：钱收到了，订阅仍是 Cancelled。
    /// 关单先让渠道侧失效（见 <see cref="IPaymentService.ClosePaymentAsync"/>）：渠道侧已经收到钱的会被记成功
    /// （首付回流随即激活订阅），渠道不可达的留着不关，两种都答 409，取消稍后再来：
    /// 前者再来时面对的是一条已激活的订阅，后者等渠道答得上来。
    /// Pending 订阅不会有补差单（计划变更只接受 Active / Trial），这个业务单号下仍开着的单只可能是首期。
    /// </remarks>
    private async Task<Result> CloseOpenFirstPaymentsAsync(Subscription subscription, CancellationToken cancellationToken)
    {
        foreach (var openStatus in new[] { PaymentStatus.Pending, PaymentStatus.Processing })
        {
            var open = await _paymentService.GetPaymentListAsync(new PaymentQueryDto
            {
                BusinessOrderNo = subscription.SubscriptionNo,
                BusinessType = BusinessType.Subscription,
                Status = openStatus,
                PageIndex = 1,
                PageSize = 100
            }, cancellationToken: cancellationToken);

            if (!open.Succeeded || open.Data == null)
                return Fail(ErrorCodes.SubscriptionBillingInProgress, 409);

            foreach (var payment in open.Data.Items)
            {
                var closed = await _paymentService.ClosePaymentAsync(
                    payment.TradeNo, "subscription cancelled before its first payment", cancellationToken: cancellationToken);

                if (closed.Succeeded)
                    continue;

                Logger.LogWarning(
                    "Cancellation of subscription {SubscriptionNo} refused: its first payment {TradeNo} could not be closed ({Message}, {Code}); it is already paid or the channel could not confirm.",
                    subscription.SubscriptionNo, payment.TradeNo, closed.Message, closed.Code);
                return Fail(ErrorCodes.SubscriptionBillingInProgress, 409);
            }
        }

        return Ok();
    }

    public async Task<Result> PauseSubscriptionAsync(Guid subscriptionId, PauseSubscriptionDto request, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        var now = DateTime.UtcNow;
        var maxPauseDays = SubscriptionOptions.MaxPauseDays;

        if (request.ResumeAt.HasValue && request.ResumeAt.Value <= now)
            return Fail(ErrorCodes.SubscriptionCannotPause, 400);

        // 与取消同一把计费锁：扣款在途时暂停，完成回流随后把它改回 Active 并推进周期，用户以为已暂停却被扣款
        var claim = await ClaimForUserChangeAsync(subscriptionId, ownerUserId, cancellationToken);
        if (!claim.Succeeded || claim.Data == null)
            return Fail(claim.Message ?? ErrorCodes.SubscriptionNotFound, claim.Code ?? 404);

        var subscription = claim.Data;

        if (subscription.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Trial))
        {
            await ReleaseBillingLockAsync(subscription.Id, cancellationToken);
            return Fail(ErrorCodes.SubscriptionCannotPause, 400);
        }

        if (maxPauseDays > 0)
        {
            var limit = now.AddDays(maxPauseDays);
            // 不传恢复时间时按上限自动设定，避免订阅无限期停在暂停态
            subscription.PausedUntil = request.ResumeAt is { } resumeAt
                ? (resumeAt > limit ? limit : resumeAt)
                : limit;
        }
        else
        {
            subscription.PausedUntil = request.ResumeAt;
        }

        subscription.Status = SubscriptionStatus.Paused;
        subscription.CancelReason = request.Reason;
        subscription.PausedAt = now;
        // NextBillingTime 保持不变：续费扫描只取 Active/PastDue，Paused 天然不在其中。
        // 保留它是为了在恢复时能把"还剩多少天"原样还回去（见 ResumeInternalAsync）。
        subscription.BillingLockedUntil = null;

        await _subscriptionRepository.UpdateAsync(subscription, cancellationToken);

        Logger.LogInformation("Subscription paused. SubscriptionNo: {SubscriptionNo}, ResumeAt: {ResumeAt}",
            subscription.SubscriptionNo, subscription.PausedUntil);

        return Ok();
    }

    public async Task<Result> ResumeSubscriptionAsync(Guid subscriptionId, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var claim = await ClaimForUserChangeAsync(subscriptionId, ownerUserId, cancellationToken);
        if (!claim.Succeeded || claim.Data == null)
            return Fail(claim.Message ?? ErrorCodes.SubscriptionNotFound, claim.Code ?? 404);

        var subscription = claim.Data;

        // 撤销一次取消 = 重新要续费；离开暂停则不替用户改自动续费开关（见 ResumeInternalAsync）
        var undoesCancellation = subscription.Status is SubscriptionStatus.Cancelled or SubscriptionStatus.PendingRenewal;

        // ★ 从没开始过的订阅（取消之前一分钱没付过、也没开过试用）没有可恢复的东西：恢复它就是白送一期。
        // 要用就重新开通并付首期。
        if (subscription.Status is not (SubscriptionStatus.Cancelled or SubscriptionStatus.Paused or SubscriptionStatus.PendingRenewal)
            || (undoesCancellation && !HasEverStarted(subscription)))
        {
            await ReleaseBillingLockAsync(subscription.Id, cancellationToken);
            return Fail(ErrorCodes.SubscriptionCannotResume, 400);
        }

        // ★ 已取消的订阅不占「同一用户同一产品只有一条有效订阅」的名额，用户可能已经重新开通了同一产品。
        // 恢复它就是第二条有效订阅：与开通同一条判重、同一个答复。此前直接写回，撞上唯一索引成了 500。
        if (subscription.Status == SubscriptionStatus.Cancelled && await HasOtherLiveSubscriptionAsync(subscription, cancellationToken))
        {
            await ReleaseBillingLockAsync(subscription.Id, cancellationToken);
            return DuplicateSubscriptionFor(subscription.ProductCode);
        }

        // 写回时一并放锁（见 ResumeInternalAsync）；判重与写回之间并发开通的那一条由索引挡下
        if (!await ResumeInternalAsync(subscription, undoesCancellation, cancellationToken))
            return DuplicateSubscriptionFor(subscription.ProductCode);

        Logger.LogInformation("Subscription resumed. SubscriptionNo: {SubscriptionNo}, NextBilling: {NextBilling}",
            subscription.SubscriptionNo, subscription.NextBillingTime);

        return Ok();
    }

    /// <summary>
    /// 恢复订阅的统一实现：清理终止痕迹，并以「已付到何时」为准决定要不要收款。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 此前只是把状态改回 Active，既不清 EndTime 也不重算 NextBillingTime：
    /// 立即取消（EndTime=now）的订阅恢复后仍带着过去的时间，下一轮扫描会立刻再把它过期掉。
    /// </para>
    /// <para>
    /// ★ <b>恢复不产生免费周期。</b>已付的那一期还没走完（NextBillingTime 在未来）→ 原样保留；
    /// 已经走完或压根没有（取消后过了很久、逾期状态被取消）→ 当场按新的一期扣款，
    /// 扣款回流成功才推进计费时间，失败落 PastDue 走催款。此前对这种情形一律
    /// 「从现在起算一个新周期」且不扣款，取消再恢复就是白送一期，可无限重复。
    /// </para>
    /// <para>
    /// ★ <b>自动续费开关只在撤销取消时打开</b>（<paramref name="reEnableAutoRenew"/>）。离开暂停（用户点恢复或暂停到期的
    /// 后台自动恢复）保留用户的选择：此前一律写回 <c>true</c>，试用期里关掉自动续费再暂停的用户，
    /// 暂停到期后照样在试用截止时被转正扣款。开关关着时，正式订阅回到 <c>PendingRenewal</c>（到期过期），
    /// 试用回到 <c>Trial</c>（到期由转正扫描过期）；已付的那期已经走完的不扣款，等过期扫描收口。
    /// </para>
    /// <para>
    /// 返回 false：写回被「同一用户同一产品只有一条有效订阅」的索引挡下（恢复一条已取消的订阅时，并发开通了同一产品），
    /// 订阅原样未动、计费锁已放掉。
    /// </para>
    /// </remarks>
    private async Task<bool> ResumeInternalAsync(Subscription subscription, bool reEnableAutoRenew, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        // 从暂停恢复：把暂停时长原样加回计费时间与试用截止，剩余周期分毫不差地还给用户。
        // 重算一个完整周期会把"暂停一天"变成"白送一个周期"。
        var pausedFor = FoldPause(subscription, now);

        if (reEnableAutoRenew)
            subscription.AutoRenew = true;

        subscription.Status = ResolveRunningStatus(subscription, now);
        subscription.CancelReason = null;
        subscription.CancelTime = null;
        subscription.EndTime = null;
        subscription.RenewalRetryCount = 0;
        subscription.PastDueSince = null;

        var periodElapsed = subscription.Status != SubscriptionStatus.Trial
            && (subscription.NextBillingTime == null || subscription.NextBillingTime <= now);

        if (subscription.Status == SubscriptionStatus.Trial)
        {
            subscription.NextBillingTime = subscription.TrialEndTime;
        }
        else if (periodElapsed)
        {
            // 没有付款事实就没有延长：时钟停在「现在」，下面立刻发起一次续费扣款；
            // 回流成功才从这里起算新的一期，失败则订阅落 PastDue 等用户处理。
            // 自动续费关着时不扣款：订阅处于 PendingRenewal 且已到期，由下一轮过期扫描收口。
            subscription.NextBillingTime = now;
        }

        // 调用方都持有计费锁（用户恢复经 ClaimForUserChangeAsync、后台自动恢复经 TryClaimAsync），写回时一并放掉；
        // 下面的扣款自己再抢一次
        subscription.BillingLockedUntil = null;
        try
        {
            await _subscriptionRepository.UpdateAsync(subscription, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueConstraintViolation())
        {
            Logger.LogInformation(ex,
                "Resuming subscription {SubscriptionNo} refused by the uniqueness index: the user has another live subscription to the same product.",
                subscription.SubscriptionNo);

            // 同 TryInsertSubscriptionAsync：不丢掉这条改动，下一次 SaveChanges 会把它重放出去
            _subscriptionRepository.Discard(subscription);
            await ReleaseBillingLockAsync(subscription.Id, cancellationToken);
            return false;
        }

        // 待生效变更的生效日取的是暂停前的周期边界，与计费时钟一起顺延
        await ShiftPendingChangesAsync(subscription.Id, pausedFor, cancellationToken);

        if (periodElapsed && subscription.AutoRenew)
            await RetryBillingInternalAsync(subscription.Id, cancellationToken);

        return true;
    }

    /// <summary>
    /// 把暂停时长折回计费时钟：<c>NextBillingTime</c> 与未转正的试用截止各顺延暂停了多久，清掉暂停标记，返回顺延了多少。
    /// 只改内存里的实体；写库之后由调用方用 <see cref="ShiftPendingChangesAsync"/> 顺延待生效变更。
    /// </summary>
    /// <remarks>
    /// 暂停期间 <c>NextBillingTime</c> 刻意不动（续费扫描本就不选 Paused），离开暂停的每一条路径（恢复、暂停到期的自动恢复、
    /// 暂停中取消）都要经过这里，否则冻结的那段已付时间就丢在了路上。
    /// </remarks>
    private static TimeSpan FoldPause(Subscription subscription, DateTime now)
    {
        var pausedFor = subscription.PausedAt is { } pausedAt && now > pausedAt
            ? now - pausedAt
            : TimeSpan.Zero;

        if (pausedFor > TimeSpan.Zero)
        {
            if (subscription.NextBillingTime.HasValue)
                subscription.NextBillingTime = subscription.NextBillingTime.Value + pausedFor;

            if (subscription.TrialEndTime.HasValue && subscription.TrialConvertedTime == null)
                subscription.TrialEndTime = subscription.TrialEndTime.Value + pausedFor;
        }

        subscription.PausedAt = null;
        subscription.PausedUntil = null;
        return pausedFor;
    }

    /// <summary>
    /// 把这条订阅上待生效（<c>Pending</c>）变更的生效日顺延 <paramref name="by"/>。
    /// </summary>
    /// <remarks>
    /// 到期变更（典型是降级）的 <c>EffectiveDate</c> 是排定那一刻的 <c>NextBillingTime</c> 快照。暂停把计费时钟往后推了，
    /// 生效日不跟着推，变更就会在顺延后的续费之前生效：用户在已按高价付过的那段时间里被换到低计划上。
    /// 条件更新只动仍是 <c>Pending</c> 的行；调用方持有计费锁，到期结算（同样要抢这把锁）不会同时改它。
    /// </remarks>
    private async Task ShiftPendingChangesAsync(Guid subscriptionId, TimeSpan by, CancellationToken cancellationToken)
    {
        if (by <= TimeSpan.Zero)
            return;

        var pending = await _changeRepository.AsNoTracking()
            .Where(c => c.SubscriptionId == subscriptionId && c.Status == SubscriptionChangeStatus.Pending)
            .Select(c => new { c.Id, c.EffectiveDate })
            .ToListAsync(cancellationToken);

        foreach (var change in pending)
        {
            var shifted = change.EffectiveDate + by;
            await _changeRepository.AsQueryable()
                .Where(c => c.Id == change.Id && c.Status == SubscriptionChangeStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.EffectiveDate, shifted), cancellationToken);
        }
    }

    /// <summary>
    /// 这条订阅是否真正开始过：付过一笔订阅款（首期 / 续费 / 转正都会写 <see cref="Subscription.LastBillingTradeNo"/>）、
    /// 记过实收额，或开过试用。三者皆无 = 开通之后一分钱没付就被取消了。
    /// </summary>
    private static bool HasEverStarted(Subscription subscription)
        => !string.IsNullOrEmpty(subscription.LastBillingTradeNo)
            || subscription.TrialStartTime != null
            || subscription.PaidAmount > 0;

    /// <summary>
    /// 一条「在跑」的订阅此刻该处的状态：未转正且试用未到期 → <c>Trial</c>；否则按自动续费开关
    /// 取 <c>Active</c>（到期续费）或 <c>PendingRenewal</c>（到期过期）。
    /// </summary>
    /// <remarks>
    /// ★ 付款回流、恢复、重开自动续费都经这里定状态，而不是各自写 <c>Active</c>。续费扫描只取 <c>AutoRenew</c>
    /// 为真的订阅、过期扫描只取 <c>PendingRenewal</c>：一条关了自动续费的 <c>Active</c> 两边都不收，
    /// 付一期用到永远（付首期前关掉自动续费、或续费扣款在途时关掉它，都会落到这个形状）。
    /// </remarks>
    private static SubscriptionStatus ResolveRunningStatus(Subscription subscription, DateTime now)
    {
        if (subscription.TrialEndTime > now && subscription.TrialConvertedTime == null)
            return SubscriptionStatus.Trial;

        return subscription.AutoRenew ? SubscriptionStatus.Active : SubscriptionStatus.PendingRenewal;
    }

    public async Task<Result<SubscriptionDto>> UpdatePaymentMethodAsync(Guid subscriptionId, AttachPaymentMethodDto request, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        var owner = await _accountResolver.ResolveScopeAsync(ownerUserId, manage: true, cancellationToken);
        if (!owner.Succeeded)
            return Fail<SubscriptionDto>(owner.Message ?? ErrorCodes.BillingAccountAccessDenied, owner.Code ?? 403);

        var subscription = await FindOwnedAsync(subscriptionId, owner.Data, cancellationToken);
        if (subscription == null)
            return Fail<SubscriptionDto>(ErrorCodes.SubscriptionNotFound, 404);

        var contact = await _billingAccountService.ResolveContactAsync(AccountOf(subscription), CurrentUser?.UserName, CurrentUser?.Email, CurrentUser?.Id, cancellationToken);
        var bindResult = await ResolvePaymentMethodAsync(
            AccountOf(subscription), CurrentUser?.Id ?? subscription.UserId, subscription.ChannelCode,
            request.PaymentMethodId, request.PaymentMethodToken, contact, cancellationToken);

        if (!bindResult.Succeeded)
            return Fail<SubscriptionDto>(bindResult.Message ?? ErrorCodes.PaymentMethodBindingFailed, bindResult.Code ?? 400);

        if (bindResult.Data == null)
            return Fail<SubscriptionDto>(ErrorCodes.PaymentMethodNotFound, 404);

        // ★ 只写支付方式这几列。读行与写行之间隔着一次绑卡（渠道往返），这期间续费回流可能已经推进了「已付到」、
        // 写了流水号、放了锁：整行写回会把它们拨回旧值，锁过期后同一期再扣一次。
        // 换卡刻意不抢计费锁：它不和在途扣款冲突（那笔用的是发起时的凭据），而一笔待落定的扣款可能持锁数天，
        // 用户恰恰要在那时换掉出问题的卡。
        var method = bindResult.Data;
        await _subscriptionRepository.AsQueryable()
            .Where(s => s.Id == subscription.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.StoredPaymentMethodId, (Guid?)method.Id)
                .SetProperty(x => x.PaymentMethodToken, method.Token)
                .SetProperty(x => x.ProviderCustomerId, method.ProviderCustomerId)
                .SetProperty(x => x.PaymentMethodBrand, method.Brand)
                .SetProperty(x => x.PaymentMethodLast4, method.Last4)
                .SetProperty(x => x.LastModificationTime, (DateTime?)DateTime.UtcNow), cancellationToken);

        Logger.LogInformation("Subscription payment method updated. SubscriptionNo: {SubscriptionNo}, Brand: {Brand}, Last4: {Last4}",
            subscription.SubscriptionNo, method.Brand, method.Last4);

        // 换卡的常见动机就是挽回一笔失败的续费（或一条已暂停服务的订阅），因此立刻重试一次而不是等下一轮扫描
        // （重试自己抢锁、按库里此刻的状态判定，见 RetryBillingInternalAsync）。新卡要 3-D Secure 时这一次会失败，
        // 订阅保持原状，管理者在订阅页用「立即付款」当场补缴。
        var updated = await FindOwnedAsync(subscription.Id, owner: null, cancellationToken) ?? subscription;
        if (updated.Status is SubscriptionStatus.PastDue or SubscriptionStatus.Suspended)
        {
            await RetryBillingInternalAsync(updated.Id, cancellationToken);
            updated = await FindOwnedAsync(subscription.Id, owner: null, cancellationToken) ?? updated;
        }

        return Ok(ToDto(updated));
    }

    public async Task<Result> UpdateAutoRenewAsync(Guid subscriptionId, bool autoRenew, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        // 与取消同一把计费锁：续费扣款在途时改开关，回流随后按它读到的旧开关整行写回
        var claim = await ClaimForUserChangeAsync(subscriptionId, ownerUserId, cancellationToken);
        if (!claim.Succeeded || claim.Data == null)
            return Fail(claim.Message ?? ErrorCodes.SubscriptionNotFound, claim.Code ?? 404);

        var subscription = claim.Data;

        // 在 PendingRenewal 上重开自动续费就是一次恢复：从没开始过的订阅（修复前「到期后取消」一个 Pending 落下的形状）
        // 不能借此变成在跑的订阅。
        if (autoRenew && subscription.Status == SubscriptionStatus.PendingRenewal && !HasEverStarted(subscription))
        {
            await ReleaseBillingLockAsync(subscription.Id, cancellationToken);
            return Fail(ErrorCodes.SubscriptionCannotResume, 400);
        }

        subscription.AutoRenew = autoRenew;
        subscription.BillingLockedUntil = null;
        if (!autoRenew && subscription.Status == SubscriptionStatus.Active)
        {
            subscription.Status = SubscriptionStatus.PendingRenewal;
        }
        else if (autoRenew && subscription.Status == SubscriptionStatus.PendingRenewal)
        {
            // 与恢复同一条推导：试用期里「到期后取消」再打开，回到 Trial 由转正扫描按折扣价处理，
            // 而不是变成 Active、在试用截止时被续费扫描按全价扣
            subscription.Status = ResolveRunningStatus(subscription, DateTime.UtcNow);
        }

        await _subscriptionRepository.UpdateAsync(subscription, cancellationToken);

        Logger.LogInformation("Subscription auto-renewal updated. SubscriptionNo: {SubscriptionNo}, AutoRenew: {AutoRenew}",
            subscription.SubscriptionNo, autoRenew);

        return Ok();
    }

    public async Task<Result> RetryBillingAsync(Guid subscriptionId, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var owner = await _accountResolver.ResolveScopeAsync(ownerUserId, manage: true, cancellationToken);
        if (!owner.Succeeded)
            return Fail(owner.Message ?? ErrorCodes.BillingAccountAccessDenied, owner.Code ?? 403);

        var subscription = await OwnedBy(_subscriptionRepository.AsQueryable(), owner.Data)
            .Where(s => s.Id == subscriptionId)
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(cancellationToken);

        if (subscription == null)
            return Fail(ErrorCodes.SubscriptionNotFound, 404);

        if (subscription.Status is not (SubscriptionStatus.PastDue or SubscriptionStatus.Suspended))
            return Fail(ErrorCodes.SubscriptionCannotRetryBilling, 400);

        // 折扣抵满全价的试用转正一分钱都不收，要一张永远不会被扣的卡只会把它卡在 PastDue 直到宽限期过期
        // （修复「转正按全价收」之前落到 PastDue 的试用正是这种形态）。
        if (string.IsNullOrWhiteSpace(subscription.PaymentMethodToken) && !IsChargeFreeTrialConversion(subscription, subscription.Plan))
            return Fail(ErrorCodes.SubscriptionPaymentMethodMissing, 400);

        await RetryBillingInternalAsync(subscription.Id, cancellationToken);
        return Ok();
    }

    public async Task<Result<SubscriptionDto>> GetSubscriptionAsync(Guid id, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var owner = await _accountResolver.ResolveScopeAsync(ownerUserId, manage: false, cancellationToken);
        if (!owner.Succeeded)
            return Fail<SubscriptionDto>(owner.Message ?? ErrorCodes.BillingAccountAccessDenied, owner.Code ?? 403);

        var subscription = await FindOwnedAsync(id, owner.Data, cancellationToken);
        if (subscription == null)
            return Fail<SubscriptionDto>(ErrorCodes.SubscriptionNotFound, 404);

        var dto = ToDto(subscription);

        // 补齐计划名：详情页要展示"当前订阅的是哪个套餐"，只查一条不值得为它做 join。
        // 回填到 DTO 而不是订阅的导航属性——后者是游离实体，挂上去会污染后续保存。
        if (string.IsNullOrEmpty(dto.PlanName))
        {
            var plan = await _planRepository.FirstOrDefaultAsync(p => p.Id == subscription.PlanId, cancellationToken);
            dto.PlanName = plan?.PlanName;
        }

        return Ok(dto);
    }

    public async Task<Result<IPagedList<SubscriptionDto>>> GetUserSubscriptionsAsync(Guid userId, PagedQueryDto? query = null, CancellationToken cancellationToken = default)
    {
        query ??= new OwnerListQueryDto();

        var owner = await _accountResolver.ResolveScopeAsync(userId, manage: false, cancellationToken);
        if (!owner.Succeeded)
            return Fail<IPagedList<SubscriptionDto>>(owner.Message ?? ErrorCodes.BillingAccountAccessDenied, owner.Code ?? 403);

        var pagedList = await OwnedBy(_subscriptionRepository.AsNoTracking(), owner.Data)
            .OrderByDescending(s => s.CreationTime)
            .ProjectTo<Subscription, SubscriptionDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        return Ok(pagedList);
    }

    public async Task<Result<IPagedList<SubscriptionDto>>> GetSubscriptionListAsync(SubscriptionQueryDto query, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var owner = await _accountResolver.ResolveScopeAsync(ownerUserId, manage: false, cancellationToken);
        if (!owner.Succeeded)
            return Fail<IPagedList<SubscriptionDto>>(owner.Message ?? ErrorCodes.BillingAccountAccessDenied, owner.Code ?? 403);

        var queryable = OwnedBy(_subscriptionRepository.AsNoTracking().Filter(query), owner.Data);

        var pagedList = await queryable
            .OrderByDescending(s => s.CreationTime)
            .ProjectTo<Subscription, SubscriptionDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        return Ok(pagedList);
    }

    public async Task<Result<List<SubscriptionPlanDto>>> GetSubscriptionPlansAsync(bool activeOnly = true, string? productCode = null, bool includeTax = false, CancellationToken cancellationToken = default)
    {
        var queryable = _planRepository.AsNoTracking();

        if (activeOnly)
            queryable = queryable.Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(productCode))
            queryable = queryable.Where(p => p.ProductCode == productCode);

        var plans = await queryable.OrderBy(p => p.SortOrder).ToListAsync(cancellationToken);
        var dtos = plans.MapToList<SubscriptionPlanDto>();

        if (includeTax && _taxCalculator != null)
        {
            foreach (var dto in dtos)
                dto.Tax = await PreviewPlanTaxAsync(dto, cancellationToken);
        }

        return Ok(dtos);
    }

    /// <summary>
    /// 一期标价的计税结果，与首期付款建单时同一个计算器、同一组入参（净额 = 标价、业务类型 = 订阅）。
    /// 计算器答不上来时返回 null 并记一条 Warning：开通页不印税，而不是让整张计划列表打不开。
    /// </summary>
    private async Task<SubscriptionPlanTaxDto?> PreviewPlanTaxAsync(SubscriptionPlanDto plan, CancellationToken cancellationToken)
    {
        if (plan.Price <= 0)
            return new SubscriptionPlanTaxDto { PayableAmount = 0m };

        var tax = await _taxCalculator!.CalculateAsync(new TaxCalculationRequest
        {
            NetAmount = plan.Price,
            Currency = plan.Currency,
            BusinessType = BusinessType.Subscription
        }, cancellationToken);

        if (!tax.Succeeded || tax.Data == null)
        {
            Logger.LogWarning("Tax preview for subscription plan {PlanCode} failed: {Message}. The plan is listed without tax.", plan.PlanCode, tax.Message);
            return null;
        }

        return new SubscriptionPlanTaxDto
        {
            TaxIncluded = tax.Data.TaxIncluded,
            TaxAmount = tax.Data.TaxAmount,
            PayableAmount = tax.Data.PayableAmount,
            Lines = tax.Data.TaxLines
        };
    }

    public Result<SubscriptionPlanDefaultsDto> GetPlanDefaults()
        => Ok(new SubscriptionPlanDefaultsDto { Currency = ResolvePlanCurrency(null) });

    /// <summary>
    /// 计划的币种：填了就规整成大写，没填就是这台宿主的默认币种。
    /// </summary>
    /// <remarks>
    /// 此前请求 DTO 的币种自带初始值 <c>USD</c>，于是任何没写币种的建计划请求都落成 USD，
    /// 与 <c>Payment:DefaultCurrency</c> 无关（一家加元部署新建的计划默认是美元）。
    /// </remarks>
    private string ResolvePlanCurrency(string? requested)
        => string.IsNullOrWhiteSpace(requested)
            ? (string.IsNullOrWhiteSpace(PaymentOptions.DefaultCurrency) ? PaymentConstants.DefaultCurrency : PaymentOptions.DefaultCurrency.Trim().ToUpperInvariant())
            : requested.Trim().ToUpperInvariant();

    public async Task<Result<SubscriptionPlanDto>> CreatePlanAsync(SubscriptionPlanDto planDto, CancellationToken cancellationToken = default)
    {
        Check.NotNull(planDto);

        var pricing = ValidatePlanPricing(planDto);
        if (!pricing.Succeeded)
            return Fail<SubscriptionPlanDto>(pricing.Message ?? ErrorCodes.PaymentInvalidAmount, pricing.Code ?? 400);

        var duplicated = await _planRepository.AnyAsync(p => p.PlanCode == planDto.PlanCode, cancellationToken);
        if (duplicated)
            return Fail<SubscriptionPlanDto>(ErrorCodes.SubscriptionPlanCodeAlreadyExists, 409);

        var plan = planDto.MapTo<SubscriptionPlan>();
        plan.Currency = ResolvePlanCurrency(planDto.Currency);
        await _planRepository.InsertAsync(plan, cancellationToken);

        Logger.LogInformation("Subscription plan created. PlanName: {PlanName}", plan.PlanName);

        return Ok(plan.MapTo<SubscriptionPlanDto>());
    }

    public async Task<Result> UpdatePlanAsync(Guid planId, SubscriptionPlanDto planDto, CancellationToken cancellationToken = default)
    {
        Check.NotNull(planDto);

        var pricing = ValidatePlanPricing(planDto);
        if (!pricing.Succeeded)
            return pricing;

        var plan = await _planRepository.FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);
        if (plan == null)
            return Fail(ErrorCodes.SubscriptionPlanNotFound, 404);

        // 停用与 DeletePlanAsync 同一条规则：有存量订阅的计划不能停用（整行 PUT 里的 IsActive 也是一条停用路径）。
        // 排在条款检查之前：两者同一个错误码，调用方据「这次是不是在停用」给出说法。
        if (plan.IsActive && !planDto.IsActive && await HasLiveSubscriptionsAsync(planId, cancellationToken))
            return Fail(ErrorCodes.SubscriptionPlanHasActiveSubscriptions, 400);

        // ★ 有存量订户的计划不能改计费条款（币种、周期），要换条款就新建计划。续费按计划现价扣，
        // 币种与周期却取订阅开通时的快照：10 USD / 月改成 1500 JPY / 月，下一次续费扣 1500 USD；
        // 改成 100 / 年，月付订户每个月被扣 100。只改价（同币种同周期）照旧允许，续费按新价收。
        // 判据与停用计划同一个（非终态订阅）；已取消的订阅可以恢复，它们由续费侧按自己的快照价兜底。
        var currency = ResolvePlanCurrency(planDto.Currency);
        var termsChanged = !string.Equals(plan.Currency, currency, StringComparison.OrdinalIgnoreCase)
            || plan.CycleType != planDto.CycleType
            || plan.CycleValue != planDto.CycleValue;

        if (termsChanged && await HasLiveSubscriptionsAsync(planId, cancellationToken))
            return Fail(ErrorCodes.SubscriptionPlanHasActiveSubscriptions, 400);

        plan.PlanName = planDto.PlanName;
        plan.ProductCode = planDto.ProductCode;
        plan.Description = planDto.Description;
        plan.Price = planDto.Price;
        plan.Currency = currency;
        plan.CycleType = planDto.CycleType;
        plan.CycleValue = planDto.CycleValue;
        plan.TrialDays = planDto.TrialDays;
        plan.IsActive = planDto.IsActive;
        plan.AllowTrial = planDto.AllowTrial;
        plan.TrialDiscount = planDto.TrialDiscount;
        plan.IncludedStorageBytes = planDto.IncludedStorageBytes;
        plan.StorageOveragePricePerGbMonth = planDto.StorageOveragePricePerGbMonth;
        plan.SortOrder = planDto.SortOrder;

        await _planRepository.UpdateAsync(plan, cancellationToken);

        Logger.LogInformation("Subscription plan updated. PlanId: {PlanId}", planId);

        return Ok();
    }

    public async Task<Result> DeletePlanAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        var plan = await _planRepository.FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);
        if (plan == null)
            return Fail(ErrorCodes.SubscriptionPlanNotFound, 404);

        // 检查是否有使用该计划的活跃订阅
        if (await HasLiveSubscriptionsAsync(planId, cancellationToken))
            return Fail(ErrorCodes.SubscriptionPlanHasActiveSubscriptions, 400);

        plan.IsActive = false;
        await _planRepository.UpdateAsync(plan, cancellationToken);

        Logger.LogInformation("Subscription plan deactivated. PlanId: {PlanId}", planId);

        return Ok();
    }

    /// <summary>这个计划上还有没终结的订阅（非取消、非过期）。</summary>
    private Task<bool> HasLiveSubscriptionsAsync(Guid planId, CancellationToken cancellationToken)
        => _subscriptionRepository.AnyAsync(
            s => s.PlanId == planId && s.Status != SubscriptionStatus.Cancelled && s.Status != SubscriptionStatus.Expired,
            cancellationToken);

    /// <summary>
    /// 计划价目的合法性：价格非负、非一次性周期的周期值为正、试用折扣（金额）落在 [0, Price] 内 ——
    /// 折扣在开通试用时快照进 <see cref="Subscription.DiscountAmount"/>，转正按「现价 − 折扣」收，
    /// 超过价格的折扣只会变成一次免费转正，负数折扣则是向试用用户多收钱。
    /// </summary>
    private Result ValidatePlanPricing(SubscriptionPlanDto planDto)
    {
        if (planDto.Price < 0)
            return Fail(ErrorCodes.PaymentInvalidAmount, 400);

        if (planDto.CycleValue <= 0 && planDto.CycleType != BillingCycleType.OneTime)
            return Fail(ErrorCodes.PaymentInvalidAmount, 400);

        if (planDto.TrialDiscount is { } trialDiscount && (trialDiscount < 0 || trialDiscount > planDto.Price))
            return Fail(ErrorCodes.SubscriptionTrialDiscountOutOfRange, 400);

        if (planDto.IncludedStorageBytes is < 0 || planDto.StorageOveragePricePerGbMonth is < 0)
            return Fail("Included storage and the storage overage price cannot be negative.", 400);

        return Ok();
    }

    /// <summary>按 Id 取一条订阅；<paramref name="owner"/> 不为 null 时只认这个账户的。</summary>
    private Task<Subscription?> FindOwnedAsync(Guid subscriptionId, BillingAccountRef? owner, CancellationToken cancellationToken)
        => owner == null
            ? _subscriptionRepository.FirstOrDefaultAsync(s => s.Id == subscriptionId, cancellationToken)
            : _subscriptionRepository.FirstOrDefaultAsync(
                s => s.Id == subscriptionId && s.BillingAccountType == owner.Type && s.BillingAccountId == owner.Id, cancellationToken);

    /// <summary>只留 <paramref name="owner"/> 账户的订阅；为 null 时不限定（管理端 / 系统调用）。</summary>
    private static IQueryable<Subscription> OwnedBy(IQueryable<Subscription> query, BillingAccountRef? owner)
        => owner == null
            ? query
            : query.Where(s => s.BillingAccountType == owner.Type && s.BillingAccountId == owner.Id);

    /// <summary>订阅所属的计费账户。</summary>
    private static BillingAccountRef AccountOf(Subscription subscription)
        => BillingAccountRef.From(subscription.BillingAccountType, subscription.BillingAccountId)
            ?? BillingAccountRef.ForUser(subscription.UserId);

    /// <summary>
    /// 用户发起的订阅状态变更（取消 / 暂停 / 恢复 / 开关自动续费）的统一入口：先抢计费锁，<b>再</b>读这一行。
    /// 成功时调用方持有锁，必须在写回时一并释放（<c>BillingLockedUntil = null</c>）或显式 <see cref="ReleaseBillingLockAsync"/>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ 两件事缺一不可。<b>抢锁</b>：后台扣款抢到一条订阅之后，它与渠道之间那次往返期间订阅行照旧可写，
    /// 此刻暂停 / 关自动续费会当场成功，而扣款的完成回流随后把状态改回 Active 并推进周期，
    /// 用户以为已暂停却被扣款。<b>锁后再读</b>：读在抢锁之前，读与抢之间落地的续费回流（推进了「已付到」、写了流水号、放了锁）
    /// 会被这里的整行写回拨回旧值，锁过期后同一期再扣一次。
    /// </para>
    /// <para>
    /// 条件与 <c>TryClaimAsync</c> 逐字相同（未锁定或锁已过期）；抢不到答 409 <c>SUBSCRIPTION_BILLING_IN_PROGRESS</c>：
    /// 锁最多持有 <c>BillingLockMinutes</c> 分钟（或到在途支付落定），这不是一件永远不会好的事。
    /// 先认归属再抢锁：锁不认人，先抢后查等于让任何人凭一个订阅 Id 把别人的订阅锁上几分钟。
    /// </para>
    /// </remarks>
    private async Task<Result<Subscription>> ClaimForUserChangeAsync(Guid subscriptionId, Guid? ownerUserId, CancellationToken cancellationToken)
    {
        // 用户发起的变更一律要能管理订阅所属的账户
        var scope = await _accountResolver.ResolveScopeAsync(ownerUserId, manage: true, cancellationToken);
        if (!scope.Succeeded)
            return Fail<Subscription>(scope.Message ?? ErrorCodes.BillingAccountAccessDenied, scope.Code ?? 403);
        var owner = scope.Data;

        var owned = await OwnedBy(_subscriptionRepository.AsNoTracking(), owner)
            .AnyAsync(s => s.Id == subscriptionId, cancellationToken);
        if (!owned)
            return Fail<Subscription>(ErrorCodes.SubscriptionNotFound, 404);

        var now = DateTime.UtcNow;
        if (!await TryClaimAsync(subscriptionId, now, now.AddMinutes(PaymentOptions.BillingLockMinutes), cancellationToken))
            return Fail<Subscription>(ErrorCodes.SubscriptionBillingInProgress, 409);

        var subscription = await FindOwnedAsync(subscriptionId, owner, cancellationToken);
        if (subscription == null)
        {
            await ReleaseBillingLockAsync(subscriptionId, cancellationToken);
            return Fail<Subscription>(ErrorCodes.SubscriptionNotFound, 404);
        }

        return Ok(subscription);
    }

    private static bool ShouldStartTrial(CreateSubscriptionDto request, SubscriptionPlan plan)
        => request.EnableTrial && plan.AllowTrial;

    /// <summary>
    /// 解析本次订阅要用的支付方式：显式ID &gt; 渠道 token &gt; 用户默认卡。
    /// 三者都没有时返回 null（允许无卡开通，但自动续费届时会失败并进入催款）。
    /// </summary>
    private async Task<Result<StoredPaymentMethod?>> ResolvePaymentMethodAsync(
        BillingAccountRef account,
        Guid actingUserId,
        string channelCode,
        Guid? paymentMethodId,
        string? paymentMethodToken,
        BillingContact contact,
        CancellationToken cancellationToken)
    {
        if (paymentMethodId.HasValue)
        {
            // ★ 只认这条订阅的渠道上保存的支付方式：续费按订阅的渠道去扣，另一个渠道的凭据在那边必然被拒，
            // 而此前这里照收、接口答 200，下一次续费才失败落 PastDue。另一个渠道上的支付方式按「不存在」答复，
            // 与默认卡的查找（按渠道）同一口径。
            var method = await _paymentMethodService.FindByIdAsync(account, paymentMethodId.Value, cancellationToken);
            return method == null || !string.Equals(method.ChannelCode, channelCode, StringComparison.OrdinalIgnoreCase)
                ? Result.Failure<StoredPaymentMethod?>(ErrorCodes.PaymentMethodNotFound, 404)
                : Result.Success<StoredPaymentMethod?>(method);
        }

        if (!string.IsNullOrWhiteSpace(paymentMethodToken))
        {
            var bound = await _paymentMethodService.BindEntityAsync(
                account, actingUserId, channelCode, paymentMethodToken, contact.Name, contact.PrimaryEmail, setAsDefault: true, cancellationToken);

            return bound.Succeeded && bound.Data != null
                ? Result.Success<StoredPaymentMethod?>(bound.Data)
                : Result.Failure<StoredPaymentMethod?>(bound.Message ?? ErrorCodes.PaymentMethodBindingFailed, bound.Code ?? 400);
        }

        var defaultMethod = await _paymentMethodService.FindDefaultAsync(account, channelCode, cancellationToken);
        return Result.Success<StoredPaymentMethod?>(defaultMethod);
    }

    private static void ApplyPaymentMethod(Subscription subscription, StoredPaymentMethod? method)
    {
        if (method == null)
            return;

        subscription.StoredPaymentMethodId = method.Id;
        subscription.PaymentMethodToken = method.Token;
        subscription.ProviderCustomerId = method.ProviderCustomerId;
        subscription.PaymentMethodBrand = method.Brand;
        subscription.PaymentMethodLast4 = method.Last4;
    }

    private async Task<Result<CouponPreviewDto>> PreviewSubscriptionCouponAsync(
        string? couponCode, Guid userId, Subscription subscription, SubscriptionPlan plan, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(couponCode))
            return Result.Success<CouponPreviewDto>(null!);

        if (_couponService == null)
            return Result.Failure<CouponPreviewDto>(ErrorCodes.CouponInvalid, 400);

        return await _couponService.PreviewAsync(new CouponApplyContext
        {
            CouponCode = couponCode,
            UserId = userId,
            BusinessOrderNo = subscription.SubscriptionNo,
            OrderAmount = plan.Price,
            Currency = plan.Currency,
            ProductType = ProductType.Subscription,
            ScopeId = plan.Id,
            SubscriptionId = subscription.Id
        }, cancellationToken);
    }

    /// <summary>
    /// 计算下次计费时间
    /// </summary>
    private static DateTime CalculateNextBillingTime(DateTime from, BillingCycleType cycleType, int cycleValue)
    {
        return cycleType switch
        {
            BillingCycleType.Day => from.AddDays(cycleValue),
            BillingCycleType.Week => from.AddDays(7 * cycleValue),
            BillingCycleType.Month => from.AddMonths(cycleValue),
            BillingCycleType.Year => from.AddYears(cycleValue),
            _ => from.AddMonths(1)
        };
    }

    /// <summary>
    /// 单条订阅转 DTO。
    /// </summary>
    /// <remarks>
    /// <c>HasPaymentMethod</c> 显式回填而不是只靠映射配置：那条配置的存在意义是让**列表查询**
    /// （<c>ProjectTo</c> → SQL 投影）也能得到这个字段；单条读取不该因为"映射配置恰好没注册"
    /// 就悄悄退化成 false —— 这个字段的语义是"能不能自动续费"，报错值比报错更隐蔽。
    /// </remarks>
    private static SubscriptionDto ToDto(Subscription subscription)
    {
        var dto = subscription.MapTo<SubscriptionDto>();
        dto.HasPaymentMethod = !string.IsNullOrWhiteSpace(subscription.PaymentMethodToken);
        return dto;
    }

    public async Task<Result> ReorderPlansAsync(IReadOnlyList<Guid> planIds, CancellationToken cancellationToken = default)
    {
        Check.NotNullOrEmpty(planIds);

        // 订阅计划是一条全局序列（定价页从上到下就是这个顺序），故不传范围谓词。
        var result = await ExecuteInUnitOfWorkAsync(
            ct => _planRepository.ReorderAsync(planIds, cancellationToken: ct),
            cancellationToken);

        if (!result.Succeeded)
            return Result.Failure(result.Message ?? "Reorder failed.", result.Code ?? 400, result.ErrorCode);

        LogInformation("Reordered {Count} subscription plan(s)", result.Data);
        return Ok();
    }

    /// <summary>「这个用户在这个产品下已经有一条有效订阅了」的统一答复。</summary>
    private Result<SubscriptionCreateResultDto> DuplicateSubscription(SubscriptionPlan plan)
        => Fail<SubscriptionCreateResultDto>(DuplicateSubscriptionCode(plan.ProductCode), 400);

    /// <inheritdoc cref="DuplicateSubscription"/>
    private Result DuplicateSubscriptionFor(string? productCode)
        => Fail(DuplicateSubscriptionCode(productCode), 400);

    private static string DuplicateSubscriptionCode(string? productCode)
        => productCode == null
            ? ErrorCodes.SubscriptionAlreadyActive
            : ErrorCodes.SubscriptionProductAlreadySubscribed;

    /// <summary>
    /// 订阅所属账户在同一产品（或同为无产品）下是否另有一条有效（非取消、非过期）订阅：与开通判重、与数据库的唯一索引同一个口径。
    /// </summary>
    private Task<bool> HasOtherLiveSubscriptionAsync(Subscription subscription, CancellationToken cancellationToken)
    {
        var id = subscription.Id;
        var accountType = subscription.BillingAccountType;
        var accountId = subscription.BillingAccountId;
        var productCode = subscription.ProductCode;

        return _subscriptionRepository.AnyAsync(
            s => s.Id != id
                && s.BillingAccountType == accountType
                && s.BillingAccountId == accountId
                && s.ProductCode == productCode
                && s.Status != SubscriptionStatus.Cancelled
                && s.Status != SubscriptionStatus.Expired,
            cancellationToken);
    }

    /// <summary>
    /// 写入订阅；被数据库的判重索引挡下时返回 false 而不是让异常冒出去。
    /// </summary>
    /// <remarks>
    /// 上面那次「有没有有效订阅」的查询与这次写入之间隔着绑卡、试算券、计税几步外部调用，
    /// 结账页一次双击落在这个窗口里两条请求都会读到「没有」。真正拦住第二条的是索引，
    /// 而索引抛出来的是 <c>DbUpdateException</c> —— 不翻译的话用户拿到的是 500，
    /// 和「服务器挂了」长得一模一样，而实际上这是一次完全正常、完全预期的拒绝。
    /// </remarks>
    private async Task<bool> TryInsertSubscriptionAsync(Subscription subscription, SubscriptionPlan plan, CancellationToken cancellationToken)
    {
        try
        {
            await _subscriptionRepository.InsertAsync(subscription, cancellationToken);
            return true;
        }
        catch (DbUpdateException ex)
        {
            Logger.LogInformation(ex,
                "Concurrent subscription creation refused by the uniqueness index. Account: {Account}, ProductCode: {ProductCode}",
                AccountOf(subscription), plan.ProductCode);

            // 这一条实体还挂在变更跟踪里且处于 Added，不丢掉它，下一次 SaveChanges 会把它重放在无关的位置上
            _subscriptionRepository.Discard(subscription);
            return false;
        }
    }

}
