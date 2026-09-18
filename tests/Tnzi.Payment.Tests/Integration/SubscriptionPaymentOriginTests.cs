using System.Text.Json;
using Tnzi.Payment.Dtos;
using Tnzi.Payment.Metadata;
using Tnzi.Payment.Services;
using Tnzi.Results;

namespace Tnzi.Payment.Tests.Integration;

/// <summary>
/// 订阅支付只能经系统通道建。
/// </summary>
/// <remarks>
/// <c>BusinessType.Subscription</c> 的支付带着计费元数据（<c>ExtraData</c>），支付完成事件按它推进订阅状态机。
/// 用户面 <c>POST /payments</c> 若也能建这种单，任何人拿自己的 0.5 元支付单加一段自填的元数据就能
/// 激活 / 续期任意价位的订阅，或拿别人的订阅号建一张不付的单，等它过期把别人的订阅打成 PastDue。
/// 这里是生产侧那一层：不是把元数据静默剥掉（那会让一次前端误传看起来像「付了钱但订阅没动」），
/// 而是当场拒绝。消费侧那一层（处理器按付款人与金额核对）在续费包的测试里。
/// </remarks>
public class SubscriptionPaymentOriginTests : PaymentIntegrationTestBase
{
    private const string ForgedMetadata = "{\"IsSubscriptionBilling\":true,\"Purpose\":0,\"SubscriptionId\":\"11111111-1111-1111-1111-111111111111\"}";

    private Task<Result<PaymentOrderResultDto>> CreateAsync(CreatePaymentDto request) =>
        InScopeAsync<IPaymentService, Result<PaymentOrderResultDto>>(svc => svc.CreatePaymentAsync(request));

    /// <summary>控制器路径：请求体来自用户，DTO 的系统标记绑不上，服务层拒绝。</summary>
    [Fact]
    public async Task AUserCreatedPayment_WithBusinessTypeSubscription_IsRefused()
    {
        var request = new CreatePaymentDto
        {
            BusinessOrderNo = "SUB-VICTIM",
            BusinessType = BusinessType.Subscription,
            Amount = 0.5m,
            Currency = "USD",
            ChannelCode = "Null",
            ExtraData = ForgedMetadata
        };

        var result = await CreateAsync(request);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(400);
        result.Message.ShouldBe(ErrorCodes.PaymentBusinessTypeSystemOnly);
    }

    /// <summary>
    /// 请求体里写上 <c>isSystemInitiated: true</c> 也没用：它是 <c>[JsonIgnore]</c> 的，
    /// 经模型绑定反序列化出来的 DTO 上恒为 false。
    /// </summary>
    [Fact]
    public async Task TheSystemMarker_CannotBeSetFromTheRequestBody()
    {
        const string body = """
            {"businessOrderNo":"SUB-VICTIM","businessType":2,"amount":0.5,"currency":"USD","channelCode":"Null",
             "isSystemInitiated":true,
             "extraData":"{\"IsSubscriptionBilling\":true,\"Purpose\":0}"}
            """;

        var request = JsonSerializer.Deserialize<CreatePaymentDto>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        request.BusinessType.ShouldBe(BusinessType.Subscription);
        request.IsSystemInitiated.ShouldBeFalse();

        var result = await CreateAsync(request);
        result.Succeeded.ShouldBeFalse();
        result.Message.ShouldBe(ErrorCodes.PaymentBusinessTypeSystemOnly);
    }

    /// <summary>系统通道（订阅模块自己建首付 / 补差单）照常。</summary>
    [Fact]
    public async Task ASystemInitiatedSubscriptionPayment_IsAccepted()
    {
        var request = new CreatePaymentDto
        {
            BusinessOrderNo = "SUB-LEGIT",
            BusinessType = BusinessType.Subscription,
            Amount = 99m,
            Currency = "USD",
            ChannelCode = "Null",
            ExtraData = ForgedMetadata,
            IsSystemInitiated = true
        };

        var result = await CreateAsync(request);

        result.Succeeded.ShouldBeTrue();
    }

    /// <summary>其它业务类型的用户面建单一个字节不差。</summary>
    [Fact]
    public async Task AUserCreatedOrderPayment_IsStillAccepted()
    {
        var request = new CreatePaymentDto
        {
            BusinessOrderNo = "ORDER-1",
            BusinessType = BusinessType.Order,
            Amount = 10m,
            Currency = "USD",
            ChannelCode = "Null",
            ExtraData = "{\"note\":\"anything\"}"
        };

        (await CreateAsync(request)).Succeeded.ShouldBeTrue();
    }
}
