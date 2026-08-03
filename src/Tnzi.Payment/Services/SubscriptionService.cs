namespace Tnzi.Payment.Services;

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
    private readonly ICouponService? _couponService;
    private readonly INotificationService? _notificationService;

    private const int BillingScanPageSize = 200;

    private PaymentOptions PaymentOptions => _paymentOptionsMonitor.CurrentValue;

    private SubscriptionOptions SubscriptionOptions => _paymentOptionsMonitor.CurrentValue.Subscription;

    public SubscriptionService(
        IRepository<Subscription, Guid> subscriptionRepository,
        IRepository<SubscriptionPlan, Guid> planRepository,
        IRepository<SubscriptionChange, Guid> changeRepository,
        IPaymentService paymentService,
        IPaymentProviderFactory paymentProviderFactory,
        IPaymentMethodService paymentMethodService,
        IOptionsMonitor<PaymentOptions> paymentOptionsMonitor,
        IServiceProvider serviceProvider,
        ICouponService? couponService = null,
        INotificationService? notificationService = null)
        : base(serviceProvider)
    {
        _subscriptionRepository = Check.NotNull(subscriptionRepository);
        _planRepository = Check.NotNull(planRepository);
        _changeRepository = Check.NotNull(changeRepository);
        _paymentService = Check.NotNull(paymentService);
        _paymentProviderFactory = Check.NotNull(paymentProviderFactory);
        _paymentMethodService = Check.NotNull(paymentMethodService);
        _paymentOptionsMonitor = Check.NotNull(paymentOptionsMonitor);
        _couponService = couponService;
        _notificationService = notificationService;
    }

    public async Task<Result<SubscriptionCreateResultDto>> CreateSubscriptionAsync(CreateSubscriptionDto request, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        var currentUser = GetRequiredCurrentUser();
        var userId = currentUser.Id!.Value;

        var plan = await _planRepository.FirstOrDefaultAsync(p => p.Id == request.PlanId, cancellationToken);
        if (plan == null)
            return Fail<SubscriptionCreateResultDto>(ErrorCodes.SubscriptionPlanNotFound, 404);

        if (!plan.IsActive)
            return Fail<SubscriptionCreateResultDto>(ErrorCodes.SubscriptionPlanNotActive, 400);

        // 判重按"用户 + 产品"：不同产品的订阅可以并存，
        // 此前只按用户判重，应用只要有第二个可订阅产品就会被误拦。
        var existingSubscription = await _subscriptionRepository.FirstOrDefaultAsync(
            s => s.UserId == userId
                && s.ProductCode == plan.ProductCode
                && s.Status != SubscriptionStatus.Cancelled
                && s.Status != SubscriptionStatus.Expired,
            cancellationToken);

        if (existingSubscription != null)
        {
            return Fail<SubscriptionCreateResultDto>(
                plan.ProductCode == null
                    ? ErrorCodes.SubscriptionAlreadyActive
                    : ErrorCodes.SubscriptionProductAlreadySubscribed,
                400);
        }

        var channelCode = string.IsNullOrWhiteSpace(request.ChannelCode)
            ? PaymentOptions.DefaultChannelCode
            : request.ChannelCode;

        var subscription = new Subscription
        {
            SubscriptionNo = Subscription.GenerateSubscriptionNo(),
            UserId = userId,
            CustomerName = currentUser.UserName,
            CustomerEmail = currentUser.Email,
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
        var bindResult = await ResolvePaymentMethodAsync(userId, channelCode, request.PaymentMethodId, request.PaymentMethodToken, currentUser, cancellationToken);
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

            await _subscriptionRepository.InsertAsync(subscription, cancellationToken);
        }
        else
        {
            subscription.DiscountAmount = couponDiscount;
            subscription.CouponId = couponResult.Data?.PromotionId;
            subscription.NextBillingTime = CalculateNextBillingTime(DateTime.UtcNow, plan.CycleType, plan.CycleValue);

            // 先落库拿到订阅ID，支付单的 BusinessOrderNo 用订阅号，回流时凭它找回订阅
            await _subscriptionRepository.InsertAsync(subscription, cancellationToken);

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
                    SubscriptionId = subscription.Id
                }.ToExtraData()
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

    public async Task<Result> CancelSubscriptionAsync(Guid subscriptionId, CancelSubscriptionDto request, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        var subscription = await FindOwnedAsync(subscriptionId, ownerUserId, cancellationToken);
        if (subscription == null)
            return Fail(ErrorCodes.SubscriptionNotFound, 404);

        if (subscription.Status == SubscriptionStatus.Cancelled || subscription.Status == SubscriptionStatus.Expired)
            return Fail(ErrorCodes.SubscriptionAlreadyCancelledOrExpired, 400);

        subscription.CancelReason = request.Reason;
        subscription.CancelTime = DateTime.UtcNow;

        if (request.Immediate)
        {
            subscription.Status = SubscriptionStatus.Cancelled;
            subscription.EndTime = DateTime.UtcNow;
            subscription.AutoRenew = false;
            // 立刻断开后台计费的抓取窗口，缩小"取消与在途扣款"的竞态面
            subscription.NextBillingTime = null;
        }
        else
        {
            // 到期后取消：关闭自动续费，到期自然过期
            subscription.Status = SubscriptionStatus.PendingRenewal;
            subscription.AutoRenew = false;
        }

        await _subscriptionRepository.UpdateAsync(subscription, cancellationToken);

        if (EventBus != null)
        {
            await EventBus.PublishAsync(new SubscriptionCancelledEvent
            {
                SubscriptionId = subscription.Id,
                SubscriptionNo = subscription.SubscriptionNo,
                UserId = subscription.UserId,
                CancelReason = request.Reason,
                Immediate = request.Immediate,
                ExpireTime = subscription.EndTime
            });
        }

        Logger.LogInformation("Subscription cancelled. SubscriptionNo: {SubscriptionNo}, Immediate: {Immediate}",
            subscription.SubscriptionNo, request.Immediate);

        return Ok();
    }

    public async Task<Result> PauseSubscriptionAsync(Guid subscriptionId, PauseSubscriptionDto request, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        var subscription = await FindOwnedAsync(subscriptionId, ownerUserId, cancellationToken);
        if (subscription == null)
            return Fail(ErrorCodes.SubscriptionNotFound, 404);

        if (subscription.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Trial))
            return Fail(ErrorCodes.SubscriptionCannotPause, 400);

        var now = DateTime.UtcNow;
        var maxPauseDays = SubscriptionOptions.MaxPauseDays;

        if (request.ResumeAt.HasValue && request.ResumeAt.Value <= now)
            return Fail(ErrorCodes.SubscriptionCannotPause, 400);

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

        await _subscriptionRepository.UpdateAsync(subscription, cancellationToken);

        Logger.LogInformation("Subscription paused. SubscriptionNo: {SubscriptionNo}, ResumeAt: {ResumeAt}",
            subscription.SubscriptionNo, subscription.PausedUntil);

        return Ok();
    }

    public async Task<Result> ResumeSubscriptionAsync(Guid subscriptionId, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var subscription = await FindOwnedAsync(subscriptionId, ownerUserId, cancellationToken);
        if (subscription == null)
            return Fail(ErrorCodes.SubscriptionNotFound, 404);

        if (subscription.Status is not (SubscriptionStatus.Cancelled or SubscriptionStatus.Paused or SubscriptionStatus.PendingRenewal))
            return Fail(ErrorCodes.SubscriptionCannotResume, 400);

        await ResumeInternalAsync(subscription, cancellationToken);

        Logger.LogInformation("Subscription resumed. SubscriptionNo: {SubscriptionNo}, NextBilling: {NextBilling}",
            subscription.SubscriptionNo, subscription.NextBillingTime);

        return Ok();
    }

    /// <summary>
    /// 恢复订阅的统一实现：清理终止痕迹并把计费时间拨回未来。
    /// </summary>
    /// <remarks>
    /// 此前只是把状态改回 Active，既不清 EndTime 也不重算 NextBillingTime：
    /// 立即取消（EndTime=now）的订阅恢复后仍带着过去的时间，下一轮扫描会立刻再把它过期掉。
    /// </remarks>
    private async Task ResumeInternalAsync(Subscription subscription, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        // 从暂停恢复：把暂停时长原样加回计费时间与试用截止，剩余周期分毫不差地还给用户。
        // 重算一个完整周期会把"暂停一天"变成"白送一个周期"。
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

        subscription.Status = subscription.TrialEndTime > now && subscription.TrialConvertedTime == null
            ? SubscriptionStatus.Trial
            : SubscriptionStatus.Active;

        subscription.AutoRenew = true;
        subscription.CancelReason = null;
        subscription.CancelTime = null;
        subscription.EndTime = null;
        subscription.PausedAt = null;
        subscription.PausedUntil = null;
        subscription.RenewalRetryCount = 0;
        subscription.PastDueSince = null;

        if (subscription.Status == SubscriptionStatus.Trial)
        {
            subscription.NextBillingTime = subscription.TrialEndTime;
        }
        else if (subscription.NextBillingTime == null || subscription.NextBillingTime <= now)
        {
            // 取消恢复（无暂停时长）或周期确已走完：从现在起算一个新周期
            subscription.NextBillingTime = CalculateNextBillingTime(now, subscription.CycleType, subscription.CycleValue);
        }

        await _subscriptionRepository.UpdateAsync(subscription, cancellationToken);
    }

    public async Task<Result<SubscriptionDto>> UpdatePaymentMethodAsync(Guid subscriptionId, AttachPaymentMethodDto request, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        Check.NotNull(request);

        var subscription = await FindOwnedAsync(subscriptionId, ownerUserId, cancellationToken);
        if (subscription == null)
            return Fail<SubscriptionDto>(ErrorCodes.SubscriptionNotFound, 404);

        var bindResult = await ResolvePaymentMethodAsync(
            subscription.UserId, subscription.ChannelCode, request.PaymentMethodId, request.PaymentMethodToken, CurrentUser, cancellationToken);

        if (!bindResult.Succeeded)
            return Fail<SubscriptionDto>(bindResult.Message ?? ErrorCodes.PaymentMethodBindingFailed, bindResult.Code ?? 400);

        if (bindResult.Data == null)
            return Fail<SubscriptionDto>(ErrorCodes.PaymentMethodNotFound, 404);

        ApplyPaymentMethod(subscription, bindResult.Data);
        await _subscriptionRepository.UpdateAsync(subscription, cancellationToken);

        Logger.LogInformation("Subscription payment method updated. SubscriptionNo: {SubscriptionNo}, Brand: {Brand}, Last4: {Last4}",
            subscription.SubscriptionNo, subscription.PaymentMethodBrand, subscription.PaymentMethodLast4);

        // 换卡的常见动机就是挽回一笔失败的续费，因此立刻重试一次而不是等下一轮扫描
        if (subscription.Status == SubscriptionStatus.PastDue)
            await RetryBillingInternalAsync(subscription, cancellationToken);

        return Ok(ToDto(subscription));
    }

    public async Task<Result> UpdateAutoRenewAsync(Guid subscriptionId, bool autoRenew, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var subscription = await FindOwnedAsync(subscriptionId, ownerUserId, cancellationToken);
        if (subscription == null)
            return Fail(ErrorCodes.SubscriptionNotFound, 404);

        subscription.AutoRenew = autoRenew;
        if (!autoRenew && subscription.Status == SubscriptionStatus.Active)
        {
            subscription.Status = SubscriptionStatus.PendingRenewal;
        }
        else if (autoRenew && subscription.Status == SubscriptionStatus.PendingRenewal)
        {
            subscription.Status = SubscriptionStatus.Active;
        }

        await _subscriptionRepository.UpdateAsync(subscription, cancellationToken);

        Logger.LogInformation("Subscription auto-renewal updated. SubscriptionNo: {SubscriptionNo}, AutoRenew: {AutoRenew}",
            subscription.SubscriptionNo, autoRenew);

        return Ok();
    }

    public async Task<Result> RetryBillingAsync(Guid subscriptionId, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var subscription = await _subscriptionRepository
            .Where(s => s.Id == subscriptionId && (!ownerUserId.HasValue || s.UserId == ownerUserId.Value))
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(cancellationToken);

        if (subscription == null)
            return Fail(ErrorCodes.SubscriptionNotFound, 404);

        if (subscription.Status != SubscriptionStatus.PastDue)
            return Fail(ErrorCodes.SubscriptionCannotRetryBilling, 400);

        if (string.IsNullOrWhiteSpace(subscription.PaymentMethodToken))
            return Fail(ErrorCodes.SubscriptionPaymentMethodMissing, 400);

        await RetryBillingInternalAsync(subscription, cancellationToken);
        return Ok();
    }

    public async Task<Result<SubscriptionDto>> GetSubscriptionAsync(Guid id, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var subscription = await FindOwnedAsync(id, ownerUserId, cancellationToken);
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

    public async Task<Result<IPagedList<SubscriptionDto>>> GetUserSubscriptionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var pagedList = await _subscriptionRepository.AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreationTime)
            .ProjectTo<Subscription, SubscriptionDto>()
            .CreateAsync(1, 100, cancellationToken);

        return Ok(pagedList);
    }

    public async Task<Result<IPagedList<SubscriptionDto>>> GetSubscriptionListAsync(SubscriptionQueryDto query, Guid? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);

        var queryable = _subscriptionRepository.AsNoTracking().Filter(query);

        if (ownerUserId.HasValue)
            queryable = queryable.Where(s => s.UserId == ownerUserId.Value);

        var pagedList = await queryable
            .OrderByDescending(s => s.CreationTime)
            .ProjectTo<Subscription, SubscriptionDto>()
            .CreateAsync(query.PageIndex, query.PageSize, cancellationToken);

        return Ok(pagedList);
    }

    public async Task<Result<List<SubscriptionPlanDto>>> GetSubscriptionPlansAsync(bool activeOnly = true, string? productCode = null, CancellationToken cancellationToken = default)
    {
        var queryable = _planRepository.AsNoTracking();

        if (activeOnly)
            queryable = queryable.Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(productCode))
            queryable = queryable.Where(p => p.ProductCode == productCode);

        var plans = await queryable.OrderBy(p => p.SortOrder).ToListAsync(cancellationToken);

        return Ok(plans.MapToList<SubscriptionPlanDto>());
    }

    public async Task<Result<SubscriptionPlanDto>> CreatePlanAsync(SubscriptionPlanDto planDto, CancellationToken cancellationToken = default)
    {
        Check.NotNull(planDto);

        if (planDto.Price < 0)
            return Fail<SubscriptionPlanDto>(ErrorCodes.PaymentInvalidAmount, 400);

        if (planDto.CycleValue <= 0 && planDto.CycleType != BillingCycleType.OneTime)
            return Fail<SubscriptionPlanDto>(ErrorCodes.PaymentInvalidAmount, 400);

        var duplicated = await _planRepository.AnyAsync(p => p.PlanCode == planDto.PlanCode, cancellationToken);
        if (duplicated)
            return Fail<SubscriptionPlanDto>(ErrorCodes.PromotionCodeAlreadyExists, 409);

        var plan = planDto.MapTo<SubscriptionPlan>();
        await _planRepository.InsertAsync(plan, cancellationToken);

        Logger.LogInformation("Subscription plan created. PlanName: {PlanName}", plan.PlanName);

        return Ok(plan.MapTo<SubscriptionPlanDto>());
    }

    public async Task<Result> UpdatePlanAsync(Guid planId, SubscriptionPlanDto planDto, CancellationToken cancellationToken = default)
    {
        Check.NotNull(planDto);

        var plan = await _planRepository.FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);
        if (plan == null)
            return Fail(ErrorCodes.SubscriptionPlanNotFound, 404);

        plan.PlanName = planDto.PlanName;
        plan.ProductCode = planDto.ProductCode;
        plan.Description = planDto.Description;
        plan.Price = planDto.Price;
        plan.Currency = planDto.Currency;
        plan.CycleType = planDto.CycleType;
        plan.CycleValue = planDto.CycleValue;
        plan.TrialDays = planDto.TrialDays;
        plan.IsActive = planDto.IsActive;
        plan.AllowTrial = planDto.AllowTrial;
        plan.TrialDiscount = planDto.TrialDiscount;
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
        var activeSubscriptions = await _subscriptionRepository.CountAsync(
            s => s.PlanId == planId && s.Status != SubscriptionStatus.Cancelled && s.Status != SubscriptionStatus.Expired,
            cancellationToken);

        if (activeSubscriptions > 0)
            return Fail(ErrorCodes.SubscriptionPlanHasActiveSubscriptions, 400);

        plan.IsActive = false;
        await _planRepository.UpdateAsync(plan, cancellationToken);

        Logger.LogInformation("Subscription plan deactivated. PlanId: {PlanId}", planId);

        return Ok();
    }

    private Task<Subscription?> FindOwnedAsync(Guid subscriptionId, Guid? ownerUserId, CancellationToken cancellationToken)
    {
        return _subscriptionRepository.FirstOrDefaultAsync(
            s => s.Id == subscriptionId && (!ownerUserId.HasValue || s.UserId == ownerUserId.Value), cancellationToken);
    }

    private static bool ShouldStartTrial(CreateSubscriptionDto request, SubscriptionPlan plan)
        => request.EnableTrial && plan.AllowTrial;

    /// <summary>
    /// 解析本次订阅要用的支付方式：显式ID &gt; 渠道 token &gt; 用户默认卡。
    /// 三者都没有时返回 null（允许无卡开通，但自动续费届时会失败并进入催款）。
    /// </summary>
    private async Task<Result<StoredPaymentMethod?>> ResolvePaymentMethodAsync(
        Guid userId,
        string channelCode,
        Guid? paymentMethodId,
        string? paymentMethodToken,
        ICurrentUser? currentUser,
        CancellationToken cancellationToken)
    {
        if (paymentMethodId.HasValue)
        {
            var method = await _paymentMethodService.FindByIdAsync(userId, paymentMethodId.Value, cancellationToken);
            return method == null
                ? Result.Failure<StoredPaymentMethod?>(ErrorCodes.PaymentMethodNotFound, 404)
                : Result.Success<StoredPaymentMethod?>(method);
        }

        if (!string.IsNullOrWhiteSpace(paymentMethodToken))
        {
            var bound = await _paymentMethodService.BindEntityAsync(
                userId, channelCode, paymentMethodToken, currentUser?.UserName, currentUser?.Email, setAsDefault: true, cancellationToken);

            return bound.Succeeded && bound.Data != null
                ? Result.Success<StoredPaymentMethod?>(bound.Data)
                : Result.Failure<StoredPaymentMethod?>(bound.Message ?? ErrorCodes.PaymentMethodBindingFailed, bound.Code ?? 400);
        }

        var defaultMethod = await _paymentMethodService.FindDefaultAsync(userId, channelCode, cancellationToken);
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
}
