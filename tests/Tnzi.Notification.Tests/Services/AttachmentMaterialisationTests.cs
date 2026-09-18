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
/// <para>
/// ★★ <b>来源纪律也在这里复核</b>（2026-09-12）：实体里的 <c>FilePath</c> 曾经原样来自管理端请求体，
/// 发信那一刻按它读任意本地文件 / 取任意 URL。入口那道门在 <c>AttachmentSourceValidationTests</c>；
/// 内置发送器不能只信实体（旧行、消费方自己的调用），所以取件前再按同一条
/// <c>AttachmentSourcePolicy</c> 复核一次，并给远程下载与本地文件加大小上限。
/// 默认配置下<b>没有任何允许的本地根目录</b>，本地路径一律失败 —— 这里的「文件存在」对照因此要显式放行临时目录。
/// </para>
/// </remarks>
public class AttachmentMaterialisationTests
{
    private readonly Mock<IHttpClientFactory> _httpClientFactory = new();
    private readonly NotificationOptions _options = new()
    {
        MaxConcurrency = 4,
        Attachments = new AttachmentOptions { AllowedLocalRoots = [Path.GetTempPath()] },
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
        var fax = new EmailToFaxSender(_options.FaxSender!, NewEmailSender(), new Mock<ILogger<EmailToFaxSender>>().Object);

        var result = await fax.SendToAsync("+1 613 555 0134", new EmailAttachment
        {
            FileName = "letter.pdf",
            FilePath = "https://files.example.com/letter.pdf",
            ContentType = "application/pdf",
        });

        result.Success.ShouldBeFalse();
        result.FailureReason!.ShouldContain("letter.pdf");
    }

    // ── 来源纪律：与入口同一条规则，取件前再复核一次 ─────────────────────────

    /// <summary>★★ 默认配置（没有允许的根目录）下，一个真实存在的本地文件也不能被装进信里。</summary>
    [Fact]
    public async Task ALocalAttachmentOutsideTheAllowedRoots_FailsTheSend()
    {
        _options.Attachments = new AttachmentOptions();
        var path = Path.Combine(Path.GetTempPath(), "tnzi-attachment-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, "{\"ConnectionStrings\":{}}");
        try
        {
            var result = await SendWithAttachmentAsync(new EmailAttachment
            {
                FileName = "appsettings.json",
                FilePath = path,
                ContentType = "application/json",
            });

            result.Success.ShouldBeFalse();
            result.FailureReason!.ShouldContain("appsettings.json");
            result.FailureReason!.ShouldContain("AllowedLocalRoots");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>★ 私网 / 云元数据地址在发出任何请求<b>之前</b>就被拦下。</summary>
    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://10.0.0.5:8500/v1/kv/?recurse")]
    public async Task AnAttachmentUrlOnThePrivateNetwork_FailsBeforeAnyRequestIsMade(string url)
    {
        var requests = UseHttpHandlerThatCountsRequests(body: new byte[8]);

        var result = await SendWithAttachmentAsync(new EmailAttachment
        {
            FileName = "meta.txt",
            FilePath = url,
            ContentType = "text/plain",
        });

        result.Success.ShouldBeFalse();
        result.FailureReason!.ShouldContain("meta.txt");
        requests.Count.ShouldBe(0);
    }

    /// <summary>
    /// ★★ 重定向不跟：原始 URL 过了 <c>EgressGuard</c>（公网地址），服务端回一个 <c>302</c> 指向云元数据 / 内网地址。
    /// 自动跟随会让第二跳绕过全部检查，把元数据端点的回应装进信里寄出去。发送器要的是那个
    /// <b>禁自动重定向</b>的具名客户端，并把任何 3xx 当作取件失败，一个后续请求都不发。
    /// </summary>
    [Fact]
    public async Task UrlAttachment_RedirectedToThePrivateNetwork_FailsWithoutFollowing()
    {
        var requests = UseHttpHandlerThatRedirects("http://169.254.169.254/latest/meta-data/iam/security-credentials/");

        var result = await SendWithAttachmentAsync(new EmailAttachment
        {
            FileName = "invoice.pdf",
            FilePath = "https://93.184.216.34/invoice.pdf",
            ContentType = "application/pdf",
        });

        result.Success.ShouldBeFalse();
        result.FailureReason!.ShouldContain("invoice.pdf");
        result.FailureReason!.ShouldContain("redirect");
        requests.Count.ShouldBe(1);
        // 具名客户端才带着「不跟重定向」的 primary handler；要的是默认客户端就等于没注册那一行。
        _httpClientFactory.Verify(f => f.CreateClient(NotificationHttpClientNames.Attachments), Times.Once());
        _httpClientFactory.Verify(f => f.CreateClient(It.Is<string>(n => n != NotificationHttpClientNames.Attachments)), Times.Never());
    }

    /// <summary>远程附件超过上限就失败，而不是把整个响应体读进内存再装进信里。</summary>
    [Fact]
    public async Task UrlAttachment_ExceedingMaxBytes_FailsTheSend()
    {
        _options.Attachments.MaxAttachmentBytes = 16;
        UseHttpHandlerThatCountsRequests(body: new byte[64]);

        var result = await SendWithAttachmentAsync(new EmailAttachment
        {
            FileName = "huge.bin",
            FilePath = "https://93.184.216.34/huge.bin",
            ContentType = "application/octet-stream",
        });

        result.Success.ShouldBeFalse();
        result.FailureReason!.ShouldContain("huge.bin");
        result.FailureReason!.ShouldContain("MaxAttachmentBytes");
    }

    [Fact]
    public async Task LocalAttachment_ExceedingMaxBytes_FailsTheSend()
    {
        _options.Attachments.MaxAttachmentBytes = 16;
        var path = Path.Combine(Path.GetTempPath(), "tnzi-attachment-" + Guid.NewGuid().ToString("N") + ".bin");
        await File.WriteAllBytesAsync(path, new byte[64]);
        try
        {
            var result = await SendWithAttachmentAsync(new EmailAttachment
            {
                FileName = "huge.bin",
                FilePath = path,
                ContentType = "application/octet-stream",
            });

            result.Success.ShouldBeFalse();
            result.FailureReason!.ShouldContain("huge.bin");
            result.FailureReason!.ShouldContain("MaxAttachmentBytes");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>对照：在上限之内的远程附件通过取件那一关（失败只能来自其后的 SMTP）。</summary>
    [Fact]
    public async Task UrlAttachment_WithinMaxBytes_GetsPastMaterialisation()
    {
        _options.Attachments.MaxAttachmentBytes = 1024;
        UseHttpHandlerThatCountsRequests(body: [0x25, 0x50, 0x44, 0x46, 0x2D]);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var result = await NewEmailSender().SendAsync(new EmailMessage
        {
            To = [new EmailAddress("someone@example.com")],
            Subject = "Statement",
            Body = "See attached.",
            Attachments = [new EmailAttachment { FileName = "invoice.pdf", FilePath = "https://93.184.216.34/invoice.pdf", ContentType = "application/pdf" }],
        }, cancelled.Token);

        result.Success.ShouldBeFalse();
        result.FailureReason!.ShouldNotContain("invoice.pdf");
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
        => new(_options.MailSender!, _httpClientFactory.Object, new Mock<ILogger<MailKitEmailSender>>().Object, _options.Attachments);

    /// <summary>回一个固定响应体并数请求次数的 HTTP 替身。</summary>
    private List<Uri> UseHttpHandlerThatCountsRequests(byte[] body)
    {
        var requests = new List<Uri>();
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns((HttpRequestMessage request, CancellationToken _) =>
            {
                requests.Add(request.RequestUri!);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
            });
        _httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler.Object));
        return requests;
    }

    /// <summary>回一个 302（带 Location）并数请求次数的 HTTP 替身。</summary>
    private List<Uri> UseHttpHandlerThatRedirects(string location)
    {
        var requests = new List<Uri>();
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns((HttpRequestMessage request, CancellationToken _) =>
            {
                requests.Add(request.RequestUri!);
                var response = new HttpResponseMessage(HttpStatusCode.Found) { Content = new ByteArrayContent([]) };
                response.Headers.Location = new Uri(location);
                return Task.FromResult(response);
            });
        _httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler.Object));
        return requests;
    }

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
