using System.Net;
using System.Text;

namespace Tnzi.Payment.Stripe.Tests;

/// <summary>
/// 本地关单 / 过期之前作废 PaymentIntent：能作废的作废；已经死的当成功（幂等）；已经付了的必须失败。
/// </summary>
/// <remarks>
/// PaymentIntent 不会自己过期（Checkout Session 才会）。此前本地关单只改本地状态，
/// 付款人几天后打开旧收银台照样付得进去，而成功回调撞上本地终态被静静吞掉。
/// 三种「作废不了」在 Stripe 那边长得一样（都是 <c>payment_intent_unexpected_state</c>），
/// 错误体里带着当前的 intent，按它的状态区分「已经死了」与「已经付了」。
/// </remarks>
public class StripePaymentIntentCancelTests
{
    [Fact]
    public void Capability_IsReported()
    {
        // PaymentService 按这个标志决定是先作废还是退回「查渠道状态」
        ProviderOver(new StubStripe()).SupportsPaymentCancellation.ShouldBeTrue();
    }

    [Fact]
    public async Task CancelPayment_VoidsTheIntent()
    {
        var stripe = new StubStripe().CancelSucceeds("pi_open");

        var result = await ProviderOver(stripe).CancelPaymentAsync("pi_open");

        result.Succeeded.ShouldBeTrue();
        stripe.Requests.ShouldContain("POST /v1/payment_intents/pi_open/cancel");
    }

    [Fact]
    public async Task CancelPayment_WhenTheIntentAlreadySucceeded_Fails()
    {
        // ★ 这条必须失败：服务层据此去查渠道状态并把这笔记成功，而不是把一笔已经扣了钱的单关掉
        var stripe = new StubStripe().CancelRefused("pi_paid", intentStatus: "succeeded");

        var result = await ProviderOver(stripe).CancelPaymentAsync("pi_paid");

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe(ErrorCodes.StripePaymentCancelFailed);
    }

    [Fact]
    public async Task CancelPayment_WhenTheIntentIsAlreadyCanceled_IsIdempotent()
    {
        var stripe = new StubStripe().CancelRefused("pi_dead", intentStatus: "canceled");

        (await ProviderOver(stripe).CancelPaymentAsync("pi_dead")).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task CancelPayment_WhenTheIntentDoesNotExist_IsIdempotent()
    {
        // 桩对未登记的路径一律 404：不存在的 intent 没有可付的东西
        (await ProviderOver(new StubStripe()).CancelPaymentAsync("pi_missing")).Succeeded.ShouldBeTrue();
    }

    // ── 夹具 ─────────────────────────────────────────────────────────────────

    private static StripeProvider ProviderOver(StubStripe stripe)
    {
        var secretKey = $"sk_test_{Guid.NewGuid():N}";
        StripeClientFactory.Use(secretKey, new global::Stripe.StripeClient(
            secretKey, httpClient: new global::Stripe.SystemNetHttpClient(new HttpClient(stripe))));

        var options = new OptionsWrapper<StripeOptions>(new StripeOptions
        {
            Enabled = true,
            SecretKey = secretKey,
            PublishableKey = "pk_test_key",
            Currency = "usd"
        });
        return new StripeProvider(options, new Mock<ILogger<StripeProvider>>().Object);
    }

    /// <summary>只认 POST payment_intents/{id}/cancel，其它一律 404。</summary>
    private sealed class StubStripe : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Status, string Json)> _responses = new(StringComparer.Ordinal);

        public List<string> Requests { get; } = [];

        public StubStripe CancelSucceeds(string id)
        {
            _responses[$"/v1/payment_intents/{id}/cancel"] = (HttpStatusCode.OK,
                $"{{\"id\":\"{id}\",\"object\":\"payment_intent\",\"status\":\"canceled\",\"amount\":1000,\"currency\":\"usd\"}}");
            return this;
        }

        public StubStripe CancelRefused(string id, string intentStatus)
        {
            _responses[$"/v1/payment_intents/{id}/cancel"] = (HttpStatusCode.BadRequest,
                "{\"error\":{\"type\":\"invalid_request_error\",\"code\":\"payment_intent_unexpected_state\"," +
                $"\"message\":\"You cannot cancel this PaymentIntent because it has a status of {intentStatus}.\"," +
                $"\"payment_intent\":{{\"id\":\"{id}\",\"object\":\"payment_intent\",\"status\":\"{intentStatus}\",\"amount\":1000,\"currency\":\"usd\"}}}}}}");
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add($"{request.Method} {path}");

            if (request.Method == HttpMethod.Post && _responses.TryGetValue(path, out var reply))
            {
                return Task.FromResult(new HttpResponseMessage(reply.Status)
                {
                    Content = new StringContent(reply.Json, Encoding.UTF8, "application/json")
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
