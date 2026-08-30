namespace Tnzi.Payment.Events.Handlers;

/// <summary>
/// 支付完成事件处理器：记录支付完成日志。
/// </summary>
/// <remarks>
/// 拆分前它还负责「自动开票并发送」。那一半随发票域搬去了可选子模块
/// <c>Tnzi.Payment.Billing</c>，在那里是**另一个**挂在同一个 <see cref="PaymentCompletedEvent"/>
/// 上的处理器，而不是这一个的一段分支。
///
/// 为什么是新增一个处理器而不是把开票逻辑留在这里可选注入：留下就得让父模块引用
/// <c>IPaymentInvoiceService</c> 与 <c>InvoiceOptions</c> 两个子模块类型，父 → 子的编译期依赖
/// 一旦成立，本模块就再也不可能在不加载那个包时构建。事件总线本来就支持一个事件挂 N 个处理器
/// （本模块自己就在 <see cref="PaymentCompletedEvent"/> 上挂了两个：这一个与订阅计费回流的那一个）。
///
/// 缺席时退化成什么：**支付照常完成，只是不自动开票**。少一项能力，不是一次错误的支付。
/// </remarks>
public class PaymentCompletedEventHandler : IEventHandler<PaymentCompletedEvent>
{
    private readonly ILogger<PaymentCompletedEventHandler> _logger;

    public PaymentCompletedEventHandler(ILogger<PaymentCompletedEventHandler> logger)
    {
        _logger = Check.NotNull(logger);
    }

    public async Task HandleAsync(PaymentCompletedEvent eventData, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Payment completed. TradeNo: {TradeNo}, Amount: {Amount} {Currency}",
            eventData.TradeNo, eventData.Amount, eventData.Currency);

        await Task.CompletedTask;
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

// 订阅域的 9 个事件处理器（6 个日志型 Subscription*EventHandler + 3 个把支付完成/失败/过期
// 路由回订阅状态机的 SubscriptionPayment*Handler）随续费域搬去了可选子模块
// Tnzi.Payment.Subscriptions（SubscriptionEventHandlers.cs，逻辑一字不变）。
//
// 其中三个「回流」处理器订阅的是**本模块的**支付事件（PaymentCompleted / Failed / Expired），
// 由子模块自己注册 —— 事件总线按 IEnumerable<IEventHandler<TEvent>> 解析，
// 同一个事件挂 N 个处理器互不覆盖。方向是「子 → 父」，合法。
// 缺席时退化成：一笔标着 BusinessType.Subscription 的支付照常完成，只是没有任何订阅状态机
// 需要被推进（这台宿主根本没有订阅）。少一项能力，不是一次错误的支付。
