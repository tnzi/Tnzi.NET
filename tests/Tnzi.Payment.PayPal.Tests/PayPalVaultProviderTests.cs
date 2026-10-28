namespace Tnzi.Payment.PayPal.Tests;

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

    private PayPalProvider CreateProvider(
        bool enableVault = true,
        string? vaultReturnUrl = "https://app.example.com/billing/paypal-return",
        string clientId = "client-id")
    {
        var options = new PayPalOptions
        {
            Enabled = true,
            ClientId = clientId,
            ClientSecret = "client-secret",
            Mode = "sandbox",
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

    // ---- 归属按计费账户 ----

    private async Task<string> MerchantIdSentForAsync(PayPalProvider provider, BillingAccountRef? account, Guid userId)
    {
        _handler.OnPost("/v3/vault/setup-tokens", _ => Json(new
        {
            id = SetupTokenId,
            status = "PAYER_ACTION_REQUIRED",
            customer = new { id = CustomerId },
            links = new[] { new { rel = "approve", href = "https://sandbox.paypal.com/agreements/approve?approval_session_id=X" } }
        }));

        var created = await provider.CreateSetupSessionAsync(new PaymentProviderSetupDto
        {
            UserId = userId,
            BillingAccount = account,
            ReturnUrl = "https://app.example.com/done"
        });
        created.Succeeded.ShouldBeTrue(created.Message);

        return _handler.LastBodyFor(HttpMethod.Post, "/v3/vault/setup-tokens")
            .GetProperty("customer").GetProperty("merchant_customer_id").GetString()!;
    }

    [Fact]
    public async Task AnOrganisationAccount_IsTheVaultOwner_SoEveryManagerGetsTheSameMerchantId()
    {
        var provider = CreateProvider();
        var firm = new BillingAccountRef(BillingAccountTypes.Installation, "default");

        var first = await MerchantIdSentForAsync(provider, firm, Guid.NewGuid());
        var second = await MerchantIdSentForAsync(provider, firm, Guid.NewGuid());

        first.ShouldBe(second, "the card belongs to the account, not to whoever entered it");
        // PayPal 只收字母数字与少数符号且限长：带冒号的账户键不能原样写进去
        first.ShouldMatch("^ba-[0-9a-f]{40}$");
    }

    [Fact]
    public async Task APersonalAccount_KeepsTheUserIdAsTheMerchantId()
    {
        var provider = CreateProvider();
        var userId = Guid.NewGuid();

        (await MerchantIdSentForAsync(provider, BillingAccountRef.ForUser(userId), userId)).ShouldBe(userId.ToString(),
            "tokens vaulted before ownership moved to accounts must still be recognised");
    }

    [Fact]
    public async Task ATokenVaultedForTheFirm_BindsForAnyManager_AndAPersonalTokenDoesNot()
    {
        var provider = CreateProvider();
        var firm = new BillingAccountRef(BillingAccountTypes.Installation, "default");
        var firmMerchantId = await MerchantIdSentForAsync(provider, firm, Guid.NewGuid());
        var someManager = Guid.NewGuid();

        _handler.OnGet($"/v3/vault/payment-tokens/{PaymentTokenId}", _ => Json(new
        {
            id = PaymentTokenId,
            customer = new { id = CustomerId, merchant_customer_id = firmMerchantId },
            payment_source = new { paypal = new { email_address = "billing@firm.example", payer_id = "AJM9JTWQJCFTA" } }
        }));
        var forTheFirm = await provider.ResolvePaymentMethodAsync(new PaymentProviderResolveMethodDto
        {
            PaymentMethodToken = PaymentTokenId,
            UserId = someManager,
            BillingAccount = firm
        });
        forTheFirm.Succeeded.ShouldBeTrue(forTheFirm.Message);

        _handler.OnGet($"/v3/vault/payment-tokens/{PaymentTokenId}", _ => Json(new
        {
            id = PaymentTokenId,
            customer = new { id = CustomerId, merchant_customer_id = someManager.ToString() },
            payment_source = new { paypal = new { email_address = "me@example.com", payer_id = "AJM9JTWQJCFTB" } }
        }));
        var personal = await provider.ResolvePaymentMethodAsync(new PaymentProviderResolveMethodDto
        {
            PaymentMethodToken = PaymentTokenId,
            UserId = someManager,
            BillingAccount = firm
        });
        personal.Code.ShouldBe(403, "a manager's own PayPal token is not the firm's");
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

    /// <summary>
    /// 账户标识只露首字符：本地部分以 emoji 开头时一个都不露，而不是露出半个代理项（标识会落库、进管理端列表）。
    /// </summary>
    [Fact]
    public async Task ResolvePaymentMethod_EmailStartingWithAnEmoji_MasksWithoutALoneSurrogate()
    {
        var provider = CreateProvider();
        var userId = Guid.NewGuid();

        _handler.OnGet($"/v3/vault/payment-tokens/{SetupTokenId}", _ => new HttpResponseMessage(HttpStatusCode.NotFound));
        _handler.OnPost("/v3/vault/payment-tokens", _ => Json(PaymentTokenPayload(userId, "\U0001F600payer@example.com")));

        var result = await provider.ResolvePaymentMethodAsync(new PaymentProviderResolveMethodDto
        {
            PaymentMethodToken = SetupTokenId,
            UserId = userId
        });

        result.Succeeded.ShouldBeTrue();
        result.Data!.AccountLabel.ShouldBe("***@example.com");
    }

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
    public async Task ResolvePaymentMethod_WhenTokenCarriesNoMerchantCustomerId_IsRejected()
    {
        // 本系统铸的每一枚凭据都带 merchant_customer_id（CreateSetupSessionAsync 写的）。
        // 没带的只可能是同一商户名下、绕过本系统流程铸出来的凭据，本系统无从证明它属于调用者 ——
        // 与 Stripe 侧「customer 没有 UserId 元数据 ⇒ 403」同一方向；此前这里放行，两个渠道对同一件事答案相反。
        var provider = CreateProvider();

        _handler.OnGet($"/v3/vault/payment-tokens/{PaymentTokenId}", _ => Json(new
        {
            id = PaymentTokenId,
            customer = new { id = CustomerId },
            payment_source = new { paypal = new { email_address = "someone@example.com", payer_id = "AJM9JTWQJCFTA" } }
        }));

        var result = await provider.ResolvePaymentMethodAsync(new PaymentProviderResolveMethodDto
        {
            PaymentMethodToken = PaymentTokenId,
            UserId = Guid.NewGuid()
        });

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

    /// <summary>
    /// ★ 绑卡请求里的令牌是用户输入，拼进路径前必须转义：<c>../../../v2/...</c> 不能把带着商户访问令牌的请求带到 PayPal API 的别处。
    /// </summary>
    [Fact]
    public async Task ResolvePaymentMethod_KeepsAPathLikeTokenInsideTheVaultPath()
    {
        var provider = CreateProvider();

        await provider.ResolvePaymentMethodAsync(new PaymentProviderResolveMethodDto
        {
            PaymentMethodToken = "../../../v2/checkout/orders/ORDER-OF-SOMEONE-ELSE",
            UserId = Guid.NewGuid()
        });

        var gets = _handler.Requests.Where(r => r.Method == HttpMethod.Get).ToList();
        gets.ShouldNotBeEmpty();
        gets.ShouldAllBe(r => r.Path.StartsWith("/v3/vault/payment-tokens/"));
    }

    [Fact]
    public async Task Detach_KeepsAPathLikeTokenInsideTheVaultPath()
    {
        var provider = CreateProvider();

        await provider.DetachPaymentMethodAsync(new PaymentProviderResolveMethodDto
        {
            PaymentMethodToken = "../../../v2/checkout/orders/ORDER-OF-SOMEONE-ELSE",
            UserId = Guid.NewGuid()
        });

        var deletes = _handler.Requests.Where(r => r.Method == HttpMethod.Delete).ToList();
        deletes.ShouldHaveSingleItem().Path.ShouldStartWith("/v3/vault/payment-tokens/");
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

    /// <summary>
    /// 访问令牌跨请求复用。
    /// </summary>
    /// <remarks>
    /// 令牌此前存在实例字段上，而本类是 Scoped —— 每个请求都是一个新实例、一个空缓存，
    /// 于是每一次建单 / 查单 / 退款前都先打一次 <c>/v1/oauth2/token</c>：
    /// 对外请求数翻倍、每笔交易多一次往返，而 PayPal 发的令牌本来有效期一小时。
    /// 症状只有「PayPal 慢」，没有任何一条错误日志。
    /// ★ 本用例用一个独占的 ClientId：缓存是按 ClientId 分桶的静态字典，
    /// 用公共那个的话，命中与否取决于**别的用例有没有先跑过**。
    /// </remarks>
    [Fact]
    public async Task TheAccessToken_IsReusedAcrossProviderInstances()
    {
        var clientId = $"cache-{Guid.NewGuid():N}";
        _handler.OnPost("/v2/checkout/orders", _ => Json(CompletedOrder("ORDER-1", "CAP-1", "10.00")));

        await CreateProvider(clientId: clientId).CreatePaymentAsync(
            new PaymentProviderCreateDto { TradeNo = "T1", Amount = 10m, Currency = "USD" });
        await CreateProvider(clientId: clientId).CreatePaymentAsync(
            new PaymentProviderCreateDto { TradeNo = "T2", Amount = 10m, Currency = "USD" });

        _handler.Requests.Count(r => r.Path == "/v1/oauth2/token").ShouldBe(1);
    }

    /// <summary>
    /// 回报的币种必须被带上来。此前只取了 <c>amount.value</c> 把 <c>currency_code</c> 丢掉，
    /// 于是服务层只能比数值 —— 一笔 100 USD 的订单收到 100 JPY 的回报照样通过金额校验。
    /// </summary>
    [Fact]
    public async Task HandleCallback_CaptureCompleted_CarriesTheReportedCurrency()
    {
        var provider = CreateProvider();
        var payload = JsonSerializer.Serialize(new
        {
            id = "WH-EVT-3",
            event_type = "PAYMENT.CAPTURE.COMPLETED",
            resource = new
            {
                id = "CAP-555",
                custom_id = "PAY-XYZ",
                amount = new { value = "30.00", currency_code = "CAD" }
            }
        });

        var result = await provider.HandleCallbackAsync(new Dictionary<string, string>
        {
            [PaymentConstants.CallbackRawBodyKey] = payload
        });

        result.Succeeded.ShouldBeTrue();
        result.Data!.TradeNo.ShouldBe("PAY-XYZ");
        result.Data.PaidAmount.ShouldBe(30.00m);
        result.Data.Currency.ShouldBe("CAD");
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

    /// <summary>
    /// capture 处于 PENDING（风控复核、eCheck 清算）：钱在路上，回报「处理中」而不是失败。
    /// 当成失败的话订阅降级并换一个流水号再扣，原来那笔随后入账就是同一期扣了两次。
    /// </summary>
    [Fact]
    public async Task ChargeOffSession_WhenTheCaptureIsPending_ReportsProcessingWithTheCaptureId()
    {
        var provider = CreateProvider();
        _handler.OnPost("/v2/checkout/orders", _ => Json(new
        {
            id = "9XJ12399",
            status = "COMPLETED",
            purchase_units = new[]
            {
                new
                {
                    payments = new
                    {
                        captures = new[]
                        {
                            new { id = "CAP-PEND", status = "PENDING", amount = new { currency_code = "USD", value = "30.00" } }
                        }
                    }
                }
            }
        }));

        var result = await provider.ChargeOffSessionAsync(new PaymentProviderChargeDto
        {
            TradeNo = "T-1005",
            Amount = 30m,
            Currency = "USD",
            PaymentMethodToken = PaymentTokenId
        });

        result.Succeeded.ShouldBeTrue();
        result.Data!.Status.ShouldBe(PaymentStatus.Processing);
        result.Data.ExternalTradeNo.ShouldBe("CAP-PEND");
    }

    /// <summary>
    /// PayPal 答 5xx：订单建没建成、钱扣没扣都说不准。回报失败（服务层按结果未知处理）而不是「成功拿到一个拒付」。
    /// </summary>
    [Fact]
    public async Task ChargeOffSession_WhenPayPalFailsWithAServerError_ReportsAnUnknownOutcomeRatherThanADecline()
    {
        var provider = CreateProvider();
        _handler.OnPost("/v2/checkout/orders", _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("{\"name\":\"INTERNAL_SERVER_ERROR\"}")
        });

        var result = await provider.ChargeOffSessionAsync(new PaymentProviderChargeDto
        {
            TradeNo = "T-1006",
            Amount = 30m,
            Currency = "USD",
            PaymentMethodToken = PaymentTokenId
        });

        result.Succeeded.ShouldBeFalse();
        result.ErrorCode.ShouldNotBe(ErrorCodes.PaymentOffSessionNotAttempted);
    }

    /// <summary>拿不到访问令牌：扣款请求根本没发出去，回报「未尝试」，服务层才把它当成确定的失败。</summary>
    [Fact]
    public async Task ChargeOffSession_WithoutAnAccessToken_ReportsThatNothingWasAttempted()
    {
        var provider = CreateProvider(clientId: $"client-{Guid.NewGuid():N}");
        _handler.OnPost("/v1/oauth2/token", _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var result = await provider.ChargeOffSessionAsync(new PaymentProviderChargeDto
        {
            TradeNo = "T-1007",
            Amount = 30m,
            Currency = "USD",
            PaymentMethodToken = PaymentTokenId
        });

        result.Succeeded.ShouldBeFalse();
        result.ErrorCode.ShouldBe(ErrorCodes.PaymentOffSessionNotAttempted);
        _handler.Requests.ShouldNotContain(r => r.Path == "/v2/checkout/orders");
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
}
