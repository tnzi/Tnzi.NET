namespace Tnzi.Notification.Tests.Services;

/// <summary>
/// 具名发送器配置节（<c>MailSenders:{key}</c> 这一族）走与默认节<b>同一套</b>校验。
/// </summary>
/// <remarks>
/// ★ 这组测试守的是「具名的那家配错了却启动得起来」：默认节缺 <c>SmtpServer</c> 是启动期错误，
/// 具名节缺同一个字段若能悄悄过关，症状就是带键的消息全部投递失败、而不带键的一切正常。
/// </remarks>
public class NamedSenderOptionsValidationTests
{
    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(NotificationOptions options)
        => new NotificationOptionsValidator().Validate(name: null, options);

    /// <summary>与默认节同一条规则、同一个字段名，路径前缀指向那一节。</summary>
    [Fact]
    public void ANamedMailProfileMissingItsServer_FailsWithTheProfilePath()
    {
        var options = new NotificationOptions
        {
            MailSenders =
            {
                ["marketing"] = new MailSenderOptions { SmtpServer = "", FromEmail = "news@example.com", EnableSsl = false }
            }
        };

        var result = Validate(options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("MailSenders:marketing.SmtpServer");
    }

    [Fact]
    public void ANamedSmsProfileMissingItsCredentials_FailsWithTheProfilePath()
    {
        var options = new NotificationOptions
        {
            SmsSenders =
            {
                ["otp"] = new SmsSenderOptions { Provider = "twilio", TwilioAccountSid = "", TwilioAuthToken = "t", TwilioFromPhoneNumber = "+1" }
            }
        };

        var result = Validate(options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("SmsSenders:otp.TwilioAccountSid");
    }

    [Fact]
    public void ANamedPushProfileMissingItsProject_FailsWithTheProfilePath()
    {
        var options = new NotificationOptions
        {
            PushSenders =
            {
                ["ops"] = new PushSenderOptions { Provider = "fcm", FirebaseProjectId = "" }
            }
        };

        var result = Validate(options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("PushSenders:ops.FirebaseProjectId");
    }

    [Fact]
    public void ANamedFaxProfileWithAnEmailAddressAsGateway_FailsWithTheProfilePath()
    {
        var options = new NotificationOptions
        {
            FaxSenders =
            {
                ["legal"] = new FaxSenderOptions { GatewayDomain = "fax@example.com" }
            }
        };

        var result = Validate(options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("FaxSenders:legal.GatewayDomain");
    }

    /// <summary>
    /// ★ 回执轮询只从默认节 <c>FaxSender:Confirmation</c> 注册。具名节上的收件箱不会被任何东西读到：
    /// 此前它照样被当成生效的配置校验，经那个网关失败的传真永远停在「已发送」，日志里什么都没有。
    /// </summary>
    [Fact]
    public void ANamedFaxProfileWithItsOwnConfirmationMailbox_IsRejected()
    {
        var options = new NotificationOptions
        {
            FaxSenders =
            {
                ["legal"] = new FaxSenderOptions
                {
                    GatewayDomain = "fax.legal.example.com",
                    Confirmation = new FaxConfirmationOptions { Host = "imap.legal.example.com", UserName = "fax", Password = "p" }
                }
            }
        };

        var result = Validate(options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("FaxSenders:legal.Confirmation is not supported");
        result.FailureMessage.ShouldContain("Notification:FaxSender:Confirmation");
    }

    /// <summary>对照：默认节上的收件箱照常有效。</summary>
    [Fact]
    public void TheDefaultFaxProfilesConfirmationMailbox_StillPasses()
    {
        var options = new NotificationOptions
        {
            FaxSender = new FaxSenderOptions
            {
                GatewayDomain = "fax.example.com",
                Confirmation = new FaxConfirmationOptions { Host = "imap.example.com", UserName = "fax", Password = "p" }
            },
            FaxSenders =
            {
                ["legal"] = new FaxSenderOptions { GatewayDomain = "fax.legal.example.com" }
            }
        };

        Validate(options).Succeeded.ShouldBeTrue();
    }

    /// <summary>
    /// ★ <c>default</c> 是保留键：默认发送器永远是单数那一节，配置里再定义一个同名 profile
    /// 就是同一个键两个来源，哪个生效取决于注册顺序。
    /// </summary>
    [Theory]
    [InlineData("default")]
    [InlineData("Default")]
    public void ANamedProfileCalledDefault_IsRejected(string key)
    {
        var options = new NotificationOptions
        {
            MailSenders =
            {
                [key] = new MailSenderOptions { SmtpServer = "smtp.example.com", FromEmail = "a@example.com", EnableSsl = false }
            }
        };

        var result = Validate(options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("reserved");
    }

    /// <summary>键的形状在这里就拦：没有代码引用配置节的名字，写错了直到有消息带着它来才失败。</summary>
    [Fact]
    public void AMalformedProfileKey_IsRejected()
    {
        var options = new NotificationOptions
        {
            SmsSenders =
            {
                ["bad key"] = new SmsSenderOptions { Provider = "plivo", PlivoAuthId = "a", PlivoAuthToken = "t", PlivoFromPhoneNumber = "+1" }
            }
        };

        var result = Validate(options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("SmsSenders:bad key");
    }

    [Fact]
    public void AFaxProfileWithAMalformedEmailProviderKey_IsRejected()
    {
        var options = new NotificationOptions
        {
            FaxSender = new FaxSenderOptions { GatewayDomain = "fax.example.com", EmailProviderKey = "no spaces allowed" }
        };

        var result = Validate(options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("FaxSender.EmailProviderKey");
    }

    /// <summary>配对了的具名节与默认节共存，一切放行。</summary>
    [Fact]
    public void WellFormedNamedProfiles_Pass()
    {
        var options = new NotificationOptions
        {
            MailSender = new MailSenderOptions { SmtpServer = "smtp.example.com", FromEmail = "a@example.com", EnableSsl = false },
            MailSenders =
            {
                ["Marketing"] = new MailSenderOptions { SmtpServer = "smtp.news.example.com", FromEmail = "news@example.com", EnableSsl = false }
            },
            FaxSender = new FaxSenderOptions { GatewayDomain = "fax.example.com", EmailProviderKey = "Marketing" },
            FaxSenders =
            {
                ["legal"] = new FaxSenderOptions { GatewayDomain = "fax.legal.example.com" }
            }
        };

        Validate(options).Succeeded.ShouldBeTrue();
    }
}
