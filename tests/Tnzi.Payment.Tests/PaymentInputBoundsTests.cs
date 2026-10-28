using System.ComponentModel.DataAnnotations;
using Tnzi.Payment.Dtos;
using Tnzi.Payment.Metadata;

namespace Tnzi.Payment.Tests;

/// <summary>
/// 用户面建单 / 退款的字符串字段在模型校验阶段就按列宽挡下，而不是写库时被数据库截断报错成 500。
/// </summary>
/// <remarks>
/// SQLite 不检查列宽，集成测试看不见这类 500；PostgreSQL / SQL Server 上一个 600 字的描述就是一次服务器错误。
/// 这里跑的是 MVC 模型绑定用的同一套 DataAnnotations 校验。
/// </remarks>
public class PaymentInputBoundsTests
{
    private static List<string> Validate(object dto)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);
        return results.SelectMany(r => r.MemberNames).ToList();
    }

    private static CreatePaymentDto ValidPayment() => new()
    {
        BusinessOrderNo = "ORDER-1",
        BusinessType = BusinessType.Order,
        Amount = 10m
    };

    [Fact]
    public void AWellFormedPayment_PassesValidation()
        => Validate(ValidPayment()).ShouldBeEmpty();

    [Theory]
    [InlineData(nameof(CreatePaymentDto.BusinessOrderNo), 129)]
    [InlineData(nameof(CreatePaymentDto.Currency), 9)]
    [InlineData(nameof(CreatePaymentDto.ChannelCode), 33)]
    [InlineData(nameof(CreatePaymentDto.Description), 501)]
    [InlineData(nameof(CreatePaymentDto.CouponCode), 65)]
    [InlineData(nameof(CreatePaymentDto.ExtraData), PaymentConstants.MaxExtraDataLength + 1)]
    public void APaymentFieldLongerThanItsColumn_IsRejected(string property, int length)
    {
        var dto = ValidPayment();
        typeof(CreatePaymentDto).GetProperty(property)!.SetValue(dto, new string('x', length));

        Validate(dto).ShouldContain(property);
    }

    [Fact]
    public void ARefundReasonLongerThanItsColumn_IsRejected()
    {
        var dto = new CreateRefundDto { TradeNo = "PAY-1", RefundAmount = 1m, Reason = new string('x', 501) };

        Validate(dto).ShouldContain(nameof(CreateRefundDto.Reason));
    }

    [Fact]
    public void ARefundTradeNoLongerThanItsColumn_IsRejected()
    {
        var dto = new CreateRefundDto { TradeNo = new string('x', 65), RefundAmount = 1m, Reason = "r" };

        Validate(dto).ShouldContain(nameof(CreateRefundDto.TradeNo));
    }

    /// <summary>审批备注写进 <c>Refund.ApproveRemark</c>（列宽 500）：管理端审批同样不该因为一段长备注拿到 500。</summary>
    [Fact]
    public void AnApprovalRemarkLongerThanItsColumn_IsRejected()
    {
        var dto = new ApproveRefundDto { Approved = true, Remark = new string('x', 501) };

        Validate(dto).ShouldContain(nameof(ApproveRefundDto.Remark));
    }
}
