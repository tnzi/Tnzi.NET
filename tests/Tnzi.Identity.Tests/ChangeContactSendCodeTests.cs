using Microsoft.AspNetCore.Mvc;
using Tnzi.Identity.Controllers;

namespace Tnzi.Identity.Tests;

/// <summary>
/// 换绑邮箱 / 手机的发码端点先校验新地址的形态，再交给发码服务。
/// </summary>
/// <remarks>
/// <para>
/// ★ 此前新地址只有 <c>[Required]</c>，同一个 DTO 同时用于邮箱与手机：任意字符串（带逗号的地址串、带显示名的地址、
/// 不是手机号的东西）都会以框架品牌的验证码邮件 / 短信发出去；超过列宽的地址在插入时 500。
/// 确认端点对邮箱有 <c>[EmailAddress]</c>，发码端却没有，两端对「什么是一个地址」答案不同。
/// </para>
/// <para>
/// 刻意<b>不</b>在发码时检查新地址是否已被别的账号占用：那会给每个登录用户一个按地址查账号的探针；
/// 占用由确认时（要求出示收到的码）判定。
/// </para>
/// </remarks>
public class ChangeContactSendCodeTests
{
    private readonly Mock<ITwoFactorService> _twoFactor = new();

    public ChangeContactSendCodeTests()
    {
        _twoFactor
            .Setup(t => t.SendCodeByAddressAsync(It.IsAny<string>(), It.IsAny<TwoFactorType>(), It.IsAny<VerificationCodePurpose>(), It.IsAny<Guid?>()))
            .ReturnsAsync(Result.Success());
    }

    private DefaultUserProfileController Build()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.Setup(u => u.Id).Returns(Guid.NewGuid());
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(currentUser.Object);

        return new DefaultUserProfileController(
            new Mock<IUserService>().Object,
            new Mock<IPasswordService>().Object,
            twoFactorService: _twoFactor.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() } },
        };
    }

    private void VerifyNothingSent()
        => _twoFactor.Verify(
            t => t.SendCodeByAddressAsync(It.IsAny<string>(), It.IsAny<TwoFactorType>(), It.IsAny<VerificationCodePurpose>(), It.IsAny<Guid?>()),
            Times.Never);

    public static TheoryData<string> NotOneEmailAddress => new()
    {
        "not-an-email",
        "victim@example.com, other@example.com",
        "victim@example.com,other",
        "Victim <victim@example.com>",
        "+1 555 0100",
        new string('a', 250) + "@example.com",
    };

    [Theory]
    [MemberData(nameof(NotOneEmailAddress))]
    public async Task Change_email_send_code_refuses_anything_but_one_email_address(string address)
    {
        var result = await Build().SendChangeEmailCode(new SendChangeVerificationCodeDto { NewAddress = address });

        result.Code.ShouldBe(400);
        VerifyNothingSent();
    }

    [Theory]
    [InlineData("user+tag@example.com")]
    [InlineData(" Mixed.Case@Example.com ")]
    public async Task Change_email_send_code_sends_to_a_plain_email_address(string address)
    {
        var result = await Build().SendChangeEmailCode(new SendChangeVerificationCodeDto { NewAddress = address });

        result.Success.ShouldBeTrue(result.Message);
        _twoFactor.Verify(t => t.SendCodeByAddressAsync(address, TwoFactorType.Email, VerificationCodePurpose.ChangeContact, null), Times.Once);
    }

    [Theory]
    [InlineData("victim@example.com")]
    [InlineData("call me maybe")]
    [InlineData("+1 555 0100; +1 555 0101")]
    public async Task Change_phone_send_code_refuses_anything_but_a_phone_number(string address)
    {
        var result = await Build().SendChangePhoneCode(new SendChangeVerificationCodeDto { NewAddress = address });

        result.Code.ShouldBe(400);
        VerifyNothingSent();
    }

    [Theory]
    [InlineData("+1 (555) 010-0100")]
    [InlineData("13800138000")]
    public async Task Change_phone_send_code_sends_to_a_phone_number(string address)
    {
        var result = await Build().SendChangePhoneCode(new SendChangeVerificationCodeDto { NewAddress = address });

        result.Success.ShouldBeTrue(result.Message);
        _twoFactor.Verify(t => t.SendCodeByAddressAsync(address, TwoFactorType.Sms, VerificationCodePurpose.ChangeContact, null), Times.Once);
    }
}
