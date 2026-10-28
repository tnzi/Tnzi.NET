using System.ComponentModel.DataAnnotations;
using EmailAttribute = Tnzi.DataAnnotations.EmailAttribute;
using PhoneAttribute = Tnzi.DataAnnotations.PhoneAttribute;

namespace Tnzi.Tests.DataAnnotations;

/// <summary>
/// 钉住 <c>Tnzi.DataAnnotations</c> 的 <c>[Phone]</c> / <c>[Email]</c> 实际接受什么，与文档的描述对齐：
/// <c>[Phone]</c> 是北美 NANP 格式（不是中国大陆手机号格式），<c>[Email]</c> 只接受 ASCII 地址；
/// 两者默认必填，<c>Required = false</c> 时空值通过。
/// </summary>
public class PhoneAndEmailAttributeTests
{
    private static bool IsValid(ValidationAttribute attribute, object? value)
        => attribute.GetValidationResult(value, new ValidationContext(new object()) { DisplayName = "Field" }) == ValidationResult.Success;

    [Theory]
    [InlineData("416-555-0123")]
    [InlineData("(416) 555-0123")]
    [InlineData("+1 416 555 0123")]
    [InlineData("416.555.0123 ext 42")]
    public void Phone_AcceptsNorthAmericanNumbers(string value)
    {
        Assert.True(IsValid(new PhoneAttribute(), value));
    }

    [Theory]
    [InlineData("13800138000")]
    [InlineData("+44 20 7946 0958")]
    [InlineData("116-555-0123")]
    public void Phone_RejectsNumbersOutsideTheNorthAmericanPlan(string value)
    {
        Assert.False(IsValid(new PhoneAttribute(), value));
    }

    [Fact]
    public void Phone_IsRequiredByDefault_AndOptionalWhenConfigured()
    {
        Assert.False(IsValid(new PhoneAttribute(), null));
        Assert.True(IsValid(new PhoneAttribute { Required = false }, null));
    }

    [Theory]
    [InlineData("user@example.com", true)]
    [InlineData("first.last+tag@sub.example.org", true)]
    [InlineData("用户@例子.中国", false)]
    [InlineData("not-an-address", false)]
    public void Email_AcceptsAsciiAddressesOnly(string value, bool expected)
    {
        Assert.Equal(expected, IsValid(new EmailAttribute(), value));
    }
}
