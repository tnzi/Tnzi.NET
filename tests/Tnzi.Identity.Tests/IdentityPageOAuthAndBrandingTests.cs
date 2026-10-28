namespace Tnzi.Identity.Tests;

/// <summary>
/// 后端兜底页面（OAuth 回调、重置密码、邮箱确认）的输出卫生：不往控制台写令牌、不把异常细节摆给用户、
/// 文案是英文、抬头是部署方配置的站点名、重置表单提交回它自己的地址。
/// </summary>
public class IdentityPageOAuthAndBrandingTests
{
    private static IdentityPageService CreateService(IDictionary<string, string?>? settings = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings ?? new Dictionary<string, string?>())
            .Build();

        return new IdentityPageService(services.BuildServiceProvider(), configuration);
    }

    private static OAuthCallbackResultDto SignedIn() => new()
    {
        Success = true,
        AccessToken = "access-token-marker",
        RefreshToken = "refresh-token-marker",
        ExpiresAt = DateTime.UtcNow.AddMinutes(30),
    };

    /// <summary>
    /// ★★ 载荷里就是令牌：写进控制台等于让它随排障截图、共享的控制台日志、录屏一起流出去。
    /// 页面脚本里一个 console 调用都不留，包括失败分支。
    /// </summary>
    [Fact]
    public void The_OAuth_callback_page_never_writes_to_the_console()
    {
        var html = CreateService().GenerateOAuthCallbackHtml(SignedIn());

        html.ShouldNotContain("console.");
        // 令牌本身仍要交给打开它的窗口：页面没坏，只是不再打印它。
        html.ShouldContain("access-token-marker");
    }

    /// <summary>脚本出错时不把异常对象渲染给用户：它的消息里可能带着这次的令牌或返回地址。</summary>
    [Fact]
    public void The_OAuth_callback_page_does_not_render_script_errors()
    {
        var html = CreateService().GenerateOAuthCallbackHtml(SignedIn());

        html.ShouldNotContain("e.toString()");
        html.ShouldNotContain("e.message");
        html.ShouldNotContain("e.stack");
    }

    [Fact]
    public void The_OAuth_callback_page_speaks_English()
    {
        var html = CreateService().GenerateOAuthCallbackHtml(SignedIn());

        html.ShouldContain("Signing you in...");
        ContainsCjk(html).ShouldBeFalse();
    }

    [Fact]
    public void The_OAuth_error_and_email_pages_speak_English()
    {
        var service = CreateService();

        ContainsCjk(service.GenerateOAuthErrorHtml("Sign-in could not be completed.")).ShouldBeFalse();
        ContainsCjk(service.GenerateEmailConfirmationResultHtml(true, "Confirmed.")).ShouldBeFalse();
        ContainsCjk(service.GenerateEmailConfirmationResultHtml(false, "Not confirmed.")).ShouldBeFalse();
        ContainsCjk(service.GenerateResetPasswordFormHtml("a@example.com", "token")).ShouldBeFalse();
        ContainsCjk(service.GenerateResetPasswordResultHtml(true, "Done.")).ShouldBeFalse();
    }

    /// <summary>
    /// 表单页由 GET 出、新密码由同一路由的 POST 收。此前提交地址写死 <c>/auth/reset-password</c>，
    /// 而 API 路由带全局前缀（默认 <c>/api</c>），兜底页的每一次提交都是 404。
    /// </summary>
    [Fact]
    public void The_reset_form_posts_back_to_its_own_address()
    {
        var html = CreateService().GenerateResetPasswordFormHtml("a@example.com", "token");

        html.ShouldContain("fetch(window.location.pathname");
        html.ShouldNotContain("fetch('/auth/reset-password'");
    }

    /// <summary>抬头与邀请信、重置信同一个站点名来源，不再显示框架名。</summary>
    [Fact]
    public void The_reset_pages_show_the_configured_site_name()
    {
        var service = CreateService(new Dictionary<string, string?> { ["System:SiteName"] = "Acme Portal" });

        var form = service.GenerateResetPasswordFormHtml("a@example.com", "token");
        var result = service.GenerateResetPasswordResultHtml(true, "Done.");

        form.ShouldContain("Reset password - Acme Portal");
        result.ShouldContain("Acme Portal");
        form.ShouldNotContain("Tnzi.NET");
        result.ShouldNotContain("Tnzi.NET");
    }

    private static bool ContainsCjk(string text) => text.Any(c => c >= '一' && c <= '鿿');
}
