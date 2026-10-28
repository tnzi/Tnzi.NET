namespace Tnzi.Payment.Stripe.Tests;

/// <summary>
/// Stripe webhook 的验签与事件 API 版本是两件事。
/// </summary>
/// <remarks>
/// SDK 的 <c>ConstructEvent</c> / <c>ParseEvent</c> 默认在事件的 API 版本与 SDK 自己的发布线不一致时抛异常，
/// 而此前验签把任何 <c>StripeException</c> 都读成「签名无效」：webhook 端点建在账户默认的旧发布线上、
/// 或升级 Stripe.net 换了发布线而端点没跟着改，**每一条**真回调都被答 400 INVALID_SIGNATURE，
/// Stripe 持续重投直至禁用端点，失败、作废与撤卡事件全部丢失，日志说的却是签名不对。
/// 这里读的字段（事件 id/type、PaymentIntent 的 id/status/金额/币种/metadata/失败原因、PaymentMethod id）
/// 跨发布线稳定，所以签名对就收下，版本不一致单独记一条指名两边版本的告警。
/// </remarks>
public class StripeWebhookApiVersionTests
{
    private const string Secret = "whsec_test_secret";

    /// <summary>一个不是 SDK 当前发布线的版本。</summary>
    private const string OtherTrainVersion = "2025-03-31.basil";

    private readonly Mock<ILogger<StripeProvider>> _logger = new();
    private readonly StripeProvider _provider;

    public StripeWebhookApiVersionTests()
    {
        var options = new OptionsWrapper<StripeOptions>(new StripeOptions
        {
            Enabled = true,
            SecretKey = "sk_test_key",
            PublishableKey = "pk_test_key",
            WebhookSecret = Secret
        });

        _provider = new StripeProvider(options, _logger.Object);
    }

    private static string Body(string apiVersion) => $$"""
        {
          "id": "evt_version_1",
          "object": "event",
          "api_version": "{{apiVersion}}",
          "type": "payment_intent.succeeded",
          "data": {
            "object": {
              "id": "pi_version_1",
              "object": "payment_intent",
              "status": "succeeded",
              "amount": 1000,
              "amount_received": 1000,
              "currency": "usd",
              "metadata": { "TradeNo": "PAY-VER-1" }
            }
          }
        }
        """;

    private static Dictionary<string, string> Signed(string body, string secret = Secret) => new()
    {
        [PaymentConstants.CallbackRawBodyKey] = body,
        [PaymentConstants.CallbackStripeSignatureKey] = global::Stripe.EventUtility.GenerateSignatureHeader(body, secret)
    };

    [Fact]
    public async Task VerifySignatureAsync_AcceptsAValidSignatureFromAnotherReleaseTrain()
    {
        (await _provider.VerifySignatureAsync(Signed(Body(OtherTrainVersion)))).ShouldBeTrue();
    }

    [Fact]
    public async Task VerifySignatureAsync_AcceptsAValidSignatureOnTheSdkVersion()
    {
        (await _provider.VerifySignatureAsync(Signed(Body(global::Stripe.StripeConfiguration.ApiVersion)))).ShouldBeTrue();
    }

    [Fact]
    public async Task VerifySignatureAsync_StillRejectsAWrongSecret()
    {
        (await _provider.VerifySignatureAsync(Signed(Body(OtherTrainVersion), "whsec_someone_else"))).ShouldBeFalse();
    }

    [Fact]
    public async Task VerifySignatureAsync_StillRejectsATamperedBody()
    {
        var parameters = Signed(Body(OtherTrainVersion));
        parameters[PaymentConstants.CallbackRawBodyKey] = parameters[PaymentConstants.CallbackRawBodyKey].Replace("1000", "1", StringComparison.Ordinal);

        (await _provider.VerifySignatureAsync(parameters)).ShouldBeFalse();
    }

    [Fact]
    public async Task HandleCallbackAsync_ParsesAnEventFromAnotherReleaseTrain()
    {
        var result = await _provider.HandleCallbackAsync(Signed(Body(OtherTrainVersion)));

        result.Succeeded.ShouldBeTrue();
        result.Data!.TradeNo.ShouldBe("PAY-VER-1");
        result.Data.ExternalTradeNo.ShouldBe("pi_version_1");
        result.Data.Status.ShouldBe(PaymentStatus.Succeeded);
        result.Data.PaidAmount.ShouldBe(10m);
    }

    /// <summary>容忍不等于沉默：运维要能从日志里看出是端点版本没对齐，而不是被告知签名不对。</summary>
    [Fact]
    public async Task HandleCallbackAsync_LogsAVersionWarningNamingBothVersions()
    {
        await _provider.HandleCallbackAsync(Signed(Body(OtherTrainVersion)));

        _logger.Verify(x => x.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((state, _) =>
                state != null && state.ToString()!.Contains(OtherTrainVersion) &&
                state.ToString()!.Contains(global::Stripe.StripeConfiguration.ApiVersion)),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public async Task HandleCallbackAsync_DoesNotWarnOnTheSdkVersion()
    {
        await _provider.HandleCallbackAsync(Signed(Body(global::Stripe.StripeConfiguration.ApiVersion)));

        _logger.Verify(x => x.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);
    }
}
