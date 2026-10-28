using System.Net;
using System.Text;

namespace Tnzi.Payment.Stripe.Tests;

/// <summary>
/// 收银台用的 PaymentIntent 在线缆上发出去的支付方式参数：订阅收款只收卡，普通付款照账户后台启用的方式自动提供。
/// </summary>
/// <remarks>
/// 消费方实测：订阅开通与补缴的 Payment Element 列出了 Klarna。限制必须落在 PaymentIntent 上：
/// 前端只藏起来挡不住任何一个拿着 client secret 自己拉起收银台的客户端。
/// </remarks>
public class StripePaymentIntentMethodTypesTests
{
    [Fact]
    public async Task AReusableOnlyPayment_IsRestrictedToCards()
    {
        var stripe = new CapturingStripe();

        var result = await ProviderOver(stripe).CreatePaymentAsync(new PaymentProviderCreateDto
        {
            TradeNo = "PAY1",
            BusinessOrderNo = "SUB-1",
            Amount = 113m,
            Currency = "CAD",
            ProviderCustomerId = "cus_1",
            SavePaymentMethod = true,
            ReusableMethodsOnly = true
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        var form = stripe.PaymentIntentForm.ShouldNotBeNull();
        form.ShouldContainKeyAndValue("allowed_payment_method_types[0]", "card");
        form.Keys.ShouldNotContain(k => k.StartsWith("automatic_payment_methods", StringComparison.Ordinal),
            "automatic payment methods would offer Klarna and other one-time methods again");
        form.ShouldContainKeyAndValue("setup_future_usage", "off_session");
    }

    [Fact]
    public async Task AnOrdinaryPayment_KeepsAutomaticPaymentMethods()
    {
        var stripe = new CapturingStripe();

        await ProviderOver(stripe).CreatePaymentAsync(new PaymentProviderCreateDto
        {
            TradeNo = "PAY2",
            BusinessOrderNo = "ORDER-1",
            Amount = 20m,
            Currency = "CAD"
        });

        var form = stripe.PaymentIntentForm.ShouldNotBeNull();
        form.ShouldContainKeyAndValue("automatic_payment_methods[enabled]", "true");
        form.Keys.ShouldNotContain(k => k.StartsWith("allowed_payment_method_types", StringComparison.Ordinal));
    }

    private static StripeProvider ProviderOver(CapturingStripe stripe)
    {
        var secretKey = $"sk_test_{Guid.NewGuid():N}";
        StripeClientFactory.Use(secretKey, new global::Stripe.StripeClient(
            secretKey, httpClient: new global::Stripe.SystemNetHttpClient(new HttpClient(stripe))));

        var options = new OptionsWrapper<StripeOptions>(new StripeOptions
        {
            Enabled = true,
            SecretKey = secretKey,
            PublishableKey = "pk_test_key"
        });
        return new StripeProvider(options, new Mock<ILogger<StripeProvider>>().Object);
    }

    /// <summary>只应答 <c>POST /v1/payment_intents</c>，并把表单体按键值拆开留给断言。</summary>
    private sealed class CapturingStripe : HttpMessageHandler
    {
        public Dictionary<string, string>? PaymentIntentForm { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/v1/payment_intents")
            {
                var body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
                PaymentIntentForm = body.Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Select(pair => pair.Split('=', 2))
                    .ToDictionary(kv => WebUtility.UrlDecode(kv[0]), kv => kv.Length > 1 ? WebUtility.UrlDecode(kv[1]) : string.Empty);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"id\":\"pi_1\",\"object\":\"payment_intent\",\"client_secret\":\"pi_1_secret_x\",\"status\":\"requires_payment_method\"}",
                        Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"error\":{\"message\":\"unexpected\"}}", Encoding.UTF8, "application/json")
            };
        }
    }
}
