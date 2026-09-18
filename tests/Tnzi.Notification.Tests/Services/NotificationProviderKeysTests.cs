namespace Tnzi.Notification.Tests.Services;

/// <summary>
/// 服务商键的收口规则：它同时约束配置节、请求、选择器返回值与落库值，四处对得上号全靠这一份。
/// </summary>
public class NotificationProviderKeysTests
{
    /// <summary>「默认」只有一种落库写法：null。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("default")]
    [InlineData("DEFAULT")]
    [InlineData("  Default ")]
    public void TheDefaultSender_NormalisesToNull(string? providerKey)
    {
        NotificationProviderKeys.Normalize(providerKey).ShouldBeNull();
        NotificationProviderKeys.IsDefault(providerKey).ShouldBeTrue();
    }

    /// <summary>
    /// ★ 具名键的规范形态是小写：keyed service 按 <c>object.Equals</c> 比较键，
    /// 配置里写 <c>Marketing</c>、请求里写 <c>MARKETING</c>，取到的必须是同一个发送器。
    /// </summary>
    [Theory]
    [InlineData("marketing", "marketing")]
    [InlineData("Marketing", "marketing")]
    [InlineData("  SendGrid-EU ", "sendgrid-eu")]
    public void ANamedKey_NormalisesToLowercase(string providerKey, string expected)
    {
        NotificationProviderKeys.Normalize(providerKey).ShouldBe(expected);
        NotificationProviderKeys.IsDefault(providerKey).ShouldBeFalse();
    }

    [Theory]
    [InlineData("marketing")]
    [InlineData("sms-cn")]
    [InlineData("a.b_c-1")]
    [InlineData("1st")]
    public void AWellFormedKey_IsValid(string providerKey)
    {
        NotificationProviderKeys.IsValid(providerKey).ShouldBeTrue();
        NotificationProviderKeys.Describe(providerKey).ShouldBeNull();
    }

    /// <summary>
    /// 键会进配置节路径与日志：空白、冒号、斜杠都会让 <c>Notification:MailSenders:{key}</c> 本身歧义。
    /// </summary>
    [Theory]
    [InlineData("has space")]
    [InlineData("a:b")]
    [InlineData("a/b")]
    [InlineData("-leading")]
    [InlineData("_leading")]
    [InlineData("中文")]
    public void AMalformedKey_IsRejectedWithAReason(string providerKey)
    {
        NotificationProviderKeys.IsValid(providerKey).ShouldBeFalse();
        NotificationProviderKeys.Describe(providerKey).ShouldNotBeNull().ShouldContain(providerKey);
    }

    [Fact]
    public void AKeyLongerThanTheColumn_IsRejected()
    {
        var tooLong = new string('a', NotificationProviderKeys.MaxLength + 1);

        NotificationProviderKeys.IsValid(tooLong).ShouldBeFalse();
        NotificationProviderKeys.Describe(tooLong).ShouldNotBeNull().ShouldContain(NotificationProviderKeys.MaxLength.ToString());
        NotificationProviderKeys.IsValid(new string('a', NotificationProviderKeys.MaxLength)).ShouldBeTrue();
    }
}
