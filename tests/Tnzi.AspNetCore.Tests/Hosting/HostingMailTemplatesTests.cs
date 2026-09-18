using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Tnzi.Template.Options;
using Tnzi.Template.Services;
using Tnzi.TestBase;

namespace Tnzi.AspNetCore.Tests.Hosting;

/// <summary>
/// 内置邮件处理器引用的模板必须真的存在，并且能被真实的 Razor 引擎渲染出来。
///
/// 守的是一条只有收件人才会发现的线：处理器 `TemplateName = "X"` 而仓库里没有 X.cshtml 时，
/// 模板存储答 404、`NotificationService` 记一条 Warning 后「回落到原始内容」——
/// 而处理器压根没给原始内容，于是一封空主题空正文的邮件被成功发出，处理器记「sent」。
/// 2026-09-01 加进来的 `UserInvitedEventHandler` 正是这样：它引用的 `UserInvited` 模板
/// 从没被提交过，邀请链接里的令牌从未送达任何人。
/// </summary>
public class HostingMailTemplatesTests
{
    private static readonly string HandlersDir =
        Path.Combine(RepoRoot.Locate(), "src", "Tnzi.Hosting", "Events", "Handlers");

    private static readonly string TemplatesDir =
        Path.Combine(RepoRoot.Locate(), "src", "Tnzi.Hosting", "Templates", "Notification");

    /// <summary>
    /// 从处理器源码里抽出每个 <c>new CreateNotificationRequest { ... }</c> 的 (渠道, 模板名)。
    /// 按 <c>new CreateNotificationRequest</c> 切块，块内取 <c>NotificationType.X</c> 与
    /// <c>TemplateName = "Y"</c>；同一个处理器可以有多块（2FA 的邮件与短信各一块）。
    /// </summary>
    private static IEnumerable<(string Handler, string Channel, string Template)> TemplateReferences()
    {
        foreach (var file in Directory.GetFiles(HandlersDir, "*.cs"))
        {
            var text = File.ReadAllText(file);
            var chunks = text.Split("new CreateNotificationRequest", StringSplitOptions.None).Skip(1);
            foreach (var chunk in chunks)
            {
                var channel = Regex.Match(chunk, @"NotificationType\.(\w+)");
                var template = Regex.Match(chunk, @"TemplateName\s*=\s*""(\w+)""");
                if (channel.Success && template.Success)
                {
                    yield return (Path.GetFileName(file), channel.Groups[1].Value, template.Groups[1].Value);
                }
            }
        }
    }

    [Fact]
    public void TheScanSeesEveryHandlerAndEveryChannel()
    {
        // 防锈：扫描器本身要能看见四个处理器与两条渠道，否则下面那条会因为「什么都没扫到」而绿。
        var refs = TemplateReferences().ToList();

        Assert.Equal(4, refs.Select(r => r.Handler).Distinct().Count());
        Assert.Contains(refs, r => r.Channel == "Email");
        Assert.Contains(refs, r => r.Channel == "Sms");
        Assert.Contains(refs, r => r.Handler == "UserInvitedEventHandler.cs" && r.Template == "UserInvited");
    }

    [Fact]
    public void EveryTemplateAHandlerReferences_ShipsInTheHostingPackage()
    {
        var missing = TemplateReferences()
            .Where(r => !File.Exists(Path.Combine(TemplatesDir, r.Channel, r.Template + ".cshtml")))
            .Select(r => $"{r.Handler} -> Templates/Notification/{r.Channel}/{r.Template}.cshtml")
            .ToList();

        Assert.True(missing.Count == 0,
            "These handlers reference a notification template that does not exist. Without it the message "
            + "is created with an empty subject and body and sent anyway:\n" + string.Join("\n", missing));
    }

    [Fact]
    public async Task TheInvitationTemplate_RendersTheAcceptLinkThroughTheRealRazorEngine()
    {
        // 文件存在只证明了一半；它还得能编译，并且把接受链接真的印进正文。
        // 模板经 csproj 的 Content 项随 Tnzi.Hosting 复制到本测试的输出目录，这里直接从那儿渲染。
        var root = Path.Combine(AppContext.BaseDirectory, "Templates");
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var engine = new RazorTemplateEngine(
            Microsoft.Extensions.Options.Options.Create(new TemplateOptions { TemplateRootPath = root, EnableCache = false }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RazorTemplateEngine>.Instance,
            cache);

        var model = new Dictionary<string, object>
        {
            ["UserName"] = "alice",
            ["AppName"] = "Acme",
            ["AcceptUrl"] = "https://app.example.com/invite/accept?token=abc123",
            ["ExpiresAt"] = DateTime.UtcNow.AddDays(7),
            ["ExpiresInDays"] = 7,
            ["IsResend"] = false,
        };

        var body = await engine.RenderFromFileAsync("Notification/Email/UserInvited", model, "Layouts/Email/_DefaultEmail");

        Assert.Contains("https://app.example.com/invite/accept?token=abc123", body);
        Assert.Contains("alice", body);
        Assert.Contains("7 days", body);
        Assert.DoesNotContain("Your invitation link, again", body);

        var resend = await engine.RenderFromFileAsync(
            "Notification/Email/UserInvited",
            new Dictionary<string, object>(model) { ["IsResend"] = true, ["ExpiresInDays"] = 1 });

        Assert.Contains("https://app.example.com/invite/accept?token=abc123", resend);
        Assert.Contains("Your invitation link, again", resend);
        Assert.Contains("1 day.", resend);
    }
}
