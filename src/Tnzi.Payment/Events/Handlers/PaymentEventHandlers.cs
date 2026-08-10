namespace Tnzi.Payment.Events.Handlers;

/// <summary>
/// 支付完成事件处理器
/// 自动生成发票并发送
/// </summary>
public class PaymentCompletedEventHandler : IEventHandler<PaymentCompletedEvent>
{
    private readonly ILogger<PaymentCompletedEventHandler> _logger;
    private readonly IPaymentInvoiceService? _invoiceService;
    private readonly IOptionsMonitor<InvoiceOptions>? _invoiceOptions;

    public PaymentCompletedEventHandler(
        ILogger<PaymentCompletedEventHandler> logger,
        IPaymentInvoiceService? invoiceService = null,
        IOptionsMonitor<InvoiceOptions>? invoiceOptions = null)
    {
        _logger = Check.NotNull(logger);
        _invoiceService = invoiceService;
        _invoiceOptions = invoiceOptions;
    }

    // 不再吞异常：发票自动生成失败应冒泡给事件总线，由其错误隔离 + 重试 + DLQ 兜底。
    // CreateFromPaymentAsync 已按 PaymentId 幂等，重试不会产生重复发票。
    public async Task HandleAsync(PaymentCompletedEvent eventData, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Payment completed. TradeNo: {TradeNo}, Amount: {Amount} {Currency}",
            eventData.TradeNo, eventData.Amount, eventData.Currency);

        if (_invoiceService == null || _invoiceOptions?.CurrentValue is not { Enabled: true } options)
            return;

        var invoiceResult = await _invoiceService.CreateFromPaymentAsync(
            eventData.PaymentId, null, cancellationToken);

        if (!invoiceResult.Succeeded || invoiceResult.Data == null)
        {
            _logger.LogWarning(
                "Invoice auto-creation failed. TradeNo: {TradeNo}, Error: {Error}",
                eventData.TradeNo, invoiceResult.Message);
            return;
        }

        _logger.LogInformation(
            "Invoice auto-created from payment. InvoiceId: {InvoiceId}, TradeNo: {TradeNo}",
            invoiceResult.Data.Id, eventData.TradeNo);

        if (!options.AutoSendOnPayment)
            return;

        // 发送结果此前被整个丢弃：发票"生成了但一直发不出去"在日志里毫无痕迹。
        // 这里显式记录失败，无收件人属于数据缺失（重试也没用），不抛出以免打满 DLQ。
        var sendResult = await _invoiceService.SendAsync(invoiceResult.Data.Id, null, null, cancellationToken);
        if (!sendResult.Succeeded)
        {
            _logger.LogError(
                "Invoice created but delivery failed. InvoiceId: {InvoiceId}, TradeNo: {TradeNo}, Error: {Error}",
                invoiceResult.Data.Id, eventData.TradeNo, sendResult.Message);
        }
    }
}

/// <summary>
/// 支付失败事件处理器
/// 记录支付失败日志
/// </summary>
public class PaymentFailedEventHandler : IEventHandler<PaymentFailedEvent>
{
    private readonly ILogger<PaymentFailedEventHandler> _logger;

    public PaymentFailedEventHandler(ILogger<PaymentFailedEventHandler> logger)
    {
        _logger = Check.NotNull(logger);
    }

    public async Task HandleAsync(PaymentFailedEvent eventData, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "Payment failed. TradeNo: {TradeNo}, ErrorCode: {ErrorCode}, Reason: {Reason}",
            eventData.TradeNo, eventData.ErrorCode, eventData.FailReason);

        await Task.CompletedTask;
    }
}

/// <summary>
/// 退款处理完成事件处理器
/// 记录退款结果日志
/// </summary>
public class RefundProcessedEventHandler : IEventHandler<RefundProcessedEvent>
{
    private readonly ILogger<RefundProcessedEventHandler> _logger;

    public RefundProcessedEventHandler(ILogger<RefundProcessedEventHandler> logger)
    {
        _logger = Check.NotNull(logger);
    }

    public async Task HandleAsync(RefundProcessedEvent eventData, CancellationToken cancellationToken = default)
    {
        if (eventData.Succeeded)
        {
            _logger.LogInformation(
                "Refund succeeded. RefundNo: {RefundNo}, Amount: {Amount} {Currency}",
                eventData.RefundNo, eventData.Amount, eventData.Currency);
        }
        else
        {
            _logger.LogWarning(
                "Refund failed. RefundNo: {RefundNo}, Reason: {Reason}",
                eventData.RefundNo, eventData.FailReason);
        }

        await Task.CompletedTask;
    }
}

/// <summary>
/// 支付过期事件处理器
/// 记录支付过期日志
/// </summary>
public class PaymentExpiredEventHandler : IEventHandler<PaymentExpiredEvent>
{
    private readonly ILogger<PaymentExpiredEventHandler> _logger;

    public PaymentExpiredEventHandler(ILogger<PaymentExpiredEventHandler> logger)
    {
        _logger = Check.NotNull(logger);
    }

    public async Task HandleAsync(PaymentExpiredEvent eventData, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Payment expired. TradeNo: {TradeNo}, BusinessOrderNo: {BusinessOrderNo}, ExpiredTime: {ExpiredTime}",
            eventData.TradeNo, eventData.BusinessOrderNo, eventData.ExpiredTime);

        await Task.CompletedTask;
    }
}

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
