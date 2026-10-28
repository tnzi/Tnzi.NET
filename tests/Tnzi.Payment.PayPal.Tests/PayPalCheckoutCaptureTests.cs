namespace Tnzi.Payment.PayPal.Tests;

/// <summary>
/// 重定向收款（<c>intent=CAPTURE</c> 的一次性订单）的收款与对账。
/// </summary>
/// <remarks>
/// <para>
/// ★ 付款人在 PayPal 点完「立即付款」之后订单停在 <c>APPROVED</c>，要商户调用 capture 才收得到钱。
/// 此前只有无人值守扣款那条路径会 capture：批准回调被当成中间态忽略、同步只读，订单到期作废，
/// 付款人以为付了，商户一分钱没收到。
/// </para>
/// <para>
/// ★ 同步 / 关单核实把订单 id 回报成 <c>ExternalTradeNo</c>：落定为成功的支付记下的是订单 id，
/// 退款打到 <c>/v2/payments/captures/{订单 id}/refunds</c>，PayPal 答 404，这笔支付在系统里永远退不了款。
/// </para>
/// </remarks>
public class PayPalCheckoutCaptureTests
{
    private const string OrderId = "5O190127TN364715T";
    private const string CaptureId = "3C679366HH908993F";
    private const string TradeNo = "PAY-CHECKOUT-1";

    private readonly StubHttpMessageHandler _handler = new();

    private IPaymentProvider CreateProvider()
    {
        var options = new PayPalOptions
        {
            Enabled = true,
            ClientId = "checkout-client",
            ClientSecret = "client-secret",
            Mode = "sandbox",
            WebhookId = "webhook-id"
        };

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(_handler, disposeHandler: false));
        _handler.OnPost("/v1/oauth2/token", _ => Json(new { access_token = "A21AA", token_type = "Bearer", expires_in = 3600 }));

        return new PayPalProvider(MsOptions.Create(options), factory.Object, NullLogger<PayPalProvider>.Instance);
    }

    private static object ApprovedOrder() => new
    {
        id = OrderId,
        status = "APPROVED",
        purchase_units = new[]
        {
            new { reference_id = TradeNo, amount = new { currency_code = "EUR", value = "25.00" } }
        }
    };

    private static object CompletedOrder(string captureStatus = "COMPLETED") => new
    {
        id = OrderId,
        status = "COMPLETED",
        purchase_units = new[]
        {
            new
            {
                reference_id = TradeNo,
                amount = new { currency_code = "EUR", value = "25.00" },
                payments = new
                {
                    captures = new[]
                    {
                        new { id = CaptureId, status = captureStatus, amount = new { currency_code = "EUR", value = "25.00" } }
                    }
                }
            }
        }
    };

    private static HttpResponseMessage Json(object payload, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(payload))
    };

    private IEnumerable<RecordedRequest> CaptureRequests
        => _handler.Requests.Where(r => r.Method == HttpMethod.Post && r.Path == $"/v2/checkout/orders/{OrderId}/capture");

    // ---- 收款 ----

    [Fact]
    public async Task Finalize_CapturesAnApprovedOrder_AndReportsTheCapture()
    {
        var provider = CreateProvider();
        _handler.OnGet($"/v2/checkout/orders/{OrderId}", _ => Json(ApprovedOrder()));
        _handler.OnPost($"/v2/checkout/orders/{OrderId}/capture", _ => Json(CompletedOrder(), HttpStatusCode.Created));

        var result = await provider.FinalizePaymentAsync(OrderId);

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Status.ShouldBe(PaymentStatus.Succeeded);
        result.Data.ExternalTradeNo.ShouldBe(CaptureId);
        result.Data.Amount.ShouldBe(25m);
        result.Data.Currency.ShouldBe("EUR");
        // 幂等键按内部流水号：付款人回站的同步与批准回调同时到达也只收一次
        CaptureRequests.ShouldHaveSingleItem().IdempotencyKey.ShouldBe($"cap:{TradeNo}");
    }

    [Fact]
    public async Task Finalize_DoesNotCaptureAnOrderThatIsAlreadyCompleted()
    {
        var provider = CreateProvider();
        _handler.OnGet($"/v2/checkout/orders/{OrderId}", _ => Json(CompletedOrder()));

        var result = await provider.FinalizePaymentAsync(OrderId);

        result.Data!.Status.ShouldBe(PaymentStatus.Succeeded);
        result.Data.ExternalTradeNo.ShouldBe(CaptureId);
        CaptureRequests.ShouldBeEmpty();
    }

    /// <summary>PayPal 拒绝 capture（付款方式被拒之类）：以订单此刻的状态为准，仍可付的就是处理中，不当成失败也不当成成功。</summary>
    [Fact]
    public async Task Finalize_WhenPayPalRefusesTheCapture_ReportsTheOrdersCurrentState()
    {
        var provider = CreateProvider();
        _handler.OnGet($"/v2/checkout/orders/{OrderId}", _ => Json(ApprovedOrder()));
        _handler.OnPost($"/v2/checkout/orders/{OrderId}/capture", _ => Json(new { name = "UNPROCESSABLE_ENTITY", details = new[] { new { issue = "INSTRUMENT_DECLINED" } } }, HttpStatusCode.UnprocessableEntity));

        var result = await provider.FinalizePaymentAsync(OrderId);

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Status.ShouldBe(PaymentStatus.Processing);
    }

    /// <summary>PayPal 自己出了问题：收没收到说不准，答失败让调用方重来（回调答 5xx 让 PayPal 重投）。</summary>
    [Fact]
    public async Task Finalize_WhenTheCaptureFailsOnPayPalsSide_Fails()
    {
        var provider = CreateProvider();
        _handler.OnGet($"/v2/checkout/orders/{OrderId}", _ => Json(ApprovedOrder()));
        _handler.OnPost($"/v2/checkout/orders/{OrderId}/capture", _ => Json(new { name = "INTERNAL_SERVER_ERROR" }, HttpStatusCode.InternalServerError));

        var result = await provider.FinalizePaymentAsync(OrderId);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(502);
    }

    // ---- 只读路径绝不收款 ----

    /// <summary>★ 关单 / 过期核实走同步：替一张正在被关掉的单收钱，等于向一个刚取消的人扣款。</summary>
    [Fact]
    public async Task Sync_NeverCaptures_AndReadsAnApprovedOrderAsProcessing()
    {
        var provider = CreateProvider();
        _handler.OnGet($"/v2/checkout/orders/{OrderId}", _ => Json(ApprovedOrder()));
        _handler.OnPost($"/v2/checkout/orders/{OrderId}/capture", _ => Json(CompletedOrder(), HttpStatusCode.Created));

        var synced = await provider.SyncOrderAsync(OrderId);

        synced.Data!.Status.ShouldBe(PaymentStatus.Processing);
        CaptureRequests.ShouldBeEmpty();
    }

    /// <summary>★ 收到钱的订单回报 capture id（退款打的是 capture），金额与币种取 capture 的。</summary>
    [Fact]
    public async Task Sync_OfACompletedOrder_ReportsTheCaptureId()
    {
        var provider = CreateProvider();
        _handler.OnGet($"/v2/checkout/orders/{OrderId}", _ => Json(CompletedOrder()));

        var result = await provider.SyncOrderAsync(OrderId);

        result.Data!.Status.ShouldBe(PaymentStatus.Succeeded);
        result.Data.ExternalTradeNo.ShouldBe(CaptureId);
        result.Data.Currency.ShouldBe("EUR");
    }

    [Fact]
    public async Task Sync_OfACompletedOrderWhoseCaptureIsPending_IsStillProcessing()
    {
        var provider = CreateProvider();
        _handler.OnGet($"/v2/checkout/orders/{OrderId}", _ => Json(CompletedOrder(captureStatus: "PENDING")));

        (await provider.SyncOrderAsync(OrderId)).Data!.Status.ShouldBe(PaymentStatus.Processing);
    }

    [Fact]
    public async Task Sync_OfAVoidedOrder_IsAFailure()
    {
        var provider = CreateProvider();
        _handler.OnGet($"/v2/checkout/orders/{OrderId}", _ => Json(new { id = OrderId, status = "VOIDED" }));

        (await provider.SyncOrderAsync(OrderId)).Data!.Status.ShouldBe(PaymentStatus.Failed);
    }

    /// <summary>★ 经回调落定的支付存的是 capture id：订单接口答 404，按 capture 查。</summary>
    [Fact]
    public async Task Sync_GivenACaptureId_ReadsTheCapture()
    {
        var provider = CreateProvider();
        _handler.OnGet($"/v2/payments/captures/{CaptureId}", _ => Json(new
        {
            id = CaptureId,
            status = "COMPLETED",
            amount = new { currency_code = "EUR", value = "25.00" }
        }));

        var result = await provider.SyncOrderAsync(CaptureId);

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Status.ShouldBe(PaymentStatus.Succeeded);
        result.Data.ExternalTradeNo.ShouldBe(CaptureId);
        result.Data.Amount.ShouldBe(25m);
    }

    // ---- 退款 ----

    /// <summary>
    /// ★ 修复之前落下的支付存的是订单 id：capture 接口答 404 时按订单找到它的 capture 再退一次，幂等键不变。
    /// </summary>
    [Fact]
    public async Task Refund_OfAPaymentRecordedWithItsOrderId_RefundsTheCapture()
    {
        var provider = CreateProvider();
        _handler.OnGet($"/v2/checkout/orders/{OrderId}", _ => Json(CompletedOrder()));
        _handler.OnPost($"/v2/payments/captures/{CaptureId}/refunds", _ => Json(new { id = "1JU08902781691411", status = "COMPLETED" }, HttpStatusCode.Created));

        var result = await provider.RefundAsync(new PaymentProviderRefundDto
        {
            TradeNo = TradeNo,
            ExternalTradeNo = OrderId,
            RefundNo = "RF-1",
            RefundAmount = 25m,
            Currency = "EUR"
        });

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Status.ShouldBe(RefundStatus.Succeeded);
        var refund = _handler.Requests.Single(r => r.Path == $"/v2/payments/captures/{CaptureId}/refunds");
        refund.IdempotencyKey.ShouldBe("re:RF-1");
    }

    // ---- 回调 ----

    [Fact]
    public async Task ApprovalCallback_AsksForFinalization()
    {
        var provider = CreateProvider();

        var approved = await provider.HandleCallbackAsync(Webhook("CHECKOUT.ORDER.APPROVED", new
        {
            id = OrderId,
            status = "APPROVED",
            purchase_units = new[] { new { reference_id = TradeNo, amount = new { currency_code = "EUR", value = "25.00" } } }
        }));
        var captured = await provider.HandleCallbackAsync(Webhook("PAYMENT.CAPTURE.COMPLETED", new
        {
            id = CaptureId,
            status = "COMPLETED",
            custom_id = TradeNo,
            amount = new { currency_code = "EUR", value = "25.00" }
        }));

        approved.Data!.Status.ShouldBe(PaymentStatus.Processing);
        approved.Data.RequiresFinalization.ShouldBeTrue();
        approved.Data.TradeNo.ShouldBe(TradeNo);
        captured.Data!.RequiresFinalization.ShouldBeFalse();
        captured.Data.ExternalTradeNo.ShouldBe(CaptureId);
    }

    /// <summary>
    /// ★ 订单资源上没有 <c>amount</c>：完成事件的金额、币种与收款 id 要从订单里的那笔收款上读。
    /// 此前恒以 0 金额进服务层，被判金额不符答 400，PayPal 一路重投。
    /// </summary>
    [Fact]
    public async Task OrderCompletedCallback_ReportsTheCapturedAmountAndCaptureId()
    {
        var provider = CreateProvider();

        var completed = await provider.HandleCallbackAsync(Webhook("CHECKOUT.ORDER.COMPLETED", CompletedOrder()));

        completed.Succeeded.ShouldBeTrue();
        completed.Data!.Status.ShouldBe(PaymentStatus.Succeeded);
        completed.Data.TradeNo.ShouldBe(TradeNo);
        completed.Data.PaidAmount.ShouldBe(25m);
        completed.Data.Currency.ShouldBe("EUR");
        completed.Data.ExternalTradeNo.ShouldBe(CaptureId);
    }

    /// <summary>订单完成却没有收款明细：没有可核对的金额，只确认收到，由收款事件落定。</summary>
    [Fact]
    public async Task OrderCompletedCallback_WithoutACapture_IsLeftToTheCaptureEvent()
    {
        var provider = CreateProvider();

        var completed = await provider.HandleCallbackAsync(Webhook("CHECKOUT.ORDER.COMPLETED", new
        {
            id = OrderId,
            status = "COMPLETED",
            purchase_units = new[] { new { reference_id = TradeNo, amount = new { currency_code = "EUR", value = "25.00" } } }
        }));

        completed.Succeeded.ShouldBeTrue();
        completed.Data!.IsHandled.ShouldBeFalse();
    }

    private static Dictionary<string, string> Webhook(string eventType, object resource) => new()
    {
        [PaymentConstants.CallbackRawBodyKey] = JsonSerializer.Serialize(new { id = "WH-" + eventType, event_type = eventType, resource })
    };
}
