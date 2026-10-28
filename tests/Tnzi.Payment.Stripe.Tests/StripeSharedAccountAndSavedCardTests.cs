using System.Net;
using System.Text;

namespace Tnzi.Payment.Stripe.Tests;

/// <summary>
/// 与另一个应用共用同一个 Stripe 账户（应用标记 + 账单描述后缀），以及首付时把卡留下来（<c>setup_future_usage</c>）。
/// </summary>
/// <remarks>
/// 请求体用走桩 HttpClient 截下来直接断言：这些参数写错不会让任何一次调用失败，
/// 只会让对方应用的 webhook 落到这里、对账单上看不出是哪个产品、第一次续费无卡可扣。
/// </remarks>
public class StripeSharedAccountAndSavedCardTests
{
    private static readonly BillingAccountRef Firm = new(BillingAccountTypes.Installation, "firm-1");

    private static StripeOptions Options(string? stamp = "crm", string? suffix = "CRMSUITE") => new()
    {
        Enabled = true,
        SecretKey = $"sk_test_{Guid.NewGuid():N}",
        PublishableKey = "pk_test_shared",
        WebhookSecret = "whsec_x",
        MetadataStampValue = stamp,
        StatementDescriptorSuffix = suffix
    };

    private static (StripeProvider Provider, CapturingStripe Stripe) ProviderOver(StripeOptions options)
    {
        var stripe = new CapturingStripe();
        StripeClientFactory.Use(options.SecretKey, new global::Stripe.StripeClient(
            options.SecretKey, httpClient: new global::Stripe.SystemNetHttpClient(new HttpClient(stripe))));
        return (new StripeProvider(new OptionsWrapper<StripeOptions>(options), new Mock<ILogger<StripeProvider>>().Object), stripe);
    }

    [Fact]
    public async Task AFirstPaymentThatSavesTheCard_CreatesTheAccountsCustomer_AndAsksStripeToKeepTheCard()
    {
        var (provider, stripe) = ProviderOver(Options());

        var created = await provider.CreatePaymentAsync(new PaymentProviderCreateDto
        {
            TradeNo = "PAY-1",
            BusinessOrderNo = "SUB-1",
            Amount = 339m,
            Currency = "CAD",
            SavePaymentMethod = true,
            BillingAccount = Firm,
            CustomerName = "Smith & Partners LLP",
            CustomerEmail = "billing@smith.example"
        });

        created.Succeeded.ShouldBeTrue(created.Message);

        var customer = stripe.Form("/v1/customers").ShouldNotBeNull("a card can only be kept on a customer");
        customer.ShouldContainKeyAndValue("metadata[BillingAccount]", "Installation:firm-1");
        customer.ShouldContainKeyAndValue("metadata[app]", "crm");
        customer.Keys.ShouldNotContain("metadata[UserId]", "an installation account belongs to no single user");

        var intent = stripe.Form("/v1/payment_intents").ShouldNotBeNull();
        intent.ShouldContainKeyAndValue("customer", "cus_new");
        intent.ShouldContainKeyAndValue("setup_future_usage", "off_session");
        intent.ShouldContainKeyAndValue("statement_descriptor_suffix", "CRMSUITE");
        intent.ShouldContainKeyAndValue("metadata[app]", "crm");
        intent.ShouldContainKeyAndValue("metadata[TradeNo]", "PAY-1");
        intent.ShouldContainKeyAndValue("amount", "33900");
    }

    [Fact]
    public async Task WithTheDefaults_NothingIsStamped_AndNoCardIsKept()
    {
        var (provider, stripe) = ProviderOver(Options(stamp: null, suffix: null));

        var created = await provider.CreatePaymentAsync(new PaymentProviderCreateDto
        {
            TradeNo = "PAY-2",
            BusinessOrderNo = "ORDER-2",
            Amount = 10m,
            Currency = "USD"
        });

        created.Succeeded.ShouldBeTrue(created.Message);
        stripe.Form("/v1/customers").ShouldBeNull();
        var intent = stripe.Form("/v1/payment_intents").ShouldNotBeNull();
        intent.Keys.ShouldNotContain("setup_future_usage");
        intent.Keys.ShouldNotContain("statement_descriptor_suffix");
        intent.Keys.ShouldNotContain(k => k.StartsWith("metadata[app]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OffSessionCharges_CarryTheStampAndTheDescriptorSuffix()
    {
        var (provider, stripe) = ProviderOver(Options());

        await provider.ChargeOffSessionAsync(new PaymentProviderChargeDto
        {
            TradeNo = "PAY-3",
            BusinessOrderNo = "SUB-3",
            Amount = 339m,
            Currency = "CAD",
            ProviderCustomerId = "cus_1",
            PaymentMethodToken = "pm_1"
        });

        var intent = stripe.Form("/v1/payment_intents").ShouldNotBeNull();
        intent.ShouldContainKeyAndValue("metadata[app]", "crm");
        intent.ShouldContainKeyAndValue("statement_descriptor_suffix", "CRMSUITE");
        intent.ShouldContainKeyAndValue("metadata[OffSession]", "true");
    }

    [Fact]
    public void ThePublishableKey_IsOfferedToTheBrowser_AndAnEmptyOneIsNot()
    {
        ProviderOver(Options()).Provider.PublishableKey.ShouldBe("pk_test_shared");

        var noKey = Options();
        noKey.PublishableKey = string.Empty;
        ProviderOver(noKey).Provider.PublishableKey.ShouldBeNull();
    }

    // ── webhook ──────────────────────────────────────────────────────────

    private static string ApiVersion => global::Stripe.StripeConfiguration.ApiVersion;

    private static Dictionary<string, string> IntentEvent(string metadataJson, string status = "succeeded", string? setupFutureUsage = null, string? paymentMethod = null) => new()
    {
        [PaymentConstants.CallbackRawBodyKey] = $$"""
        {
          "id": "evt_shared_1",
          "object": "event",
          "api_version": "{{ApiVersion}}",
          "type": "payment_intent.succeeded",
          "data": {
            "object": {
              "id": "pi_shared_1",
              "object": "payment_intent",
              "status": "{{status}}",
              "amount": 33900,
              "amount_received": 33900,
              "currency": "cad",
              {{(setupFutureUsage == null ? "" : $"\"setup_future_usage\": \"{setupFutureUsage}\",")}}
              {{(paymentMethod == null ? "" : $"\"payment_method\": \"{paymentMethod}\",")}}
              "metadata": {{metadataJson}}
            }
          }
        }
        """
    };

    [Fact]
    public async Task AnIntentFromTheOtherApplication_IsIgnoredQuietly()
    {
        var (provider, _) = ProviderOver(Options());

        // 另一个同样基于本框架的应用：带着 TradeNo，却是它自己的单
        var foreign = await provider.HandleCallbackAsync(IntentEvent("""{ "TradeNo": "PAY-OTHER", "app": "practice" }"""));
        foreign.Succeeded.ShouldBeTrue();
        foreign.Data!.IsHandled.ShouldBeFalse("answering 404 for someone else's payment would make Stripe redeliver it for days");

        var unstamped = await provider.HandleCallbackAsync(IntentEvent("""{ "TradeNo": "PAY-OTHER-2" }"""));
        unstamped.Data!.IsHandled.ShouldBeFalse();

        var mine = await provider.HandleCallbackAsync(IntentEvent("""{ "TradeNo": "PAY-MINE", "app": "crm" }"""));
        mine.Data!.IsHandled.ShouldBeTrue();
        mine.Data.TradeNo.ShouldBe("PAY-MINE");
    }

    [Fact]
    public async Task WithoutAStamp_EveryIntentIsHandledAsBefore()
    {
        var (provider, _) = ProviderOver(Options(stamp: null));

        var result = await provider.HandleCallbackAsync(IntentEvent("""{ "TradeNo": "PAY-ANY", "app": "practice" }"""));

        result.Data!.IsHandled.ShouldBeTrue();
        result.Data.TradeNo.ShouldBe("PAY-ANY");
    }

    [Fact]
    public async Task ASucceededIntentThatKeptTheCard_ReportsTheSavedPaymentMethod()
    {
        var (provider, _) = ProviderOver(Options());

        var kept = await provider.HandleCallbackAsync(IntentEvent(
            """{ "TradeNo": "PAY-SAVE", "app": "crm" }""", setupFutureUsage: "off_session", paymentMethod: "pm_kept"));
        kept.Data!.SavedPaymentMethodToken.ShouldBe("pm_kept");

        var notKept = await provider.HandleCallbackAsync(IntentEvent(
            """{ "TradeNo": "PAY-NOSAVE", "app": "crm" }""", paymentMethod: "pm_once"));
        notKept.Data!.SavedPaymentMethodToken.ShouldBeNull("a one-off payment's card is not ours to keep");
    }

    private static Dictionary<string, string> PaymentMethodEvent(string type) => new()
    {
        [PaymentConstants.CallbackRawBodyKey] = $$"""
        {
          "id": "evt_pm_1",
          "object": "event",
          "api_version": "{{ApiVersion}}",
          "type": "{{type}}",
          "data": {
            "object": {
              "id": "pm_updated",
              "object": "payment_method",
              "type": "card",
              "customer": "cus_1",
              "card": { "brand": "visa", "last4": "1881", "exp_month": 4, "exp_year": 2031 }
            }
          }
        }
        """
    };

    [Theory]
    [InlineData("payment_method.updated")]
    [InlineData("payment_method.automatically_updated")]
    public async Task ACardUpdate_IsReportedWithTheNewDetails(string type)
    {
        var (provider, _) = ProviderOver(Options());

        var result = await provider.HandleCallbackAsync(PaymentMethodEvent(type));

        result.Data!.Kind.ShouldBe(PaymentCallbackKind.PaymentMethodUpdated);
        result.Data.IsHandled.ShouldBeTrue();
        var card = result.Data.UpdatedPaymentMethod.ShouldNotBeNull();
        card.Token.ShouldBe("pm_updated");
        card.Last4.ShouldBe("1881");
        card.ExpiryMonth.ShouldBe(4);
        card.ExpiryYear.ShouldBe(2031);
    }

    [Fact]
    public async Task AnAttachedCard_IsNeitherARevocationNorAnUpdate()
    {
        var (provider, _) = ProviderOver(Options());

        var result = await provider.HandleCallbackAsync(PaymentMethodEvent("payment_method.attached"));

        result.Data!.IsHandled.ShouldBeFalse();
    }

    // ── 客户归属 ─────────────────────────────────────────────────────────

    [Fact]
    public void CustomerOwnership_FollowsTheBillingAccount_AndRejectsTheOtherApplicationsCustomer()
    {
        var options = Options();
        global::Stripe.Customer Customer(Dictionary<string, string> metadata) => new() { Id = "cus_x", Metadata = metadata };

        StripeProvider.DescribeCustomerOwnershipViolation(
            Customer(new() { ["BillingAccount"] = "Installation:firm-1", ["app"] = "crm" }), Firm, options).ShouldBeNull();

        StripeProvider.DescribeCustomerOwnershipViolation(
            Customer(new() { ["BillingAccount"] = "Installation:firm-2" }), Firm, options).ShouldNotBeNull();

        // 两套安装都叫 Installation:default 时，靠应用标记分得清
        StripeProvider.DescribeCustomerOwnershipViolation(
            Customer(new() { ["BillingAccount"] = "Installation:firm-1", ["app"] = "practice" }), Firm, options).ShouldNotBeNull();

        // 引入计费账户之前建的个人客户只带 UserId：个人账户照旧认得
        var userId = Guid.NewGuid();
        StripeProvider.DescribeCustomerOwnershipViolation(
            Customer(new() { ["UserId"] = userId.ToString() }), BillingAccountRef.ForUser(userId), options).ShouldBeNull();
        StripeProvider.DescribeCustomerOwnershipViolation(
            Customer(new() { ["UserId"] = userId.ToString() }), Firm, options).ShouldNotBeNull();
    }

    [Fact]
    public void TradeNoSearch_IsNarrowedToThisApplication_WhenStamped()
    {
        StripeProvider.BuildTradeNoSearchQuery("PAY-9", Options())
            .ShouldBe("metadata['TradeNo']:'PAY-9' AND metadata['app']:'crm'");
        StripeProvider.BuildTradeNoSearchQuery("PAY-9", Options(stamp: null))
            .ShouldBe("metadata['TradeNo']:'PAY-9'");
    }

    /// <summary>应答 customers / payment_intents 两个创建端点，并把表单体按键值拆开留给断言。</summary>
    private sealed class CapturingStripe : HttpMessageHandler
    {
        private readonly Dictionary<string, Dictionary<string, string>> _forms = [];

        public Dictionary<string, string>? Form(string path) => _forms.GetValueOrDefault(path);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path is "/v1/customers" or "/v1/payment_intents")
            {
                var body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
                _forms[path] = body.Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Select(pair => pair.Split('=', 2))
                    .ToDictionary(kv => WebUtility.UrlDecode(kv[0]), kv => kv.Length > 1 ? WebUtility.UrlDecode(kv[1]) : string.Empty);

                var json = path == "/v1/customers"
                    ? "{\"id\":\"cus_new\",\"object\":\"customer\"}"
                    : "{\"id\":\"pi_1\",\"object\":\"payment_intent\",\"status\":\"requires_payment_method\",\"client_secret\":\"pi_1_secret\",\"amount\":33900,\"currency\":\"cad\"}";
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"error\":{\"message\":\"unexpected\"}}", Encoding.UTF8, "application/json")
            };
        }
    }
}

/// <summary>应用标记与账单描述后缀的配置校验：错配要在启动时报出来，而不是到第一笔扣款才被 Stripe 拒绝。</summary>
public class StripeSharedAccountOptionsValidationTests
{
    private static List<string> Errors(Action<StripeOptions> configure)
    {
        var options = new StripeOptions { Enabled = true, SecretKey = "sk", WebhookSecret = "whsec" };
        configure(options);
        var result = new StripeOptionsValidator().Validate(null, options);
        return result.Failed ? result.Failures!.ToList() : [];
    }

    [Fact]
    public void TheDefaults_AreValid() => Errors(_ => { }).ShouldBeEmpty();

    [Theory]
    [InlineData("CRMSUITE", true)]
    [InlineData("12345", false)]           // 至少一个字母
    [InlineData("LAW*FIRM", false)]        // 禁用字符
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVW", false)] // 23 > 22
    public void TheDescriptorSuffix_FollowsStripesRules(string suffix, bool valid)
        => Errors(o => o.StatementDescriptorSuffix = suffix).Count.ShouldBe(valid ? 0 : 1);

    [Theory]
    [InlineData("TradeNo")]
    [InlineData("billingaccount")]
    public void TheStampKey_CannotBeOneTheChannelWritesItself(string key)
        => Errors(o => { o.MetadataStampKey = key; o.MetadataStampValue = "crm"; }).ShouldHaveSingleItem();
}
