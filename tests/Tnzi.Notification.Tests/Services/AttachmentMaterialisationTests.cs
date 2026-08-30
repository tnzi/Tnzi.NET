namespace Tnzi.Notification.Tests.Services;

/// <summary>
/// 取不到的附件必须让这次投递<b>失败</b>，不能记一条 warning 就把信发出去。
/// </summary>
/// <remarks>
/// <para>
/// ★★ <b>传真上这件事是彻底的</b>：<c>FaxEnvelope</c> 按网关要求把正文置空，一份传真的全部
/// 内容就是那个 PDF。附件丢了，网关收到的是一封什么都没有的信，而收件人被记成 <c>Sent</c>
/// —— 运营看到的是「已投递」，对方什么也没收到，并且没有任何东西会告诉任何一方。
/// </para>
/// <para>
/// ★ <b>邮件同理只是不那么显眼</b>：收件人拿到正文却没有发票，状态仍然是已送达。
/// </para>
/// <para>
/// ★ 每条「必须失败」都配一条<b>对照</b>：只写否定断言看不出自己是不是在验一条恒假的条件。
/// 对照用一个已取消的令牌 + 内存附件 —— 内存附件不做任何 I/O，所以失败原因只能来自
/// 附件之后的那一步（连 SMTP），这就证明附件那一关是过了的，而且全程不碰网络。
/// </para>
/// </remarks>
public class AttachmentMaterialisationTests
{
    private readonly Mock<IHttpClientFactory> _httpClientFactory = new();
    private readonly NotificationOptions _options = new()
    {
        MaxConcurrency = 4,
        MailSender = new MailSenderOptions
        {
            FromEmail = "noreply@example.com",
            FromName = "Test",
            SmtpServer = "smtp.example.com",
            SmtpPort = 587,
            EnableSsl = true,
            Username = "u",
            Password = "p",
        },
        FaxSender = new FaxSenderOptions { Enabled = true, GatewayDomain = "fax.example.com" },
    };

    // ── 必须失败 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnAttachmentPathThatDoesNotExist_FailsTheSend()
    {
        var result = await SendWithAttachmentAsync(new EmailAttachment
        {
            FileName = "invoice.pdf",
            FilePath = Path.Combine(Path.GetTempPath(), "tnzi-does-not-exist-" + Guid.NewGuid().ToString("N") + ".pdf"),
            ContentType = "application/pdf",
        });

        result.Success.ShouldBeFalse();
        result.FailureReason!.ShouldContain("invoice.pdf");
    }

    [Fact]
    public async Task AnAttachmentUrlThatCannotBeDownloaded_FailsTheSend()
    {
        UseHttpHandlerThatThrows();

        var result = await SendWithAttachmentAsync(new EmailAttachment
        {
            FileName = "invoice.pdf",
            FilePath = "https://files.example.com/invoice.pdf",
            ContentType = "application/pdf",
        });

        result.Success.ShouldBeFalse();
        result.FailureReason!.ShouldContain("invoice.pdf");
    }

    [Fact]
    public async Task AnAttachmentWithNeitherContentNorPath_FailsTheSend()
    {
        var result = await SendWithAttachmentAsync(new EmailAttachment
        {
            FileName = "invoice.pdf",
            ContentType = "application/pdf",
        });

        result.Success.ShouldBeFalse();
        result.FailureReason!.ShouldContain("invoice.pdf");
    }

    /// <summary>
    /// ★★ 这条是本轮的要害：一份取不到 PDF 的传真，不能回一个成功结果 ——
    /// 上游会据此把收件人记成 <c>Sent</c>。
    /// </summary>
    [Fact]
    public async Task AFaxWhosePdfCannotBeFetched_IsNotReportedAsSent()
    {
        UseHttpHandlerThatThrows();
        var fax = new EmailToFaxSender(_options, NewEmailSender(), new Mock<ILogger<EmailToFaxSender>>().Object);

        var result = await fax.SendToAsync("+1 613 555 0134", new EmailAttachment
        {
            FileName = "letter.pdf",
            FilePath = "https://files.example.com/letter.pdf",
            ContentType = "application/pdf",
        });

        result.Success.ShouldBeFalse();
        result.FailureReason!.ShouldContain("letter.pdf");
    }

    // ── 对照：附件装得上就不该在这一关失败 ──────────────────────────────────

    [Fact]
    public async Task AnInMemoryAttachment_GetsPastMaterialisation()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var result = await NewEmailSender().SendAsync(new EmailMessage
        {
            To = [new EmailAddress("someone@example.com")],
            Subject = "Statement",
            Body = "See attached.",
            Attachments =
            [
                new EmailAttachment
                {
                    FileName = "invoice.pdf",
                    Content = [0x25, 0x50, 0x44, 0x46, 0x2D],
                    ContentType = "application/pdf",
                }
            ],
        }, cancelled.Token);

        // 失败是必然的（令牌已取消，连不上 SMTP），要断言的是它**不是**附件那一关失败的。
        result.Success.ShouldBeFalse();
        result.FailureReason!.ShouldNotContain("invoice.pdf");
    }

    [Fact]
    public async Task AnAttachmentFileThatExists_GetsPastMaterialisation()
    {
        var path = Path.Combine(Path.GetTempPath(), "tnzi-attachment-" + Guid.NewGuid().ToString("N") + ".pdf");
        await File.WriteAllBytesAsync(path, [0x25, 0x50, 0x44, 0x46, 0x2D]);
        try
        {
            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync();

            var result = await NewEmailSender().SendAsync(new EmailMessage
            {
                To = [new EmailAddress("someone@example.com")],
                Subject = "Statement",
                Body = "See attached.",
                Attachments = [new EmailAttachment { FileName = "invoice.pdf", FilePath = path, ContentType = "application/pdf" }],
            }, cancelled.Token);

            result.FailureReason!.ShouldNotContain("was not found");
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ── 夹具 ─────────────────────────────────────────────────────────────────

    private MailKitEmailSender NewEmailSender()
        => new(_options, _httpClientFactory.Object, new Mock<ILogger<MailKitEmailSender>>().Object);

    private Task<SendResult> SendWithAttachmentAsync(EmailAttachment attachment)
        => NewEmailSender().SendAsync(new EmailMessage
        {
            To = [new EmailAddress("someone@example.com")],
            Subject = "Statement",
            Body = "See attached.",
            Attachments = [attachment],
        });

    private void UseHttpHandlerThatThrows()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("the object storage answered 404"));
        _httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler.Object));
    }
}
