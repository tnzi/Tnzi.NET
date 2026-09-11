namespace Tnzi.Payment.Stripe.Tests;

/// <summary>
/// Stripe webhook 的支付状态映射。
/// </summary>
/// <remarks>
/// 此前状态只由 <c>PaymentIntent.Status</c> 决定，而 Stripe 的 PaymentIntent
/// **根本没有 failed 这个状态**：一笔扣款失败后，intent 被退回
/// <c>requires_payment_method</c> 让客户重试。于是 <c>payment_intent.payment_failed</c>
/// 被映射成「处理中」，本地状态机拿不到终态 —— <c>PaymentFailedEvent</c> 永远不发布，
/// 订阅不降级 PastDue，为这笔订单核销掉的优惠券要等过期扫描才归还。
/// 判据必须是**事件类型**，与同一文件里 <c>payment_method.detached</c> 的处理同形。
/// </remarks>
public class StripeCallbackStatusTests
{
    private readonly StripeProvider _provider;

    public StripeCallbackStatusTests()
    {
        var options = new OptionsWrapper<StripeOptions>(new StripeOptions
        {
            Enabled = true,
            SecretKey = "sk_test_key",
            PublishableKey = "pk_test_key",
            WebhookSecret = string.Empty,
            Currency = "usd"
        });

        _provider = new StripeProvider(options, new Mock<ILogger<StripeProvider>>().Object);
    }

    private static string StripeApiVersion => global::Stripe.StripeConfiguration.ApiVersion;

    private static Dictionary<string, string> Event(string type, string intentStatus) => new()
    {
        // api_version 取 SDK 自己的版本：ParseEvent 默认在版本不一致时抛异常，
        // 写死一个字面量会让这组测试在下一次升级 Stripe.net 时整体变红，而原因与被测行为无关。
        [PaymentConstants.CallbackRawBodyKey] = $$"""
        {
          "id": "evt_test_1",
          "object": "event",
          "api_version": "{{StripeApiVersion}}",
          "type": "{{type}}",
          "data": {
            "object": {
              "id": "pi_test_1",
              "object": "payment_intent",
              "status": "{{intentStatus}}",
              "amount": 1000,
              "amount_received": 0,
              "currency": "usd",
              "metadata": { "TradeNo": "PAY-CB-1" }
            }
          }
        }
        """
    };

    [Fact]
    public async Task PaymentFailedEvent_MapsToFailed_NotProcessing()
    {
        // Stripe 把失败的 intent 退回 requires_payment_method；只看 intent 状态就读成「还在处理」
        var result = await _provider.HandleCallbackAsync(Event("payment_intent.payment_failed", "requires_payment_method"));

        result.Succeeded.ShouldBeTrue();
        result.Data!.TradeNo.ShouldBe("PAY-CB-1");
        result.Data.Status.ShouldBe(PaymentStatus.Failed);
    }

    [Fact]
    public async Task CanceledEvent_MapsToCancelled()
    {
        var result = await _provider.HandleCallbackAsync(Event("payment_intent.canceled", "canceled"));

        result.Succeeded.ShouldBeTrue();
        result.Data!.Status.ShouldBe(PaymentStatus.Cancelled);
    }

    [Fact]
    public async Task SucceededEvent_MapsToSucceeded()
    {
        var result = await _provider.HandleCallbackAsync(Event("payment_intent.succeeded", "succeeded"));

        result.Succeeded.ShouldBeTrue();
        result.Data!.Status.ShouldBe(PaymentStatus.Succeeded);
    }

    /// <summary>
    /// 回报的币种必须被带上来：金额是按它从最小单位换算回来的，
    /// 不带上去，服务层的一致性校验就只能比数值 ——
    /// 一笔 100 USD 的订单收到 100 JPY 的回报照样判成完全成功。
    /// </summary>
    [Fact]
    public async Task Callback_CarriesTheReportedCurrency()
    {
        var result = await _provider.HandleCallbackAsync(Event("payment_intent.succeeded", "succeeded"));

        result.Data!.Currency.ShouldBe("usd");
    }

    [Fact]
    public async Task CreatedEvent_StaysProcessing()
    {
        // 刚建单的 intent 也是 requires_payment_method —— 这才是它正常的初始状态，
        // 按 intent 状态一刀切判失败会把每一笔新订单当场判死
        var result = await _provider.HandleCallbackAsync(Event("payment_intent.created", "requires_payment_method"));

        result.Succeeded.ShouldBeTrue();
        result.Data!.Status.ShouldBe(PaymentStatus.Processing);
    }

    [Fact]
    public async Task RequiresActionEvent_StaysProcessing()
    {
        var result = await _provider.HandleCallbackAsync(
            Event("payment_intent.requires_action", "requires_action"));

        result.Succeeded.ShouldBeTrue();
        result.Data!.Status.ShouldBe(PaymentStatus.Processing);
    }
}
