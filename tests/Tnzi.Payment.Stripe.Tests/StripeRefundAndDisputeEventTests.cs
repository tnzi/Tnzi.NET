using System.Net;
using System.Text;

namespace Tnzi.Payment.Stripe.Tests;

/// <summary>
/// Stripe 侧的退款与拒付：本模块发起的退款带着退款号（据此认出「是我自己退的」），
/// <c>charge.refunded</c> / <c>refund.*</c> / <c>charge.dispute.*</c> 翻译成按 PaymentIntent 定位支付的回调结果。
/// </summary>
public class StripeRefundAndDisputeEventTests
{
    private static string ApiVersion => global::Stripe.StripeConfiguration.ApiVersion;

    private static StripeOptions Options() => new()
    {
        Enabled = true,
        SecretKey = $"sk_test_{Guid.NewGuid():N}",
        WebhookSecret = "whsec_x",
        MetadataStampValue = "crm"
    };

    private static (StripeProvider Provider, RefundStripe Stripe) ProviderOver(StripeOptions options)
    {
        var stripe = new RefundStripe();
        StripeClientFactory.Use(options.SecretKey, new global::Stripe.StripeClient(
            options.SecretKey, httpClient: new global::Stripe.SystemNetHttpClient(new HttpClient(stripe))));
        return (new StripeProvider(new OptionsWrapper<StripeOptions>(options), new Mock<ILogger<StripeProvider>>().Object), stripe);
    }

    private static Dictionary<string, string> Event(string type, string objectJson) => new()
    {
        [PaymentConstants.CallbackRawBodyKey] = $$"""
        {
          "id": "evt_{{Guid.NewGuid():N}}",
          "object": "event",
          "api_version": "{{ApiVersion}}",
          "type": "{{type}}",
          "data": { "object": {{objectJson}} }
        }
        """
    };

    [Fact]
    public async Task ARefundWeMake_CarriesOurRefundNumberAndTheAppStamp()
    {
        var (provider, stripe) = ProviderOver(Options());

        var refunded = await provider.RefundAsync(new PaymentProviderRefundDto
        {
            TradeNo = "PAY-1",
            ExternalTradeNo = "pi_1",
            RefundNo = "RF-1",
            RefundAmount = 50m,
            Currency = "CAD",
            Reason = "goodwill"
        });

        refunded.Succeeded.ShouldBeTrue(refunded.Message);
        var form = stripe.RefundForm.ShouldNotBeNull();
        form.ShouldContainKeyAndValue("metadata[RefundNo]", "RF-1");
        form.ShouldContainKeyAndValue("metadata[app]", "crm");
    }

    [Fact]
    public async Task ChargeRefunded_ListsEveryRefundOnTheCharge_MarkingTheOnesWeMade()
    {
        var (provider, _) = ProviderOver(Options());

        var result = await provider.HandleCallbackAsync(Event("charge.refunded", """
        {
          "id": "ch_1", "object": "charge", "payment_intent": "pi_1", "amount": 33900, "currency": "cad",
          "refunds": { "object": "list", "has_more": false, "data": [
            { "id": "re_dashboard", "object": "refund", "amount": 10000, "currency": "cad", "status": "succeeded", "reason": "requested_by_customer", "metadata": {} },
            { "id": "re_ours", "object": "refund", "amount": 5000, "currency": "cad", "status": "pending", "metadata": { "RefundNo": "RF-9" } }
          ] }
        }
        """));

        result.Succeeded.ShouldBeTrue(result.Message);
        var callback = result.Data!;
        callback.Kind.ShouldBe(PaymentCallbackKind.ExternalRefund);
        callback.ExternalTradeNo.ShouldBe("pi_1");
        callback.Refunds!.Count.ShouldBe(2);

        var dashboard = callback.Refunds.Single(r => r.ExternalRefundNo == "re_dashboard");
        dashboard.LocalRefundNo.ShouldBeNull();
        dashboard.Amount.ShouldBe(100m);
        dashboard.Currency.ShouldBe("CAD");
        dashboard.Status.ShouldBe(RefundStatus.Succeeded);

        var ours = callback.Refunds.Single(r => r.ExternalRefundNo == "re_ours");
        ours.LocalRefundNo.ShouldBe("RF-9");
        ours.Status.ShouldBe(RefundStatus.Refunding);
    }

    [Fact]
    public async Task ChargeRefunded_WithoutTheEmbeddedList_AsksStripeForTheRefunds()
    {
        var (provider, stripe) = ProviderOver(Options());

        var result = await provider.HandleCallbackAsync(Event("charge.refunded", """
        { "id": "ch_2", "object": "charge", "payment_intent": "pi_2", "amount": 20000, "currency": "cad" }
        """));

        result.Succeeded.ShouldBeTrue(result.Message);
        stripe.ListedCharge.ShouldBe("ch_2");
        result.Data!.Refunds!.ShouldHaveSingleItem().ExternalRefundNo.ShouldBe("re_listed");
    }

    [Fact]
    public async Task RefundUpdated_IsOneRefundForItsPaymentIntent()
    {
        var (provider, _) = ProviderOver(Options());

        var result = await provider.HandleCallbackAsync(Event("refund.updated", """
        { "id": "re_3", "object": "refund", "payment_intent": "pi_3", "amount": 1500, "currency": "cad", "status": "failed", "metadata": {} }
        """));

        var callback = result.Data!;
        callback.Kind.ShouldBe(PaymentCallbackKind.ExternalRefund);
        callback.ExternalTradeNo.ShouldBe("pi_3");
        callback.Refunds!.ShouldHaveSingleItem().Status.ShouldBe(RefundStatus.Failed);
    }

    [Theory]
    [InlineData("needs_response", PaymentDisputeStatus.NeedsResponse)]
    [InlineData("under_review", PaymentDisputeStatus.UnderReview)]
    [InlineData("won", PaymentDisputeStatus.Won)]
    [InlineData("lost", PaymentDisputeStatus.Lost)]
    [InlineData("warning_needs_response", PaymentDisputeStatus.WarningNeedsResponse)]
    [InlineData("something_new", PaymentDisputeStatus.Unknown)]
    public async Task ChargeDispute_IsTheDisputeForItsPaymentIntent(string stripeStatus, PaymentDisputeStatus expected)
    {
        var (provider, _) = ProviderOver(Options());

        var result = await provider.HandleCallbackAsync(Event("charge.dispute.created", $$"""
        { "id": "dp_1", "object": "dispute", "charge": "ch_1", "payment_intent": "pi_1", "amount": 33900, "currency": "cad", "status": "{{stripeStatus}}", "reason": "fraudulent" }
        """));

        result.Succeeded.ShouldBeTrue(result.Message);
        var callback = result.Data!;
        callback.Kind.ShouldBe(PaymentCallbackKind.Dispute);
        callback.ExternalTradeNo.ShouldBe("pi_1");
        callback.Dispute!.DisputeId.ShouldBe("dp_1");
        callback.Dispute.Status.ShouldBe(expected);
        callback.Dispute.Amount.ShouldBe(339m);
        callback.Dispute.Reason.ShouldBe("fraudulent");
    }

    private sealed class RefundStripe : HttpMessageHandler
    {
        public Dictionary<string, string>? RefundForm { get; private set; }
        public string? ListedCharge { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path == "/v1/refunds")
            {
                var body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
                RefundForm = body.Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Select(pair => pair.Split('=', 2))
                    .ToDictionary(kv => WebUtility.UrlDecode(kv[0]), kv => kv.Length > 1 ? WebUtility.UrlDecode(kv[1]) : string.Empty);
                return Json("""{"id":"re_new","object":"refund","amount":5000,"currency":"cad","status":"succeeded"}""");
            }

            if (request.Method == HttpMethod.Get && path == "/v1/refunds")
            {
                ListedCharge = WebUtility.UrlDecode(request.RequestUri.Query).Split('&').Select(p => p.TrimStart('?')).FirstOrDefault(p => p.StartsWith("charge=", StringComparison.Ordinal))?["charge=".Length..];
                return Json("""{"object":"list","has_more":false,"data":[{"id":"re_listed","object":"refund","amount":20000,"currency":"cad","status":"succeeded","metadata":{}}]}""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"error\":{\"message\":\"unexpected\"}}", Encoding.UTF8, "application/json")
            };
        }

        private static HttpResponseMessage Json(string json)
            => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
