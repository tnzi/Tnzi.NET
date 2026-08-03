using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Tnzi.Payment.Dtos;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Options;
using Tnzi.Payment.Providers;
// Tnzi.Payment.Options 这个命名空间会遮住 Microsoft.Extensions.Options.Options 静态类
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.Payment.Tests;

/// <summary>
/// PayPal 账户保存（Vault v3）与商户发起扣款的单元测试。
/// </summary>
/// <remarks>
/// 这里断言的重点是**发给 PayPal 的请求体**，而不只是我们怎么解析响应：
/// 少一个 <c>stored_credential</c> 字段、把 <c>usage_type</c> 写错，PayPal 会当成客户在场交易
/// 并要求付款人交互——而后台扣款时没有人在。这类错误在集成环境里表现为"续费莫名其妙失败"，
/// 只有钉住请求形状才能在改动时立刻发现。
/// </remarks>
public class PayPalVaultProviderTests
{
    private const string SetupTokenId = "5C991763TT9910314";
    private const string PaymentTokenId = "jwgvx42";
    private const string CustomerId = "customer_4029352051";

    private readonly StubHttpMessageHandler _handler = new();

    private PayPalProvider CreateProvider(bool enableVault = true, string? vaultReturnUrl = "https://app.example.com/billing/paypal-return")
    {
        var options = new PayPalOptions
        {
            Enabled = true,
            ClientId = "client-id",
            ClientSecret = "client-secret",
            Mode = "sandbox",
            Currency = "USD",
            BrandName = "Example Inc",
            WebhookId = "webhook-id",
            EnableVault = enableVault,
            VaultReturnUrl = vaultReturnUrl
        };

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(_handler, disposeHandler: false));

        // 每个 PayPal 请求前都要先换访问令牌，全部用例都需要它
        _handler.OnPost("/v1/oauth2/token", _ => Json(new { access_token = "A21AA", token_type = "Bearer", expires_in = 3600 }));

        return new PayPalProvider(MsOptions.Create(options), factory.Object, NullLogger<PayPalProvider>.Instance);
    }

    // ---- 开关 ----

    [Fact]
    public void Capabilities_FollowEnableVault()
    {
        CreateProvider(enableVault: false).SupportsPaymentMethodStorage.ShouldBeFalse();
        CreateProvider(enableVault: false).SupportsOffSessionCharge.ShouldBeFalse();
        CreateProvider().SupportsPaymentMethodStorage.ShouldBeTrue();
        CreateProvider().SupportsOffSessionCharge.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateSetupSession_WhenVaultDisabled_ReportsNotSupported()
    {
        var provider = CreateProvider(enableVault: false);

        var result = await provider.CreateSetupSessionAsync(new PaymentProviderSetupDto { UserId = Guid.NewGuid() });

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe(ErrorCodes.PaymentMethodStorageNotSupported);
        // 关着的时候一次 PayPal 请求都不该发出去
        _handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task CreateSetupSession_WithoutReturnUrl_FailsBeforeCallingPayPal()
    {
        var provider = CreateProvider(vaultReturnUrl: null);

        var result = await provider.CreateSetupSessionAsync(new PaymentProviderSetupDto { UserId = Guid.NewGuid() });

        result.Succeeded.ShouldBeFalse();
        // 没有回跳地址时把用户送去 PayPal 才是最糟的：他点完同意就无处可去
        _handler.Requests.ShouldNotContain(r => r.Path.Contains("setup-tokens"));
    }

    // ---- 绑定：申请授权 ----

    [Fact]
    public async Task CreateSetupSession_ReturnsApprovalUrlAndDeclaresMerchantInitiated()
    {
        var provider = CreateProvider();
        var userId = Guid.NewGuid();

        _handler.OnPost("/v3/vault/setup-tokens", _ => Json(new
        {
            id = SetupTokenId,
            status = "PAYER_ACTION_REQUIRED",
            customer = new { id = CustomerId },
            links = new[]
            {
                new { rel = "approve", href = "https://sandbox.paypal.com/agreements/approve?approval_session_id=X" },
                new { rel = "self", href = "https://api-m.sandbox.paypal.com/v3/vault/setup-tokens/" + SetupTokenId }
            }
        }));

        var result = await provider.CreateSetupSessionAsync(new PaymentProviderSetupDto
        {
            UserId = userId,
            ReturnUrl = "https://app.example.com/done"
        });

        result.Succeeded.ShouldBeTrue();
        result.Data!.SetupId.ShouldBe(SetupTokenId);
        result.Data.ApprovalUrl.ShouldStartWith("https://sandbox.paypal.com/agreements/approve");
        result.Data.ProviderCustomerId.ShouldBe(CustomerId);

        var body = _handler.LastBodyFor(HttpMethod.Post, "/v3/vault/setup-tokens");
        var paypal = body.GetProperty("payment_source").GetProperty("paypal");
        // MERCHANT 才是"以后允许商户直接扣款"，写成 PLATFORM/CUSTOMER 都会让后续扣款要求付款人在场
        paypal.GetProperty("usage_type").GetString().ShouldBe("MERCHANT");
        paypal.GetProperty("usage_pattern").GetString().ShouldBe("SUBSCRIPTION_PREPAID");
        paypal.GetProperty("experience_context").GetProperty("return_url").GetString().ShouldBe("https://app.example.com/done");
        // 商户侧客户标识是登记时判断归属的唯一依据，不写就无从校验
        body.GetProperty("customer").GetProperty("merchant_customer_id").GetString().ShouldBe(userId.ToString());
    }

    [Fact]
    public async Task CreateSetupSession_WhenPayPalReturnsNoApprovalLink_Fails()
    {
        var provider = CreateProvider();
        _handler.OnPost("/v3/vault/setup-tokens", _ => Json(new { id = SetupTokenId, links = Array.Empty<object>() }));

        var result = await provider.CreateSetupSessionAsync(new PaymentProviderSetupDto { UserId = Guid.NewGuid() });

        // 没有授权地址 = 商户账号多半没开通 reference transactions。
        // 返回一个前端不知道拿来干什么的空会话，只会把失败推迟到更晚
        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe(ErrorCodes.PayPalVaultFailed);
    }

    // ---- 绑定：换取长期凭据 ----

    [Fact]
    public async Task ResolvePaymentMethod_ExchangesSetupTokenAndMasksAccount()
    {
        var provider = CreateProvider();
        var userId = Guid.NewGuid();

        _handler.OnGet($"/v3/vault/payment-tokens/{SetupTokenId}", _ => new HttpResponseMessage(HttpStatusCode.NotFound));
        _handler.OnPost("/v3/vault/payment-tokens", _ => Json(PaymentTokenPayload(userId, "payer@example.com")));

        var result = await provider.ResolvePaymentMethodAsync(new PaymentProviderResolveMethodDto
        {
            PaymentMethodToken = SetupTokenId,
            UserId = userId
        });

        result.Succeeded.ShouldBeTrue();
        result.Data!.Token.ShouldBe(PaymentTokenId);
        result.Data.ProviderCustomerId.ShouldBe(CustomerId);
        result.Data.MethodType.ShouldBe(PaymentMethod.PayPal);
        result.Data.Brand.ShouldBe("PayPal");
        // 展示用的账户标识会出现在管理端列表和支持工单里，不该是完整邮箱
        result.Data.AccountLabel.ShouldBe("p***@example.com");

        var body = _handler.LastBodyFor(HttpMethod.Post, "/v3/vault/payment-tokens");
        body.GetProperty("payment_source").GetProperty("token").GetProperty("type").GetString().ShouldBe("SETUP_TOKEN");
    }

    [Fact]
    public async Task ResolvePaymentMethod_WhenTokenAlreadyVaulted_DoesNotExchangeAgain()
    {
        var provider = CreateProvider();
        var userId = Guid.NewGuid();

        _handler.OnGet($"/v3/vault/payment-tokens/{PaymentTokenId}", _ => Json(PaymentTokenPayload(userId, "payer@example.com")));

        var result = await provider.ResolvePaymentMethodAsync(new PaymentProviderResolveMethodDto
        {
            PaymentMethodToken = PaymentTokenId,
            UserId = userId
        });

        result.Succeeded.ShouldBeTrue();
        // setup token 是一次性的。用户刷新回跳页 / 前端重试时若再换一次，PayPal 必然报错——
        // 先查一次让重复登记变成幂等操作
        _handler.Requests.ShouldNotContain(r => r.Method == HttpMethod.Post && r.Path == "/v3/vault/payment-tokens");
    }

    [Fact]
    public async Task ResolvePaymentMethod_WhenTokenBelongsToAnotherUser_IsRejected()
    {
        var provider = CreateProvider();
        var owner = Guid.NewGuid();
        var attacker = Guid.NewGuid();

        _handler.OnGet($"/v3/vault/payment-tokens/{PaymentTokenId}", _ => Json(PaymentTokenPayload(owner, "victim@example.com")));

        var result = await provider.ResolvePaymentMethodAsync(new PaymentProviderResolveMethodDto
        {
            PaymentMethodToken = PaymentTokenId,
            UserId = attacker
        });

        // 拿到别人的凭据不该能把别人的 PayPal 账户绑到自己名下
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
    }

    [Fact]
    public async Task ResolvePaymentMethod_MapsVaultedCardExpiry()
    {
        var provider = CreateProvider();
        var userId = Guid.NewGuid();

        _handler.OnGet($"/v3/vault/payment-tokens/{PaymentTokenId}", _ => Json(new
        {
            id = PaymentTokenId,
            customer = new { id = CustomerId, merchant_customer_id = userId.ToString() },
            payment_source = new { card = new { brand = "VISA", last_digits = "1111", expiry = "2031-07" } }
        }));

        var result = await provider.ResolvePaymentMethodAsync(new PaymentProviderResolveMethodDto
        {
            PaymentMethodToken = PaymentTokenId,
            UserId = userId
        });

        result.Succeeded.ShouldBeTrue();
        result.Data!.MethodType.ShouldBe(PaymentMethod.CreditCard);
        result.Data.Last4.ShouldBe("1111");
        // PayPal 的有效期是 "YYYY-MM" 一个字符串，落库要拆成年月两列
        result.Data.ExpiryYear.ShouldBe(2031);
        result.Data.ExpiryMonth.ShouldBe(7);
    }

    // ---- 解绑 ----

    [Fact]
    public async Task Detach_WhenTokenAlreadyGone_Succeeds()
    {
        var provider = CreateProvider();
        _handler.OnDelete($"/v3/vault/payment-tokens/{PaymentTokenId}", _ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await provider.DetachPaymentMethodAsync(new PaymentProviderResolveMethodDto
        {
            PaymentMethodToken = PaymentTokenId,
            UserId = Guid.NewGuid()
        });

        // 用户可能已经在 PayPal 后台自己撤销了授权。解绑是幂等操作，本地照常清理
        result.Succeeded.ShouldBeTrue();
    }

    // ---- 撤销通知 ----

    [Fact]
    public async Task HandleCallback_VaultTokenDeleted_ReportsRevocation()
    {
        var provider = CreateProvider();
        var payload = JsonSerializer.Serialize(new
        {
            id = "WH-EVT-1",
            event_type = "VAULT.PAYMENT-TOKEN.DELETED",
            resource = new { id = PaymentTokenId }
        });

        var result = await provider.HandleCallbackAsync(new Dictionary<string, string>
        {
            [PaymentConstants.CallbackRawBodyKey] = payload
        });

        result.Succeeded.ShouldBeTrue();
        // 付款人在 PayPal 撤销了授权。不识别这条事件，本地会一直拿着一个作废的凭据，
        // 直到下个周期扣款失败才发现
        result.Data!.Kind.ShouldBe(PaymentCallbackKind.PaymentMethodRevoked);
        result.Data.PaymentMethodToken.ShouldBe(PaymentTokenId);
        result.Data.IsHandled.ShouldBeTrue();
        result.Data.EventId.ShouldBe("WH-EVT-1");
    }

    [Fact]
    public async Task HandleCallback_UnrelatedVaultEvent_IsIgnored()
    {
        var provider = CreateProvider();
        var payload = JsonSerializer.Serialize(new
        {
            id = "WH-EVT-2",
            event_type = "VAULT.PAYMENT-TOKEN.CREATED",
            resource = new { id = PaymentTokenId }
        });

        var result = await provider.HandleCallbackAsync(new Dictionary<string, string>
        {
            [PaymentConstants.CallbackRawBodyKey] = payload
        });

        // 「凭据已创建」不是「凭据已作废」。把它一并当撤销，会把用户刚绑好的账户立刻清掉
        result.Succeeded.ShouldBeTrue();
        result.Data!.IsHandled.ShouldBeFalse();
        result.Data.Kind.ShouldBe(PaymentCallbackKind.Payment);
    }

    // ---- 商户发起扣款 ----

    [Fact]
    public async Task ChargeOffSession_DeclaresMerchantInitiatedRecurringAndReturnsCaptureId()
    {
        var provider = CreateProvider();
        _handler.OnPost("/v2/checkout/orders", _ => Json(CompletedOrder("9XJ12345", "CAP-777", "30.00")));

        var result = await provider.ChargeOffSessionAsync(new PaymentProviderChargeDto
        {
            TradeNo = "T-1001",
            BusinessOrderNo = "SUB-1",
            Amount = 30m,
            Currency = "USD",
            PaymentMethodToken = PaymentTokenId
        });

        result.Succeeded.ShouldBeTrue();
        result.Data!.Status.ShouldBe(PaymentStatus.Succeeded);
        result.Data.PaidAmount.ShouldBe(30m);
        // 退款接口打的是 capture 不是 order；存订单号会让这笔钱退不了
        result.Data.ExternalTradeNo.ShouldBe("CAP-777");

        var request = _handler.Requests.Last(r => r.Path == "/v2/checkout/orders");
        var paypal = request.Body!.Value.GetProperty("payment_source").GetProperty("paypal");
        paypal.GetProperty("vault_id").GetString().ShouldBe(PaymentTokenId);

        var stored = paypal.GetProperty("stored_credential");
        // 这三个字段合起来才是"商户发起的周期性非首次扣款"；缺任何一个 PayPal 都会要求付款人在场
        stored.GetProperty("payment_initiator").GetString().ShouldBe("MERCHANT");
        stored.GetProperty("payment_type").GetString().ShouldBe("RECURRING");
        stored.GetProperty("usage").GetString().ShouldBe("SUBSEQUENT");

        // 幂等键按内部流水号：扣款成功但本地推进失败后重试，不会二次扣款
        request.IdempotencyKey.ShouldBe("pi:T-1001");
    }

    [Fact]
    public async Task ChargeOffSession_WhenOrderNotCompleted_CapturesIt()
    {
        var provider = CreateProvider();
        _handler.OnPost("/v2/checkout/orders", _ => Json(new { id = "9XJ12345", status = "APPROVED" }));
        _handler.OnPost("/v2/checkout/orders/9XJ12345/capture", _ => Json(CompletedOrder("9XJ12345", "CAP-888", "12.00")));

        var result = await provider.ChargeOffSessionAsync(new PaymentProviderChargeDto
        {
            TradeNo = "T-1002",
            Amount = 12m,
            Currency = "USD",
            PaymentMethodToken = PaymentTokenId
        });

        // 是否自动收款取决于账号配置，两种都要处理——"少收一笔钱"不能靠猜
        result.Data!.Status.ShouldBe(PaymentStatus.Succeeded);
        result.Data.ExternalTradeNo.ShouldBe("CAP-888");
    }

    [Fact]
    public async Task ChargeOffSession_WhenDeclined_ReturnsFailedResultRatherThanError()
    {
        var provider = CreateProvider();
        _handler.OnPost("/v2/checkout/orders", _ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
        {
            Content = new StringContent("{\"name\":\"UNPROCESSABLE_ENTITY\",\"details\":[{\"issue\":\"INSTRUMENT_DECLINED\"}]}")
        });

        var result = await provider.ChargeOffSessionAsync(new PaymentProviderChargeDto
        {
            TradeNo = "T-1003",
            Amount = 30m,
            Currency = "USD",
            PaymentMethodToken = PaymentTokenId
        });

        // 扣款被拒是预期结果，不是系统错误：要让订阅状态机据此降级 PastDue 并催款，
        // 而不是抛失败让整轮后台扫描中断
        result.Succeeded.ShouldBeTrue();
        result.Data!.Status.ShouldBe(PaymentStatus.Failed);
        result.Data.FailReason!.ShouldContain("INSTRUMENT_DECLINED");
    }

    [Fact]
    public async Task ChargeOffSession_WhenVaultDisabled_ReportsNotSupported()
    {
        var provider = CreateProvider(enableVault: false);

        var result = await provider.ChargeOffSessionAsync(new PaymentProviderChargeDto
        {
            TradeNo = "T-1004",
            Amount = 30m,
            Currency = "USD",
            PaymentMethodToken = PaymentTokenId
        });

        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe(ErrorCodes.PaymentOffSessionNotSupported);
    }

    // ---- 载荷构造 ----

    private static object PaymentTokenPayload(Guid userId, string email) => new
    {
        id = PaymentTokenId,
        customer = new { id = CustomerId, merchant_customer_id = userId.ToString() },
        payment_source = new { paypal = new { email_address = email, payer_id = "AJM9JTWQJCFTA" } }
    };

    private static object CompletedOrder(string orderId, string captureId, string amount) => new
    {
        id = orderId,
        status = "COMPLETED",
        purchase_units = new[]
        {
            new
            {
                payments = new
                {
                    captures = new[]
                    {
                        new { id = captureId, status = "COMPLETED", amount = new { currency_code = "USD", value = amount } }
                    }
                }
            }
        }
    };

    private static HttpResponseMessage Json(object payload) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(payload))
    };

    /// <summary>
    /// 按「方法 + 路径」派发的 HTTP 桩，同时录下发出去的请求体供断言。
    /// </summary>
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<RecordedRequest, HttpResponseMessage>> _routes = new();

        public List<RecordedRequest> Requests { get; } = [];

        public void OnPost(string path, Func<RecordedRequest, HttpResponseMessage> responder) => _routes[Key(HttpMethod.Post, path)] = responder;
        public void OnGet(string path, Func<RecordedRequest, HttpResponseMessage> responder) => _routes[Key(HttpMethod.Get, path)] = responder;
        public void OnDelete(string path, Func<RecordedRequest, HttpResponseMessage> responder) => _routes[Key(HttpMethod.Delete, path)] = responder;

        public JsonElement LastBodyFor(HttpMethod method, string path)
        {
            var request = Requests.Last(r => r.Method == method && r.Path == path);
            request.Body.ShouldNotBeNull($"No JSON body recorded for {method} {path}.");
            return request.Body.Value;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            JsonElement? body = null;

            if (request.Content != null)
            {
                var raw = await request.Content.ReadAsStringAsync(cancellationToken);
                // 换令牌那一步是表单不是 JSON，解析失败不该让用例炸掉
                if (raw.StartsWith('{'))
                    body = JsonDocument.Parse(raw).RootElement.Clone();
            }

            var recorded = new RecordedRequest(
                request.Method,
                path,
                body,
                request.Headers.TryGetValues("PayPal-Request-Id", out var ids) ? ids.FirstOrDefault() : null);

            Requests.Add(recorded);

            return _routes.TryGetValue(Key(request.Method, path), out var responder)
                ? responder(recorded)
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static string Key(HttpMethod method, string path) => $"{method}:{path}";
    }

    private sealed record RecordedRequest(HttpMethod Method, string Path, JsonElement? Body, string? IdempotencyKey);
}
