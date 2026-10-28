using System.Net;
using System.Text;

namespace Tnzi.Payment.Stripe.Tests;

/// <summary>
/// 绑卡用的 SetupIntent 在线缆上发出去的参数。
/// </summary>
/// <remarks>
/// Stripe API 2026-08-26 起，SetupIntent / PaymentIntent 的创建与更新<b>拒收</b> <c>payment_method_types</c>
/// （400 <c>payment_method_types_no_longer_supported</c>），替代参数是 <c>allowed_payment_method_types</c>。
/// SDK 升到锁定新 API 版本的大版本后，旧字段若还在请求里，每一次绑卡都会失败；
/// 而本渠道没有任何别的测试会发出这个请求，所以这里用走桩 HttpClient 的 <c>StripeClient</c> 截下请求体直接断言。
/// </remarks>
public class StripeSetupIntentTests
{
    [Fact]
    public async Task CreateSetupSession_RestrictsToCardsThroughAllowedPaymentMethodTypes()
    {
        var stripe = new CapturingStripe();
        var provider = ProviderOver(stripe);

        var result = await provider.CreateSetupSessionAsync(new PaymentProviderSetupDto
        {
            UserId = Guid.NewGuid(),
            ProviderCustomerId = "cus_existing"
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.SetupId.ShouldBe("seti_1");

        var form = stripe.SetupIntentForm.ShouldNotBeNull("no POST /v1/setup_intents was sent");
        form.ShouldContainKeyAndValue("allowed_payment_method_types[0]", "card");
        form.ShouldContainKeyAndValue("usage", "off_session");
        form.ShouldContainKeyAndValue("customer", "cus_existing");
        form.Keys.ShouldNotContain(k => k.StartsWith("payment_method_types", StringComparison.Ordinal),
            "payment_method_types is rejected by the pinned Stripe API version");
    }

    // ── 夹具 ─────────────────────────────────────────────────────────────────

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

    /// <summary>只应答 <c>POST /v1/setup_intents</c>，并把表单体按键值拆开留给断言。</summary>
    private sealed class CapturingStripe : HttpMessageHandler
    {
        public Dictionary<string, string>? SetupIntentForm { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/v1/setup_intents")
            {
                var body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
                SetupIntentForm = body.Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Select(pair => pair.Split('=', 2))
                    .ToDictionary(kv => WebUtility.UrlDecode(kv[0]), kv => kv.Length > 1 ? WebUtility.UrlDecode(kv[1]) : string.Empty);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"id\":\"seti_1\",\"object\":\"setup_intent\",\"client_secret\":\"seti_1_secret_x\",\"status\":\"requires_payment_method\"}",
                        Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(
                    "{\"error\":{\"type\":\"invalid_request_error\",\"message\":\"stub: no such resource\"}}",
                    Encoding.UTF8, "application/json")
            };
        }
    }
}
