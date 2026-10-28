using System.ComponentModel.DataAnnotations;

namespace Tnzi.Payment.Subscriptions.Tests;

/// <summary>
/// 开通订阅的字符串字段在模型校验阶段就按列宽挡下，而不是写库时被数据库截断报错成 500。
/// </summary>
/// <remarks>
/// 渠道代码存进 <c>Subscription.ChannelCode</c>（32）：渠道查不到时存的是请求原样，试用开通不建首期单，
/// 一个超长的渠道代码直接写进订阅行。支付方式凭据存进 <c>Subscription.PaymentMethodToken</c>（128）。
/// 优惠券代码与建单的 <c>CreatePaymentDto.CouponCode</c> 同一个上限。SQLite 不检查列宽，集成测试看不见这类 500。
/// </remarks>
public class SubscriptionInputBoundsTests
{
    private static List<string> Validate(object dto)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);
        return results.SelectMany(r => r.MemberNames).ToList();
    }

    [Fact]
    public void AWellFormedRequest_PassesValidation()
        => Validate(new CreateSubscriptionDto { PlanId = Guid.NewGuid(), ChannelCode = "Stripe", CouponCode = "WELCOME", PaymentMethodToken = "pm_123" })
            .ShouldBeEmpty();

    [Theory]
    [InlineData(nameof(CreateSubscriptionDto.ChannelCode), 33)]
    [InlineData(nameof(CreateSubscriptionDto.CouponCode), 65)]
    [InlineData(nameof(CreateSubscriptionDto.PaymentMethodToken), 129)]
    public void AFieldLongerThanItsColumn_IsRejected(string property, int length)
    {
        var dto = new CreateSubscriptionDto { PlanId = Guid.NewGuid() };
        typeof(CreateSubscriptionDto).GetProperty(property)!.SetValue(dto, new string('x', length));

        Validate(dto).ShouldContain(property);
    }
}
