namespace Tnzi.Payment.Billing.Events;

/// <summary>
/// 支付完成 → 自动开票并发送。
/// </summary>
/// <remarks>
/// <para>
/// 拆分前这段逻辑是父模块 <c>PaymentCompletedEventHandler</c> 的后半段（可选注入
/// <c>IPaymentInvoiceService</c>，解析不到就 return）。搬来本模块之后它是**另一个独立处理器**，
/// 挂在同一个 <see cref="PaymentCompletedEvent"/> 上 —— 事件总线按
/// <c>IEnumerable&lt;IEventHandler&lt;TEvent&gt;&gt;</c> 解析，一个事件挂 N 个处理器是既有做法
/// （父模块自己就在这个事件上挂了两个）。
/// </para>
/// <para>
/// 这样拆的理由不是风格：留在父模块就意味着父模块引用 <c>IPaymentInvoiceService</c>，
/// 那是一条父 → 子的编译期依赖，父模块从此无法在不加载本包时构建。缺席时的表现也更诚实 ——
/// 本模块没加载，这个处理器根本不存在，日志里不会出现一句「发票服务未解析到」的噪音。
/// </para>
/// <para>
/// <b>刻意不吞异常</b>（沿拆分前的取舍）：开票失败应冒泡给事件总线，由其错误隔离 + 重试 + DLQ 兜底。
/// <c>CreateFromPaymentAsync</c> 按 <c>PaymentId</c> 幂等（应用层查重 + 数据库唯一索引兜底），
/// 重试不会产生第二张发票号。
/// </para>
/// </remarks>
public class InvoiceIssuingHandler : IEventHandler<PaymentCompletedEvent>
{
    private readonly ILogger<InvoiceIssuingHandler> _logger;
    private readonly IPaymentInvoiceService _invoiceService;
    private readonly IOptionsMonitor<InvoiceOptions> _invoiceOptions;

    public InvoiceIssuingHandler(
        ILogger<InvoiceIssuingHandler> logger,
        IPaymentInvoiceService invoiceService,
        IOptionsMonitor<InvoiceOptions> invoiceOptions)
    {
        _logger = Check.NotNull(logger);
        _invoiceService = Check.NotNull(invoiceService);
        _invoiceOptions = Check.NotNull(invoiceOptions);
    }

    public async Task HandleAsync(PaymentCompletedEvent eventData, CancellationToken cancellationToken = default)
    {
        // Payment:Invoice:Enabled=false 是「装了这个包但这套账不开票」，与「没装这个包」不同：
        // 前者仍然有发票的表与端点（可以手工开），后者连路由都不存在。
        if (_invoiceOptions.CurrentValue is not { Enabled: true } options)
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

        // 发送结果曾被整个丢弃：发票"生成了但一直发不出去"在日志里毫无痕迹。
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
