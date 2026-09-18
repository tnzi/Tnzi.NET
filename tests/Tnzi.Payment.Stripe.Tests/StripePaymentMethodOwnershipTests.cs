using System.Net;
using System.Text;

namespace Tnzi.Payment.Stripe.Tests;

/// <summary>
/// 绑卡的「这张卡是不是你的」守卫，在<b>首次绑卡</b>（本地还没有渠道客户）这条路径上必须真的判定。
/// </summary>
/// <remarks>
/// <para>
/// ★★ 此前 <c>ResolvePaymentMethodAsync</c> 在 <c>input.ProviderCustomerId</c> 为空时把
/// <c>paymentMethod.CustomerId</c>（token 自称的客户）当作比对基准，再拿它与 <c>paymentMethod.CustomerId</c>
/// 比对 —— 恒等，「belongs to customer X, expected Y」那段永远不执行。知道某个 <c>pm_…</c> 标识的用户
/// 在自己首次绑卡时把它提交上来，绑定成功，且他的 <c>ProviderCustomerId</c> 从此是<b>卡主的</b> Stripe customer：
/// 后续 off-session 续费扣的是卡主的卡。PayPal 侧同一位置早有按 merchant_customer_id 的归属校验。
/// </para>
/// <para>
/// 判据现成：<c>CreateCustomerAsync</c> 建客户时写了 <c>Metadata["UserId"]</c>。这里用一个走桩 HttpClient
/// 的 <c>StripeClient</c> 伪造 <c>GET /v1/payment_methods/{id}</c> 与 <c>GET /v1/customers/{id}</c>，
/// 跑的是真实的解析路径，不触网。
/// </para>
/// </remarks>
public class StripePaymentMethodOwnershipTests
{
    private static readonly Guid Victim = Guid.NewGuid();
    private static readonly Guid Attacker = Guid.NewGuid();

    /// <summary>★★ 首次绑卡提交一张挂在别人 customer 名下的卡：拒绝，不得 attach，不得回显卡片信息。</summary>
    [Fact]
    public async Task ResolvePaymentMethod_RejectsTokenWhoseCustomerBelongsToAnotherUser()
    {
        var stripe = new StubStripe()
            .PaymentMethod("pm_victim", customer: "cus_victim")
            .Customer("cus_victim", userId: Victim);
        var provider = ProviderOver(stripe);

        var result = await provider.ResolvePaymentMethodAsync(new PaymentProviderResolveMethodDto
        {
            UserId = Attacker,
            PaymentMethodToken = "pm_victim",
            ProviderCustomerId = null
        });

        result.Succeeded.ShouldBeFalse("一张挂在别人 Stripe customer 名下的卡被绑到了攻击者名下");
        result.Message.ShouldBe(ErrorCodes.PaymentMethodBindingFailed);
        result.Code.ShouldBe(403);
        stripe.Requests.ShouldNotContain(r => r.Contains("/attach", StringComparison.Ordinal));
    }

    /// <summary>合法的首次绑卡：SetupIntent 建在新客户名下，Stripe 确认时已把 pm 挂上去，customer 的 UserId 就是调用者。</summary>
    [Fact]
    public async Task ResolvePaymentMethod_AcceptsTokenWhoseCustomerCarriesTheCallersUserId()
    {
        var stripe = new StubStripe()
            .PaymentMethod("pm_mine", customer: "cus_mine")
            .Customer("cus_mine", userId: Victim);
        var provider = ProviderOver(stripe);

        var result = await provider.ResolvePaymentMethodAsync(new PaymentProviderResolveMethodDto
        {
            UserId = Victim,
            PaymentMethodToken = "pm_mine",
            ProviderCustomerId = null
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.ProviderCustomerId.ShouldBe("cus_mine");
        result.Data.Token.ShouldBe("pm_mine");
        result.Data.Last4.ShouldBe("4242");
    }

    /// <summary>customer 上没有 UserId 元数据（不是本系统建的客户）：信息不足不等于放行，方向关闭。</summary>
    [Fact]
    public async Task ResolvePaymentMethod_RejectsTokenWhoseCustomerHasNoUserIdMetadata()
    {
        var stripe = new StubStripe()
            .PaymentMethod("pm_foreign", customer: "cus_foreign")
            .Customer("cus_foreign", userId: null);
        var provider = ProviderOver(stripe);

        var result = await provider.ResolvePaymentMethodAsync(new PaymentProviderResolveMethodDto
        {
            UserId = Attacker,
            PaymentMethodToken = "pm_foreign"
        });

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe(ErrorCodes.PaymentMethodBindingFailed);
    }

    /// <summary>本地已有渠道客户的分支保持原状：token 挂在别的 customer 上照样拒绝。</summary>
    [Fact]
    public async Task ResolvePaymentMethod_WithAKnownCustomer_StillRejectsATokenAttachedElsewhere()
    {
        var stripe = new StubStripe()
            .PaymentMethod("pm_victim", customer: "cus_victim")
            .Customer("cus_victim", userId: Victim);
        var provider = ProviderOver(stripe);

        var result = await provider.ResolvePaymentMethodAsync(new PaymentProviderResolveMethodDto
        {
            UserId = Attacker,
            PaymentMethodToken = "pm_victim",
            ProviderCustomerId = "cus_attacker"
        });

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe(ErrorCodes.PaymentMethodBindingFailed);
    }

    /// <summary>
    /// 纯判定：本系统建的客户带 <c>Metadata["UserId"]</c>；只有它等于调用者才算归属成立。
    /// ★ 红线：删掉这个判定（或让它恒回 null），上面两条「拒绝」用例必须红。
    /// </summary>
    [Fact]
    public void DescribeCustomerOwnershipViolation_OnlyAcceptsTheCallersOwnCustomer()
    {
        var mine = new global::Stripe.Customer { Id = "cus_1", Metadata = new Dictionary<string, string> { ["UserId"] = Victim.ToString() } };
        var someoneElses = new global::Stripe.Customer { Id = "cus_2", Metadata = new Dictionary<string, string> { ["UserId"] = Attacker.ToString() } };
        var unlabeled = new global::Stripe.Customer { Id = "cus_3", Metadata = new Dictionary<string, string>() };
        var deleted = new global::Stripe.Customer { Id = "cus_4", Deleted = true, Metadata = new Dictionary<string, string> { ["UserId"] = Victim.ToString() } };

        StripeProvider.DescribeCustomerOwnershipViolation(mine, Victim).ShouldBeNull();
        StripeProvider.DescribeCustomerOwnershipViolation(someoneElses, Victim).ShouldNotBeNull();
        StripeProvider.DescribeCustomerOwnershipViolation(unlabeled, Victim).ShouldNotBeNull();
        StripeProvider.DescribeCustomerOwnershipViolation(deleted, Victim).ShouldNotBeNull();
        StripeProvider.DescribeCustomerOwnershipViolation(null, Victim).ShouldNotBeNull();
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

    /// <summary>按路径应答的 Stripe 桩：只认 GET payment_methods / customers，其它一律 404。</summary>
    private sealed class StubStripe : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _responses = new(StringComparer.Ordinal);

        public List<string> Requests { get; } = [];

        public StubStripe PaymentMethod(string id, string? customer)
        {
            var customerJson = customer == null ? "null" : $"\"{customer}\"";
            _responses[$"/v1/payment_methods/{id}"] =
                $"{{\"id\":\"{id}\",\"object\":\"payment_method\",\"type\":\"card\",\"customer\":{customerJson}," +
                "\"card\":{\"brand\":\"visa\",\"last4\":\"4242\",\"exp_month\":1,\"exp_year\":2030}}";
            return this;
        }

        public StubStripe Customer(string id, Guid? userId)
        {
            var metadata = userId == null ? "{}" : $"{{\"UserId\":\"{userId}\"}}";
            _responses[$"/v1/customers/{id}"] =
                $"{{\"id\":\"{id}\",\"object\":\"customer\",\"metadata\":{metadata}}}";
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add($"{request.Method} {path}");

            if (request.Method == HttpMethod.Get && _responses.TryGetValue(path, out var json))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(
                    "{\"error\":{\"type\":\"invalid_request_error\",\"message\":\"stub: no such resource\"}}",
                    Encoding.UTF8, "application/json")
            });
        }
    }
}
