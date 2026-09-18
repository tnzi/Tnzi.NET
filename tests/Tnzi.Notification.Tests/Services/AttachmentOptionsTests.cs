using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using Tnzi.Modules;
using Tnzi.Notification.Services.Internal;

namespace Tnzi.Notification.Tests.Services;

/// <summary>
/// <c>Notification:Attachments</c>：配置校验、模块接线，以及 <see cref="AttachmentSourcePolicy"/> 本身的边界。
/// </summary>
/// <remarks>
/// ★ 接线那条守的是「配了根目录但内置发送器没拿到」：发送器的那个参数是可选的（缺省 = 一个根目录都不允许），
/// 模块工厂漏传的症状是"配了 AllowedLocalRoots 而每个本地附件照样失败"，与没配一模一样。
/// </remarks>
public class AttachmentOptionsTests
{
    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(NotificationOptions options)
        => new NotificationOptionsValidator().Validate(name: null, options);

    // ── 校验 ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ARelativeAllowedRoot_FailsValidation()
    {
        var options = new NotificationOptions { Attachments = { AllowedLocalRoots = [Path.Combine("var", "attachments")] } };

        var result = Validate(options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Attachments.AllowedLocalRoots");
    }

    [Fact]
    public void ABlankAllowedRoot_FailsValidation()
    {
        var options = new NotificationOptions { Attachments = { AllowedLocalRoots = [" "] } };

        Validate(options).Failed.ShouldBeTrue();
    }

    [Fact]
    public void ANonPositiveMaxAttachmentBytes_FailsValidation()
    {
        var options = new NotificationOptions { Attachments = { MaxAttachmentBytes = 0 } };

        var result = Validate(options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Attachments.MaxAttachmentBytes");
    }

    [Fact]
    public void TheDefaults_PassValidation()
    {
        Validate(new NotificationOptions()).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void AnAbsoluteAllowedRoot_PassesValidation()
    {
        var options = new NotificationOptions { Attachments = { AllowedLocalRoots = [Path.GetTempPath()] } };

        Validate(options).Succeeded.ShouldBeTrue();
    }

    // ── 接线 ─────────────────────────────────────────────────────────────────

    /// <summary>模块工厂造出来的默认邮件发送器要拿到配置里的根目录。</summary>
    [Fact]
    public async Task TheModuleBuiltMailSender_HonoursTheConfiguredLocalRoots()
    {
        var root = Path.Combine(Path.GetTempPath(), "tnzi-attachment-wiring", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "invoice.pdf");
        await File.WriteAllBytesAsync(path, [0x25, 0x50, 0x44, 0x46, 0x2D]);
        try
        {
            using var provider = BuildProvider(new Dictionary<string, string?>
            {
                ["Notification:MailSender:SmtpServer"] = "smtp.example.com",
                ["Notification:MailSender:FromEmail"] = "noreply@example.com",
                ["Notification:MailSender:EnableSsl"] = "false",
                ["Notification:Attachments:AllowedLocalRoots:0"] = root,
            });
            using var scope = provider.CreateScope();
            var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>().ShouldBeOfType<MailKitEmailSender>();

            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync();
            var result = await sender.SendAsync(new EmailMessage
            {
                To = [new EmailAddress("someone@example.com")],
                Subject = "Statement",
                Body = "See attached.",
                Attachments = [new EmailAttachment { FileName = "invoice.pdf", FilePath = path, ContentType = "application/pdf" }],
            }, cancelled.Token);

            // 失败是必然的（令牌已取消，连不上 SMTP）；要断言的是它不是在附件来源那一关失败的。
            result.FailureReason!.ShouldNotContain("invoice.pdf");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// ★★ 取远程附件的具名 HttpClient 不跟随重定向。<c>EgressGuard</c> 只看得见原始 URL：
    /// 一个公网地址回 <c>302 Location: http://169.254.169.254/…</c>，自动跟随的客户端会把第二跳直接发出去，
    /// 元数据端点的回应装进信里寄给收件人，而检查全过。这一行只在模块注册里，接错了发送器照常工作。
    /// </summary>
    [Fact]
    public void TheAttachmentHttpClient_DoesNotFollowRedirects()
    {
        using var provider = BuildProvider([]);

        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(NotificationHttpClientNames.Attachments);
        var primary = handler;
        while (primary is DelegatingHandler { InnerHandler: { } inner })
            primary = inner;

        var allowAutoRedirect = primary switch
        {
            SocketsHttpHandler sockets => sockets.AllowAutoRedirect,
            HttpClientHandler classic => classic.AllowAutoRedirect,
            _ => throw new Xunit.Sdk.XunitException($"Unexpected primary handler {primary.GetType().Name}."),
        };

        allowAutoRedirect.ShouldBeFalse();
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var context = new ServiceConfigurationContext(services, configuration);

        var module = new NotificationModule();
        module.PreConfigureServicesAsync(context).GetAwaiter().GetResult();
        module.ConfigureServicesAsync(context).GetAwaiter().GetResult();

        return services.BuildServiceProvider();
    }

    // ── 策略边界 ─────────────────────────────────────────────────────────────

    /// <summary>★ 符号链接逃逸：链接本身在根目录之下，指向的目标不在。</summary>
    [Fact]
    public async Task ASymbolicLinkPointingOutsideTheRoot_IsRejected()
    {
        var root = Path.Combine(Path.GetTempPath(), "tnzi-attachment-link", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var outside = Path.Combine(Path.GetTempPath(), "tnzi-attachment-target-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(outside, "secret");
        var link = Path.Combine(root, "innocent.txt");
        try
        {
            try
            {
                File.CreateSymbolicLink(link, outside);
            }
            catch (IOException)
            {
                // 没有创建符号链接的权限（Windows 未开发者模式）：这条用例无从验证，但不能假绿。
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }

            var options = new AttachmentOptions { AllowedLocalRoots = [root] };

            var violation = await AttachmentSourcePolicy.DescribeViolationAsync(link, options, CancellationToken.None);

            violation.ShouldNotBeNull();
            violation.ShouldContain("outside the allowed");
        }
        finally
        {
            try { File.Delete(link); } catch { /* best effort */ }
            try { File.Delete(outside); } catch { /* best effort */ }
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// ★ 目录链接逃逸：链接的是<b>中间的目录</b>而不是叶子文件。<c>root/etc -> /etc</c> 之后，
    /// <c>root/etc/passwd</c> 的前缀检查过、叶子本身又不是链接 —— 只看叶子的检查一处都拦不住。
    /// 每一级目录都要解析到最终目标。
    /// </summary>
    [Fact]
    public async Task ADirectoryLinkPointingOutsideTheRoot_IsRejected()
    {
        var root = Path.Combine(Path.GetTempPath(), "tnzi-attachment-dirlink", Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(Path.GetTempPath(), "tnzi-attachment-dirlink-target", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "passwd"), "secret");
        var link = Path.Combine(root, "etc");
        try
        {
            if (!TryCreateDirectoryLink(link, outside))
                return;

            var options = new AttachmentOptions { AllowedLocalRoots = [root] };

            var violation = await AttachmentSourcePolicy.DescribeViolationAsync(Path.Combine(link, "passwd"), options, CancellationToken.None);

            violation.ShouldNotBeNull();
            violation.ShouldContain("outside the allowed");
        }
        finally
        {
            try { Directory.Delete(link); } catch { /* best effort */ }
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
            try { Directory.Delete(outside, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>对照：目录链接指向<b>另一个允许的根</b>之下时放行 —— 解析链接不能把合法的部署树（<c>current -> releases/N</c>）误拦。</summary>
    [Fact]
    public async Task ADirectoryLinkPointingIntoAnotherAllowedRoot_IsAccepted()
    {
        var root = Path.Combine(Path.GetTempPath(), "tnzi-attachment-dirlink", Guid.NewGuid().ToString("N"));
        var releases = Path.Combine(Path.GetTempPath(), "tnzi-attachment-dirlink-releases", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(releases);
        await File.WriteAllTextAsync(Path.Combine(releases, "invoice.pdf"), "%PDF-");
        var link = Path.Combine(root, "current");
        try
        {
            if (!TryCreateDirectoryLink(link, releases))
                return;

            var options = new AttachmentOptions { AllowedLocalRoots = [root, releases] };

            var violation = await AttachmentSourcePolicy.DescribeViolationAsync(Path.Combine(link, "invoice.pdf"), options, CancellationToken.None);

            violation.ShouldBeNull();
        }
        finally
        {
            try { Directory.Delete(link); } catch { /* best effort */ }
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
            try { Directory.Delete(releases, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// 建目录链接：符号链接优先；Windows 没有开发者模式时退回 junction（不要权限，.NET 同样把它报成链接）。
    /// 两样都建不了就返回 false，用例无从验证但不能假绿。
    /// </summary>
    private static bool TryCreateDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!OperatingSystem.IsWindows())
                return false;
        }

        using var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });
        mklink!.WaitForExit();
        return mklink.ExitCode == 0 && new DirectoryInfo(link).LinkTarget != null;
    }

    /// <summary>
    /// 允许的根是文件系统根（<c>C:\</c> / <c>/</c>）时也要匹配得上：<c>Path.TrimEndingDirectorySeparator</c>
    /// 不动根路径，直接再补一个分隔符会得到 <c>C:\</c>，什么路径都不以它开头 ——
    /// 运营配了整个盘却每个附件都报「outside the allowed local roots」。
    /// </summary>
    [Fact]
    public async Task AFilesystemRootAsAllowedRoot_AllowsEverythingBeneathIt()
    {
        var filesystemRoot = Path.GetPathRoot(Path.GetTempPath())!;
        var options = new AttachmentOptions { AllowedLocalRoots = [filesystemRoot] };

        var violation = await AttachmentSourcePolicy.DescribeViolationAsync(Path.Combine(Path.GetTempPath(), "tnzi-attachment-root", "a.pdf"), options, CancellationToken.None);

        violation.ShouldBeNull();
    }

    /// <summary>根目录带不带尾部分隔符都一样。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ARootWithOrWithoutATrailingSeparator_AllowsFilesBeneathIt(bool trailingSeparator)
    {
        var root = Path.Combine(Path.GetTempPath(), "tnzi-attachment-sep", Guid.NewGuid().ToString("N"));
        var configured = trailingSeparator ? root + Path.DirectorySeparatorChar : root;
        var options = new AttachmentOptions { AllowedLocalRoots = [configured] };

        var violation = await AttachmentSourcePolicy.DescribeViolationAsync(Path.Combine(root, "sub", "a.pdf"), options, CancellationToken.None);

        violation.ShouldBeNull();
    }

    /// <summary>空路径不归本策略管（FileId-only 附件、或什么都没给，由发送器在取件时判定）。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task AnEmptyPath_IsNotAViolation(string? filePath)
    {
        var violation = await AttachmentSourcePolicy.DescribeViolationAsync(filePath, new AttachmentOptions(), CancellationToken.None);

        violation.ShouldBeNull();
    }
}
