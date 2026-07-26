namespace Tnzi.Finance.Banking.Events.Handlers;

/// <summary>
/// 付款作废联动作废支票处理器
/// </summary>
/// <remarks>
/// 订阅 <see cref="FinanceDocumentVoidedEvent"/>，仅对 <c>DocType == "PaymentEntry"</c> 生效：
/// 自动作废该付款关联的全部 Issued 支票（Posted 付款不可变，不回写 PaymentEntry.Reference）。
/// 不吞异常——作废失败冒泡给事件总线由其错误隔离 + 重试 + DLQ 兜底。
/// </remarks>
public class PaymentVoidedCheckHandler : IEventHandler<FinanceDocumentVoidedEvent>
{
    private readonly ILogger<PaymentVoidedCheckHandler> _logger;
    private readonly ICheckService _checkService;

    public PaymentVoidedCheckHandler(ILogger<PaymentVoidedCheckHandler> logger, ICheckService checkService)
    {
        _logger = Check.NotNull(logger);
        _checkService = Check.NotNull(checkService);
    }

    public async Task HandleAsync(FinanceDocumentVoidedEvent eventData, CancellationToken cancellationToken = default)
    {
        Check.NotNull(eventData);
        if (!string.Equals(eventData.DocType, FinanceSourceTypes.PaymentEntry, StringComparison.Ordinal))
            return;

        var result = await _checkService.VoidByPaymentAsync(eventData.DocId, $"Payment {eventData.Number} voided", cancellationToken);
        if (!result.Succeeded)
        {
            // 让失败冒泡以触发重试/DLQ（业务铁律：事件处理器不吞异常）
            throw new BusinessException(result.Message ?? "Failed to void checks for the voided payment.");
        }

        _logger.LogInformation("Voided checks for voided payment {PaymentId} ({Number}).", eventData.DocId, eventData.Number);
    }
}
