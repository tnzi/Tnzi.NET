namespace Tnzi.Payment.Subscriptions.Events;

// 订阅域的 9 个事件处理器：6 个日志型（创建 / 取消 / 过期 / 续费 / 计划变更 / 试用转正）
// 加 3 个把父模块的支付完成 / 失败 / 过期事件路由回订阅状态机的「回流」处理器。
//
// 全部逐字搬自父模块的 Events/Handlers/PaymentEventHandlers.cs，逻辑一字未改。
// ★ 三个回流处理器订阅的是**父模块的**事件（PaymentCompletedEvent / PaymentFailedEvent /
//   PaymentExpiredEvent）——「子 → 父」是合法方向，而且事件总线按
//   IEnumerable<IEventHandler<TEvent>> 解析，父模块自己那几个处理器不受影响。
// ★ 它们的 ISubscriptionService 参数保留了「可空 + 默认 null」的写法：那是拆分前
//   为「订阅服务可能没注册」留的口子，现在整个类型都随本模块走，实际永远解析得到。
//   保留不改是为了让搬迁是**纯移动**，可以逐行 diff。

/// <summary>
/// 订阅创建事件处理器
/// 记录订阅创建日志
/// </summary>
public class SubscriptionCreatedEventHandler : IEventHandler<SubscriptionCreatedEvent>
{
    private readonly ILogger<SubscriptionCreatedEventHandler> _logger;

    public SubscriptionCreatedEventHandler(ILogger<SubscriptionCreatedEventHandler> logger)
    {
        _logger = Check.NotNull(logger);
    }

    public async Task HandleAsync(SubscriptionCreatedEvent eventData, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Subscription created. SubscriptionNo: {SubscriptionNo}, UserId: {UserId}, Plan: {PlanName}, IsTrial: {IsTrial}",
            eventData.SubscriptionNo, eventData.UserId, eventData.PlanName, eventData.IsTrial);

        await Task.CompletedTask;
    }
}

/// <summary>
/// 订阅取消事件处理器
/// 记录订阅取消日志
/// </summary>
public class SubscriptionCancelledEventHandler : IEventHandler<SubscriptionCancelledEvent>
{
    private readonly ILogger<SubscriptionCancelledEventHandler> _logger;

    public SubscriptionCancelledEventHandler(ILogger<SubscriptionCancelledEventHandler> logger)
    {
        _logger = Check.NotNull(logger);
    }

    public async Task HandleAsync(SubscriptionCancelledEvent eventData, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Subscription cancelled. SubscriptionNo: {SubscriptionNo}, UserId: {UserId}, Immediate: {Immediate}, Reason: {Reason}",
            eventData.SubscriptionNo, eventData.UserId, eventData.Immediate, eventData.CancelReason);

        await Task.CompletedTask;
    }
}

/// <summary>
/// 订阅过期事件处理器
/// 记录订阅过期日志
/// </summary>
public class SubscriptionExpiredEventHandler : IEventHandler<SubscriptionExpiredEvent>
{
    private readonly ILogger<SubscriptionExpiredEventHandler> _logger;

    public SubscriptionExpiredEventHandler(ILogger<SubscriptionExpiredEventHandler> logger)
    {
        _logger = Check.NotNull(logger);
    }

    public async Task HandleAsync(SubscriptionExpiredEvent eventData, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Subscription expired. SubscriptionNo: {SubscriptionNo}, UserId: {UserId}, ExpiredTime: {ExpiredTime}",
            eventData.SubscriptionNo, eventData.UserId, eventData.ExpiredTime);

        await Task.CompletedTask;
    }
}

/// <summary>
/// 订阅续费事件处理器
/// 记录订阅续费日志
/// </summary>
public class SubscriptionRenewedEventHandler : IEventHandler<SubscriptionRenewedEvent>
{
    private readonly ILogger<SubscriptionRenewedEventHandler> _logger;

    public SubscriptionRenewedEventHandler(ILogger<SubscriptionRenewedEventHandler> logger)
    {
        _logger = Check.NotNull(logger);
    }

    public async Task HandleAsync(SubscriptionRenewedEvent eventData, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Subscription renewed. SubscriptionNo: {SubscriptionNo}, UserId: {UserId}, NewEndTime: {NewEndTime}, AutoRenew: {AutoRenew}",
            eventData.SubscriptionNo, eventData.UserId, eventData.NewEndTime, eventData.AutoRenew);

        await Task.CompletedTask;
    }
}

/// <summary>
/// 订阅计划变更事件处理器
/// 记录计划变更日志
/// </summary>
public class SubscriptionPlanChangedEventHandler : IEventHandler<SubscriptionPlanChangedEvent>
{
    private readonly ILogger<SubscriptionPlanChangedEventHandler> _logger;

    public SubscriptionPlanChangedEventHandler(ILogger<SubscriptionPlanChangedEventHandler> logger)
    {
        _logger = Check.NotNull(logger);
    }

    public async Task HandleAsync(SubscriptionPlanChangedEvent eventData, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Subscription plan changed. SubscriptionNo: {SubscriptionNo}, UserId: {UserId}, FromPlan: {FromPlanId}, ToPlan: {ToPlanId}, ChangeType: {ChangeType}, ProratedAmount: {ProratedAmount}, Immediate: {Immediate}",
            eventData.SubscriptionNo, eventData.UserId, eventData.FromPlanId, eventData.ToPlanId,
            eventData.ChangeType, eventData.ProratedAmount, eventData.Immediate);

        await Task.CompletedTask;
    }
}

/// <summary>
/// 订阅支付完成处理器：将订阅相关支付的完成回流到订阅状态机（激活/续费/试用转正/升级补差生效）
/// </summary>
/// <remarks>
/// 不再吞异常：状态机推进失败应冒泡给事件总线，由其重试 + DLQ 兜底。
/// 这直接缓解「扣款成功-推进失败-换新流水重扣」的 exactly-once 残留风险（见模块 Known Issues）。
/// </remarks>
public class SubscriptionPaymentCompletedHandler : IEventHandler<PaymentCompletedEvent>
{
    private readonly ILogger<SubscriptionPaymentCompletedHandler> _logger;
    private readonly ISubscriptionService? _subscriptionService;

    public SubscriptionPaymentCompletedHandler(
        ILogger<SubscriptionPaymentCompletedHandler> logger,
        ISubscriptionService? subscriptionService = null)
    {
        _logger = Check.NotNull(logger);
        _subscriptionService = subscriptionService;
    }

    public async Task HandleAsync(PaymentCompletedEvent eventData, CancellationToken cancellationToken = default)
    {
        if (eventData.BusinessType != BusinessType.Subscription || _subscriptionService == null)
            return;

        var meta = SubscriptionBillingMetadata.TryParse(eventData.ExtraData);
        if (meta == null)
            return;

        _logger.LogDebug("Applying subscription payment-completed. TradeNo: {TradeNo}, Purpose: {Purpose}", eventData.TradeNo, meta.Purpose);
        await _subscriptionService.ApplyPaymentCompletedAsync(new SubscriptionPaymentContext
        {
            Purpose = meta.Purpose,
            SubscriptionId = meta.SubscriptionId,
            SubscriptionNo = eventData.BusinessOrderNo,
            ChangeId = meta.ChangeId,
            PayerUserId = eventData.UserId,
            PaymentTradeNo = eventData.TradeNo,
            Amount = eventData.Amount,
            Currency = eventData.Currency
        }, cancellationToken);
    }
}

/// <summary>
/// 订阅支付失败处理器：续费/转正失败降级 PastDue，升级补差失败取消变更
/// </summary>
/// <remarks>不再吞异常：降级失败应冒泡给事件总线，由其重试 + DLQ 兜底。</remarks>
public class SubscriptionPaymentFailedHandler : IEventHandler<PaymentFailedEvent>
{
    private readonly ILogger<SubscriptionPaymentFailedHandler> _logger;
    private readonly ISubscriptionService? _subscriptionService;

    public SubscriptionPaymentFailedHandler(
        ILogger<SubscriptionPaymentFailedHandler> logger,
        ISubscriptionService? subscriptionService = null)
    {
        _logger = Check.NotNull(logger);
        _subscriptionService = subscriptionService;
    }

    public async Task HandleAsync(PaymentFailedEvent eventData, CancellationToken cancellationToken = default)
    {
        if (eventData.BusinessType != BusinessType.Subscription || _subscriptionService == null)
            return;

        var meta = SubscriptionBillingMetadata.TryParse(eventData.ExtraData);
        if (meta == null)
            return;

        _logger.LogDebug("Applying subscription payment-failed. TradeNo: {TradeNo}, Purpose: {Purpose}", eventData.TradeNo, meta.Purpose);
        await _subscriptionService.ApplyPaymentFailedAsync(new SubscriptionPaymentContext
        {
            Purpose = meta.Purpose,
            SubscriptionId = meta.SubscriptionId,
            SubscriptionNo = eventData.BusinessOrderNo,
            ChangeId = meta.ChangeId,
            PayerUserId = eventData.UserId,
            PaymentTradeNo = eventData.TradeNo,
            FailReason = eventData.FailReason
        }, cancellationToken);
    }
}

/// <summary>
/// 订阅支付过期处理器：未支付的订阅待支付订单过期视同失败（如升级补差待支付单过期则取消变更）
/// </summary>
/// <remarks>不再吞异常：状态推进失败应冒泡给事件总线，由其重试 + DLQ 兜底。</remarks>
public class SubscriptionPaymentExpiredHandler : IEventHandler<PaymentExpiredEvent>
{
    private readonly ILogger<SubscriptionPaymentExpiredHandler> _logger;
    private readonly ISubscriptionService? _subscriptionService;

    public SubscriptionPaymentExpiredHandler(
        ILogger<SubscriptionPaymentExpiredHandler> logger,
        ISubscriptionService? subscriptionService = null)
    {
        _logger = Check.NotNull(logger);
        _subscriptionService = subscriptionService;
    }

    public async Task HandleAsync(PaymentExpiredEvent eventData, CancellationToken cancellationToken = default)
    {
        if (eventData.BusinessType != BusinessType.Subscription || _subscriptionService == null)
            return;

        var meta = SubscriptionBillingMetadata.TryParse(eventData.ExtraData);
        if (meta == null)
            return;

        _logger.LogDebug("Applying subscription payment-expired. TradeNo: {TradeNo}, Purpose: {Purpose}", eventData.TradeNo, meta.Purpose);
        await _subscriptionService.ApplyPaymentFailedAsync(new SubscriptionPaymentContext
        {
            Purpose = meta.Purpose,
            SubscriptionId = meta.SubscriptionId,
            SubscriptionNo = eventData.BusinessOrderNo,
            ChangeId = meta.ChangeId,
            PayerUserId = eventData.UserId,
            PaymentTradeNo = eventData.TradeNo,
            FailReason = "Payment order expired"
        }, cancellationToken);
    }
}

/// <summary>
/// 试用转正事件处理器
/// 记录试用转正日志
/// </summary>
public class SubscriptionTrialConvertedEventHandler : IEventHandler<SubscriptionTrialConvertedEvent>
{
    private readonly ILogger<SubscriptionTrialConvertedEventHandler> _logger;

    public SubscriptionTrialConvertedEventHandler(ILogger<SubscriptionTrialConvertedEventHandler> logger)
    {
        _logger = Check.NotNull(logger);
    }

    public async Task HandleAsync(SubscriptionTrialConvertedEvent eventData, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Subscription trial converted. SubscriptionNo: {SubscriptionNo}, UserId: {UserId}, ConvertedTime: {ConvertedTime}",
            eventData.SubscriptionNo, eventData.UserId, eventData.ConvertedTime);

        await Task.CompletedTask;
    }
}
