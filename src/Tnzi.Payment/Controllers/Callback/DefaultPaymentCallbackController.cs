namespace Tnzi.Payment.Controllers.Callback;

/// <summary>
/// 支付回调控制器基类
/// 处理来自支付渠道的异步通知回调
/// </summary>
[AllowAnonymous]
[ApiExplorerSettings(GroupName = "public")]
[Route("payments/callback")]
[DefaultController]
public class DefaultPaymentCallbackController : ApiControllerBase
{
    private readonly IPaymentService _paymentService;

    public DefaultPaymentCallbackController(IPaymentService paymentService)
    {
        _paymentService = Check.NotNull(paymentService);
    }

    protected IPaymentService PaymentService => _paymentService;

    /// <summary>
    /// Stripe Webhook 回调
    /// </summary>
    [HttpPost("stripe")]
    public virtual async Task<ApiResult> StripeCallback()
    {
        var parameters = await ReadCallbackParametersAsync();
        var result = await _paymentService.HandleCallbackAsync(new PaymentCallbackDto
        {
            ChannelCode = "Stripe",
            Parameters = parameters
        });
        return result.ToApiResult();
    }

    /// <summary>
    /// PayPal Webhook 回调
    /// </summary>
    [HttpPost("paypal")]
    public virtual async Task<ApiResult> PayPalCallback()
    {
        var parameters = await ReadCallbackParametersAsync();
        var result = await _paymentService.HandleCallbackAsync(new PaymentCallbackDto
        {
            ChannelCode = "PayPal",
            Parameters = parameters
        });
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

        if (!string.IsNullOrEmpty(body))
        {
            parameters["__raw_body"] = body;
        }

        // 添加重要的 Header 用于签名验证
        if (request.Headers.TryGetValue("Stripe-Signature", out var stripeSignature))
        {
            parameters["__stripe_signature"] = stripeSignature.ToString();
        }

        if (request.Headers.TryGetValue("PayPal-Transmission-Id", out var paypalTransmissionId))
            parameters["__paypal_transmission_id"] = paypalTransmissionId.ToString();

        if (request.Headers.TryGetValue("PayPal-Transmission-Time", out var paypalTransmissionTime))
            parameters["__paypal_transmission_time"] = paypalTransmissionTime.ToString();

        if (request.Headers.TryGetValue("PayPal-Transmission-Sig", out var paypalTransmissionSig))
            parameters["__paypal_transmission_sig"] = paypalTransmissionSig.ToString();

        if (request.Headers.TryGetValue("PayPal-Cert-Url", out var paypalCertUrl))
            parameters["__paypal_cert_url"] = paypalCertUrl.ToString();

        if (request.Headers.TryGetValue("PayPal-Auth-Algo", out var paypalAuthAlgo))
            parameters["__paypal_auth_algo"] = paypalAuthAlgo.ToString();

        // 如果是 form data，解析表单参数
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync();
            foreach (var item in form)
            {
                parameters[item.Key] = item.Value.ToString();
            }
        }
        else
        {
            // 尝试解析 JSON body 为 key-value 对
            try
            {
                var jsonDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body);
                if (jsonDict != null)
                {
                    foreach (var kvp in jsonDict)
                    {
                        parameters[kvp.Key] = kvp.Value.ToString();
                    }
                }
            }
            catch (JsonException)
            {
                // 非 JSON 格式，忽略
            }
        }

        return parameters;
    }
}
