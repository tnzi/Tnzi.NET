namespace Tnzi.Payment.Billing.Events;

/// <summary>
/// 退款成功 → 开一张贷项通知单冲减原票据，并发给客户（<c>Payment:Invoice:IssueCreditNotes</c>，默认关闭）。
/// </summary>
/// <remarks>
/// 渠道后台手工退的款经渠道回调记下后同样发 <see cref="RefundProcessedEvent"/>，所以那些退款也有贷项通知单。
/// 不吞异常：开票失败交给事件总线重试；<see cref="IPaymentInvoiceService.CreateCreditNoteAsync"/> 按退款幂等，重试不会开出第二张。
/// 这笔支付当初没开过票时什么都不做（没有可冲减的东西）。
/// </remarks>
public class CreditNoteIssuingHandler : IEventHandler<RefundProcessedEvent>
{
    private readonly ILogger<CreditNoteIssuingHandler> _logger;
    private readonly IPaymentInvoiceService _invoiceService;
    private readonly IOptionsMonitor<InvoiceOptions> _invoiceOptions;

    /// <summary>DI 构造函数。</summary>
    public CreditNoteIssuingHandler(
        ILogger<CreditNoteIssuingHandler> logger,
        IPaymentInvoiceService invoiceService,
        IOptionsMonitor<InvoiceOptions> invoiceOptions)
    {
        _logger = Check.NotNull(logger);
        _invoiceService = Check.NotNull(invoiceService);
        _invoiceOptions = Check.NotNull(invoiceOptions);
    }

    /// <inheritdoc />
    public async Task HandleAsync(RefundProcessedEvent eventData, CancellationToken cancellationToken = default)
    {
        Check.NotNull(eventData);

        if (!eventData.Succeeded || _invoiceOptions.CurrentValue is not { Enabled: true, IssueCreditNotes: true } options)
            return;

        var issued = await _invoiceService.CreateCreditNoteAsync(new CreateCreditNoteDto
        {
            RefundId = eventData.RefundId,
            RefundNo = eventData.RefundNo,
            PaymentId = eventData.PaymentId,
            Amount = eventData.Amount,
            Currency = eventData.Currency,
            RefundTime = eventData.CompletedTime
        }, cancellationToken);

        if (!issued.Succeeded || issued.Data == null)
        {
            _logger.LogInformation("No credit note for refund {RefundNo} of {TradeNo}: {Reason}.",
                eventData.RefundNo, eventData.TradeNo, issued.Message);
            return;
        }

        if (!options.AutoSendOnPayment)
            return;

        // 首发语义同开票：发过就不再发，重投的同一事件不会让客户收到第二封
        var sent = await _invoiceService.SendFirstAsync(issued.Data.Id, cancellationToken);
        if (!sent.Succeeded)
        {
            _logger.LogError("Credit note {InvoiceNo} was issued but could not be sent: {Error}.",
                issued.Data.InvoiceNo, sent.Message);
        }
    }
}
