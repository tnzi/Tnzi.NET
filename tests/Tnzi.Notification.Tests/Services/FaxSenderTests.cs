using System.Net.Mime;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;

namespace Tnzi.Notification.Tests.Services;

/// <summary>
/// <see cref="EmailToFaxSender"/> 与 <see cref="UnconfiguredFaxSender"/>：网关约束、演示重定向、未配置时的行为。
/// </summary>
/// <remarks>
/// 断言一律挂在**真正交给 <see cref="IEmailSender"/> 的那封邮件**上，而不是"调用没抛异常"。
/// 传真发错的方式全都是"网关收下了，然后什么也没发生"，只有检查那封邮件长什么样才看得出来。
/// </remarks>
public class FaxSenderTests
{
    private const string Gateway = "fax.example.com";

    /// <summary>一份最小的合法 PDF 头（只需要魔数，本组不解析 PDF 结构）。</summary>
    private static byte[] Pdf() => Encoding.ASCII.GetBytes("%PDF-1.4\n% minimal\n");

    [Fact]
    public async Task SendPdfAsync_AddressesTheGatewayWithTheNormalisedNumber()
    {
        var (sender, sent) = CreateSender();

        var result = await sender.SendPdfAsync("+1 (905) 555-1234", Pdf(), "letter.pdf");

        result.Success.ShouldBeTrue(result.FailureReason);
        sent.Value!.To.Single().Address.ShouldBe($"9055551234@{Gateway}");
    }

    /// <summary>网关域名写成 <c>@fax.example.com</c> 也认，免得配出 <c>905…@@fax…</c>。</summary>
    [Fact]
    public async Task SendPdfAsync_ToleratesALeadingAtInTheGatewayDomain()
    {
        var (sender, sent) = CreateSender(gateway: "@" + Gateway);

        await sender.SendPdfAsync("9055551234", Pdf(), "letter.pdf");

        sent.Value!.To.Single().Address.ShouldBe($"9055551234@{Gateway}");
    }

    /// <summary>
    /// ★ 网关的三条硬约束一次断完：正文为空、恰好一个附件、MIME 是 application/pdf。
    /// 说明书原文要求正文里不得有任何文字、图片或签名档 —— 多一样就不到达。
    /// </summary>
    [Fact]
    public async Task SendPdfAsync_SendsAnEmptyBodyAndExactlyOnePdfAttachment()
    {
        var (sender, sent) = CreateSender();

        await sender.SendPdfAsync("9055551234", Pdf(), "letter.pdf");

        var message = sent.Value!;
        message.Body.ShouldBeEmpty();
        message.IsHtml.ShouldBeFalse();
        message.Cc.ShouldBeEmpty();
        message.Bcc.ShouldBeEmpty();
        message.Attachments!.Count.ShouldBe(1);
        message.Attachments[0].ContentType.ShouldBe(MediaTypeNames.Application.Pdf);
        message.Attachments[0].FileName.ShouldBe("letter.pdf");
    }

    /// <summary>
    /// 调用方给了 octet-stream + <c>.pdf</c> 时，MIME 由框架补正 ——
    /// 有的网关看 MIME、有的看扩展名，两边说法不一致的那一次不会报错，只会不到达。
    /// </summary>
    [Fact]
    public async Task SendToAsync_NormalisesTheDeclaredContentType()
    {
        var (sender, sent) = CreateSender();

        await sender.SendToAsync("9055551234",
            EmailAttachment.FromBytes(Pdf(), "letter.pdf", MediaTypeNames.Application.Octet));

        sent.Value!.Attachments![0].ContentType.ShouldBe(MediaTypeNames.Application.Pdf);
    }

    /// <summary>文件名少了扩展名时补上，理由同上。</summary>
    [Fact]
    public async Task SendToAsync_GivesTheAttachmentAPdfExtension()
    {
        var (sender, sent) = CreateSender();

        await sender.SendToAsync("9055551234",
            EmailAttachment.FromBytes(Pdf(), "letter", MediaTypeNames.Application.Pdf));

        sent.Value!.Attachments![0].FileName.ShouldBe("letter.pdf");
    }

    [Fact]
    public async Task SendToAsync_DoesNotMutateTheCallersDocument()
    {
        var (sender, _) = CreateSender();
        var document = EmailAttachment.FromBytes(Pdf(), "letter", MediaTypeNames.Application.Octet);

        await sender.SendToAsync("9055551234", document);

        // 重试会复用同一份文档：就地改写会让第二次发送带着上一次的修改
        document.FileName.ShouldBe("letter");
        document.ContentType.ShouldBe(MediaTypeNames.Application.Octet);
    }

    [Fact]
    public async Task SendToAsync_PassesAFilePathDocumentThrough()
    {
        var (sender, sent) = CreateSender();

        await sender.SendToAsync("9055551234", EmailAttachment.FromFile("https://files.example.com/a.pdf"));

        sent.Value!.Attachments![0].FilePath.ShouldBe("https://files.example.com/a.pdf");
        sent.Value.Attachments[0].ContentType.ShouldBe(MediaTypeNames.Application.Pdf);
    }

    [Fact]
    public async Task SendPdfAsync_PutsTheSubjectOnTheMessage()
    {
        var (sender, sent) = CreateSender();

        await sender.SendPdfAsync("9055551234", Pdf(), "letter.pdf", subject: "File 2026-118");

        sent.Value!.Subject.ShouldBe("File 2026-118");
    }

    [Fact]
    public async Task SendPdfAsync_ReturnsTheCarrierMessageIdForLaterConfirmationMatching()
    {
        var (sender, _) = CreateSender();

        var result = await sender.SendPdfAsync("9055551234", Pdf(), "letter.pdf");

        result.ExternalMessageId.ShouldBe("email-id");
    }

    #region 演示 / 预发重定向

    /// <summary>
    /// ★ 重定向只换收件人，其余一切与生产路径逐字相同 —— 消费应用会在这个模式下跑几个月，
    /// 如果它是一条独立分支，那几个月验证的就是**另一条**代码路径。
    /// </summary>
    [Fact]
    public async Task WhenRedirected_OnlyTheRecipientChanges()
    {
        var (sender, sent) = CreateSender(devOverride: "demo@example.com");

        await sender.SendPdfAsync("9055551234", Pdf(), "letter.pdf", subject: "File 2026-118");

        var message = sent.Value!;
        message.To.Single().Address.ShouldBe("demo@example.com");
        message.Cc.ShouldBeEmpty();
        message.Bcc.ShouldBeEmpty();

        // 正文、附件、MIME 与不重定向时一模一样
        message.Body.ShouldBeEmpty();
        message.IsHtml.ShouldBeFalse();
        message.Attachments!.Count.ShouldBe(1);
        message.Attachments[0].ContentType.ShouldBe(MediaTypeNames.Application.Pdf);
    }

    /// <summary>重定向后仍要看得出这份传真本来发给谁，否则演示环境里全是一模一样的信。</summary>
    [Fact]
    public async Task WhenRedirected_TheIntendedFaxAddressStaysVisible()
    {
        var (sender, sent) = CreateSender(devOverride: "demo@example.com");

        await sender.SendPdfAsync("+1 905 555 1234", Pdf(), "letter.pdf", subject: "File 2026-118");

        sent.Value!.Subject.ShouldContain($"9055551234@{Gateway}");
        sent.Value.Subject.ShouldContain("File 2026-118");
    }

    /// <summary>重定向生效时，网关地址一个字节都不许留在收件人字段里。</summary>
    [Fact]
    public async Task WhenRedirected_TheGatewayNeverAppearsAsARecipient()
    {
        var (sender, sent) = CreateSender(devOverride: "demo@example.com");

        await sender.SendPdfAsync("9055551234", Pdf(), "letter.pdf");

        var addresses = sent.Value!.To.Concat(sent.Value.Cc).Concat(sent.Value.Bcc)
            .Select(a => a.Address).ToList();
        addresses.ShouldBe(["demo@example.com"]);
    }

    /// <summary>号码照样归一化：重定向不是"跳过校验"的开关。</summary>
    [Fact]
    public async Task WhenRedirected_AnUndialableNumberIsStillRejected()
    {
        var (sender, sent) = CreateSender(devOverride: "demo@example.com");

        var result = await sender.SendPdfAsync("nope", Pdf(), "letter.pdf");

        result.Success.ShouldBeFalse();
        sent.Value.ShouldBeNull();
    }

    #endregion

    #region 拒绝：号码与文档

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("9055551234 ext. 4")]
    public async Task SendPdfAsync_WithAnUndialableNumber_FailsWithoutSending(string number)
    {
        var (sender, sent) = CreateSender();

        var result = await sender.SendPdfAsync(number, Pdf(), "letter.pdf");

        result.Success.ShouldBeFalse();
        result.FailureReason.ShouldNotBeNullOrWhiteSpace();
        sent.Value.ShouldBeNull();
    }

    [Fact]
    public async Task SendToAsync_WithANonPdfDocument_FailsWithoutSending()
    {
        var (sender, sent) = CreateSender();

        var result = await sender.SendToAsync("9055551234",
            EmailAttachment.FromBytes(Encoding.ASCII.GetBytes("hello"), "letter.docx",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document"));

        result.Success.ShouldBeFalse();
        result.FailureReason!.ShouldContain("PDF");
        sent.Value.ShouldBeNull();
    }

    /// <summary>
    /// ★ 名字叫 <c>.pdf</c> 不等于是 PDF。"把 Word 另存成 letter.pdf"这一类，
    /// 网关照收，然后不到达 —— 字节在手就顺便看一眼魔数是唯一能提前发现它的机会。
    /// </summary>
    [Fact]
    public async Task SendPdfAsync_WithBytesThatAreNotAPdf_FailsWithoutSending()
    {
        var (sender, sent) = CreateSender();

        var result = await sender.SendPdfAsync("9055551234", Encoding.ASCII.GetBytes("PK zip"), "letter.pdf");

        result.Success.ShouldBeFalse();
        sent.Value.ShouldBeNull();
    }

    /// <summary>
    /// 没有文件名时"补上 .pdf"会得到一个叫 <c>.pdf</c> 的附件 —— 那不是任何人想寄出去的东西。
    /// 调用方漏填就说出来，别替他编一个名字。
    /// </summary>
    [Fact]
    public async Task SendToAsync_WithoutAFileName_FailsWithoutSending()
    {
        var (sender, sent) = CreateSender();

        var result = await sender.SendToAsync("9055551234",
            new EmailAttachment { Content = Pdf(), ContentType = MediaTypeNames.Application.Pdf });

        result.Success.ShouldBeFalse();
        result.FailureReason!.ShouldContain("file name");
        sent.Value.ShouldBeNull();
    }

    [Fact]
    public async Task SendToAsync_WithNeitherContentNorPath_FailsWithoutSending()
    {
        var (sender, sent) = CreateSender();

        var result = await sender.SendToAsync("9055551234",
            new EmailAttachment { FileName = "letter.pdf", ContentType = MediaTypeNames.Application.Pdf });

        result.Success.ShouldBeFalse();
        sent.Value.ShouldBeNull();
    }

    #endregion

    #region 未配置

    /// <summary>
    /// ★ SMTP 没配时 <see cref="IEmailSender"/> 是 <see cref="NullEmailSender"/>，而它**返回成功** ——
    /// 不拦这一道，每一份传真都会被记成已投递，一份都没发出去。
    /// </summary>
    [Fact]
    public async Task SendPdfAsync_WhenTheMailSenderIsNotConfigured_Fails()
    {
        var options = new NotificationOptions
        {
            MailSender = null,
            FaxSender = new FaxSenderOptions { GatewayDomain = Gateway }
        };
        IFaxSender sender = new EmailToFaxSender(
            options, new NullEmailSender(NullLogger<NullEmailSender>.Instance), NullLogger<EmailToFaxSender>.Instance);

        var result = await sender.SendPdfAsync("9055551234", Pdf(), "letter.pdf");

        result.Success.ShouldBeFalse();
        result.FailureReason!.ShouldContain("mail sender");
    }

    /// <summary>
    /// ★ 判的是「手上这个 sender 会不会真的发信」，不是「<c>MailSender</c> 那一节配没配」。
    /// </summary>
    /// <remarks>
    /// 消费应用注册自己的 <see cref="IEmailSender"/>（SendGrid / Postmark 这类 API 发信）时
    /// 根本不会配 SMTP 那一节。按配置判会把一个邮件通道完全正常的部署判成发不了传真，
    /// 还把人引向"去配 SMTP" —— 一个配置正确的部署被自己的守卫拦死，且提示词指错了方向。
    /// </remarks>
    [Fact]
    public async Task SendPdfAsync_WithAConsumerRegisteredEmailSenderAndNoSmtpSection_StillSends()
    {
        var options = new NotificationOptions
        {
            MailSender = null,
            FaxSender = new FaxSenderOptions { GatewayDomain = Gateway }
        };

        var sent = new StrongBox<EmailMessage?>(null);
        var emailSender = new Mock<IEmailSender>();
        emailSender
            .Setup(s => s.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback<EmailMessage, CancellationToken>((message, _) => sent.Value = message)
            .ReturnsAsync(SendResult.CreateSuccess("api-id"));

        IFaxSender sender = new EmailToFaxSender(options, emailSender.Object, NullLogger<EmailToFaxSender>.Instance);

        var result = await sender.SendPdfAsync("9055551234", Pdf(), "letter.pdf");

        result.Success.ShouldBeTrue(result.FailureReason);
        sent.Value!.To.Single().Address.ShouldBe($"9055551234@{Gateway}");
    }

    [Fact]
    public async Task SendPdfAsync_WhenTheFaxSectionIsDisabled_Fails()
    {
        var (sender, sent) = CreateSender(enabled: false);

        var result = await sender.SendPdfAsync("9055551234", Pdf(), "letter.pdf");

        result.Success.ShouldBeFalse();
        sent.Value.ShouldBeNull();
    }

    /// <summary>
    /// ★ 未配置的回退**失败**，不像 <see cref="NullEmailSender"/> 那样报成功。
    /// 一份传真通常是函件本身，"发过了"会被拿去交差；报成功等于给没发出去的传真开一张已投递证明，
    /// 而这种谎没有任何症状。
    /// </summary>
    [Fact]
    public async Task UnconfiguredFaxSender_FailsLoudlyInsteadOfPretendingToSend()
    {
        IFaxSender sender = new UnconfiguredFaxSender(NullLogger<UnconfiguredFaxSender>.Instance);

        var result = await sender.SendPdfAsync("9055551234", Pdf(), "letter.pdf");

        result.Success.ShouldBeFalse();
        result.FailureReason!.ShouldContain("Notification:FaxSender");
    }

    #endregion

    /// <summary>建一个把外发邮件截下来的 <see cref="EmailToFaxSender"/>。</summary>
    private static (IFaxSender Sender, StrongBox<EmailMessage?> Sent) CreateSender(
        string gateway = Gateway, string? devOverride = null, bool enabled = true)
    {
        var options = new NotificationOptions
        {
            MailSender = new MailSenderOptions { SmtpServer = "smtp.example.com", FromEmail = "noreply@example.com" },
            FaxSender = new FaxSenderOptions { Enabled = enabled, GatewayDomain = gateway, DevOverrideEmail = devOverride }
        };

        var sent = new StrongBox<EmailMessage?>(null);
        var emailSender = new Mock<IEmailSender>();
        emailSender
            .Setup(s => s.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback<EmailMessage, CancellationToken>((message, _) => sent.Value = message)
            .ReturnsAsync(SendResult.CreateSuccess("email-id"));

        return (new EmailToFaxSender(options, emailSender.Object, NullLogger<EmailToFaxSender>.Instance), sent);
    }
}
