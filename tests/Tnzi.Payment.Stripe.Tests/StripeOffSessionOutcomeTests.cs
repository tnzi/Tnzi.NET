using System.Net;
using System.Text;

namespace Tnzi.Payment.Stripe.Tests;

/// <summary>
/// 无人值守扣款的结果要如实分成三类：已收款、确定被拒、说不准。
/// </summary>
/// <remarks>
/// 服务层把「被拒」落成失败并让订阅换一个流水号（也就换了幂等键）再扣一笔。
/// 把 Stripe 自己的 5xx（官方说法：结果不确定）也报成被拒，原来那笔若其实扣成功了，同一期就扣了两次。
/// </remarks>
public class StripeOffSessionOutcomeTests
{
    private static PaymentProviderChargeDto Charge(string tradeNo) => new()
    {
        TradeNo = tradeNo,
        BusinessOrderNo = "SUB-1",
        Amount = 10m,
        Currency = "USD",
        ProviderCustomerId = "cus_1",
        PaymentMethodToken = "pm_1"
    };

    [Fact]
    public async Task ACardDecline_IsReportedAsAFailedCharge()
    {
        var stripe = new StubStripe(HttpStatusCode.PaymentRequired,
            "{\"error\":{\"type\":\"card_error\",\"code\":\"card_declined\",\"message\":\"Your card was declined.\"," +
            "\"payment_intent\":{\"id\":\"pi_declined\",\"object\":\"payment_intent\",\"status\":\"requires_payment_method\",\"amount\":1000,\"currency\":\"usd\"}}}");

        var result = await ProviderOver(stripe).ChargeOffSessionAsync(Charge("T-DECLINE"));

        result.Succeeded.ShouldBeTrue();
        result.Data!.Status.ShouldBe(PaymentStatus.Failed);
        result.Data.ExternalTradeNo.ShouldBe("pi_declined");
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "api_error")]
    [InlineData(HttpStatusCode.Conflict, "idempotency_error")]
    public async Task AnIndeterminateStripeError_IsReportedAsAnUnknownOutcome(HttpStatusCode status, string type)
    {
        var stripe = new StubStripe(status, $"{{\"error\":{{\"type\":\"{type}\",\"message\":\"stub\"}}}}");

        var result = await ProviderOver(stripe).ChargeOffSessionAsync(Charge($"T-{(int)status}"));

        result.Succeeded.ShouldBeFalse();
        result.ErrorCode.ShouldNotBe(ErrorCodes.PaymentOffSessionNotAttempted);
    }

    [Fact]
    public async Task AProcessingIntent_IsReportedAsProcessing()
    {
        var stripe = new StubStripe(HttpStatusCode.OK,
            "{\"id\":\"pi_proc\",\"object\":\"payment_intent\",\"status\":\"processing\",\"amount\":1000,\"amount_received\":0,\"currency\":\"usd\"}");

        var result = await ProviderOver(stripe).ChargeOffSessionAsync(Charge("T-PROC"));

        result.Succeeded.ShouldBeTrue();
        result.Data!.Status.ShouldBe(PaymentStatus.Processing);
        result.Data.ExternalTradeNo.ShouldBe("pi_proc");
    }

    private static StripeProvider ProviderOver(StubStripe stripe)
    {
        var secretKey = $"sk_test_{Guid.NewGuid():N}";
        StripeClientFactory.Use(secretKey, new global::Stripe.StripeClient(
            secretKey, httpClient: new global::Stripe.SystemNetHttpClient(new HttpClient(stripe), maxNetworkRetries: 0)));

        var options = new OptionsWrapper<StripeOptions>(new StripeOptions
        {
            Enabled = true,
            SecretKey = secretKey,
            PublishableKey = "pk_test_key"
        });
        return new StripeProvider(options, new Mock<ILogger<StripeProvider>>().Object);
    }

    /// <summary>对创建 PaymentIntent 的请求一律回同一个答复。</summary>
    private sealed class StubStripe(HttpStatusCode status, string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path == "/v1/payment_intents")
            {
                return Task.FromResult(new HttpResponseMessage(status)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(
                    "{\"error\":{\"type\":\"invalid_request_error\",\"code\":\"resource_missing\",\"message\":\"stub: no such resource\"}}",
                    Encoding.UTF8, "application/json")
            });
        }
    }
}
