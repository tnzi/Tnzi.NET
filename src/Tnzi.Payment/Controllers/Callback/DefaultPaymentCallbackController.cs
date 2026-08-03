namespace Tnzi.Payment.Controllers.Callback;

/// <summary>
/// 支付回调控制器：处理来自支付渠道的异步通知。
/// </summary>
/// <remarks>
/// 状态码语义对渠道是有约束力的契约，不能一律回 200：
/// <list type="bullet">
/// <item>2xx = 已妥善处理（含"与支付无关、无需处理"），渠道不再重投；</item>
/// <item>4xx = 确定性拒绝（验签失败、报文不合法、非本系统订单），重投也不会变好；</item>
/// <item>5xx = 暂时性故障（数据库不可用等），必须让渠道重投，否则这笔状态就永久丢了。</item>
/// </list>
/// 此前所有失败都经统一包装回 200，渠道会把它们全部记为投递成功，
/// 一次瞬时故障就意味着一笔支付永远停在"处理中"。
/// </remarks>
[AllowAnonymous]
[ApiExplorerSettings(GroupName = "public")]
[Route("payments/callback")]
[DefaultController]
public class DefaultPaymentCallbackController : ApiControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly ILogger<DefaultPaymentCallbackController> _logger;

    public DefaultPaymentCallbackController(
        IPaymentService paymentService,
        ILogger<DefaultPaymentCallbackController> logger)
    {
        _paymentService = Check.NotNull(paymentService);
        _logger = Check.NotNull(logger);
    }

    protected IPaymentService PaymentService => _paymentService;

    /// <summary>
    /// Stripe Webhook 回调
    /// </summary>
    [HttpPost("stripe")]
    public virtual Task<ApiResult> StripeCallback()
        => HandleAsync(PaymentConstants.StripeChannelCode);

    /// <summary>
    /// PayPal Webhook 回调
    /// </summary>
    [HttpPost("paypal")]
    public virtual Task<ApiResult> PayPalCallback()
        => HandleAsync(PaymentConstants.PayPalChannelCode);

    /// <summary>
    /// 回调处理主流程：解析参数 → 交给支付服务 → 返回信封。
    /// </summary>
    /// <remarks>
    /// HTTP 状态码**不在这里手工设置**：<c>ApiResult</c> 实现 <c>IConvertToActionResult</c>，
    /// 框架按信封的 <c>Code</c> 落传输状态码。因此状态码语义完全由服务层给出的
    /// <c>Result.Code</c> 决定（确定性拒绝 4xx / 暂时性故障以异常冒泡转 5xx），
    /// 控制器只负责把被拒的回调记成告警——渠道后台会显示投递失败，运维需要能对上号。
    /// </remarks>
    protected virtual async Task<ApiResult> HandleAsync(string channelCode)
    {
        var parameters = await ReadCallbackParametersAsync();

        var result = await _paymentService.HandleCallbackAsync(new PaymentCallbackDto
        {
            ChannelCode = channelCode,
            Parameters = parameters
        });

        if (!result.Succeeded)
        {
            _logger.LogWarning("Payment callback rejected. Channel: {Channel}, Status: {Status}, Reason: {Reason}",
                channelCode, result.Code ?? 400, result.Message);
        }

        return result.ToApiResult();
    }

    /// <summary>
    /// 从请求中读取回调参数
    /// 支持 JSON body 和 form data 两种方式
    /// </summary>
    protected virtual async Task<IDictionary<string, string>> ReadCallbackParametersAsync()
    {
        var parameters = new Dictionary<string, string>();
        var request = HttpContext.Request;

        // 保存原始请求体用于签名验证
        request.EnableBuffering();
        using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        request.Body.Position = 0;

        // 先解析报文，再写入保留键：报文里出现同名字段也顶不掉签名验证依赖的原始值
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync();
            foreach (var item in form)
            {
                if (IsReservedKey(item.Key))
                    continue;

                parameters[item.Key] = item.Value.ToString();
            }
        }
        else if (!string.IsNullOrEmpty(body))
        {
            try
            {
                var jsonDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body);
                if (jsonDict != null)
                {
                    foreach (var kvp in jsonDict)
                    {
                        if (IsReservedKey(kvp.Key))
                            continue;

                        parameters[kvp.Key] = kvp.Value.ToString();
                    }
                }
            }
            catch (JsonException)
            {
                // 非 JSON 格式，忽略：验签仍以原始报文为准
            }
        }

        if (!string.IsNullOrEmpty(body))
            parameters[PaymentConstants.CallbackRawBodyKey] = body;

        AddHeader(parameters, "Stripe-Signature", PaymentConstants.CallbackStripeSignatureKey);
        AddHeader(parameters, "PayPal-Transmission-Id", PaymentConstants.CallbackPayPalTransmissionIdKey);
        AddHeader(parameters, "PayPal-Transmission-Time", PaymentConstants.CallbackPayPalTransmissionTimeKey);
        AddHeader(parameters, "PayPal-Transmission-Sig", PaymentConstants.CallbackPayPalTransmissionSigKey);
        AddHeader(parameters, "PayPal-Cert-Url", PaymentConstants.CallbackPayPalCertUrlKey);
        AddHeader(parameters, "PayPal-Auth-Algo", PaymentConstants.CallbackPayPalAuthAlgoKey);

        return parameters;
    }

    private void AddHeader(IDictionary<string, string> parameters, string headerName, string parameterKey)
    {
        if (HttpContext.Request.Headers.TryGetValue(headerName, out var value))
            parameters[parameterKey] = value.ToString();
    }

    private static bool IsReservedKey(string key)
        => key.StartsWith(PaymentConstants.CallbackReservedKeyPrefix, StringComparison.Ordinal);
}
