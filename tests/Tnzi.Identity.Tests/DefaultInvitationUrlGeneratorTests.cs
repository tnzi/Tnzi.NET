using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.Tests;

/// <summary>
/// 默认邀请链接生成器读前端 origin 的键必须与框架其它地方同一个。
/// </summary>
/// <remarks>
/// ★ 参考消费方只配了 <c>System:FrontendUrl</c>（Hosting 邮件处理器读的那个键），而这里此前只读
/// <c>App:FrontendUrl</c>：密码重置信正常，邀请信的接受链接却是相对路径 —— 处理器失败关闭拒发，
/// 管理端仍报「已发出」。现在两边都经 <c>FrontendUrlResolver</c>：主键 <c>System:FrontendUrl</c>，旧键回退。
/// <para>
/// ★ 默认路径是 <c>/admin/accept-invitation</c>：框架自带的接受页在 <c>@tnzi/ui-admin</c> 里，
/// 而 ui-admin 默认把所有路由挂在 <c>basePath</c> <c>/admin</c> 下。此前默认链接是 <c>{origin}/accept-invitation</c>，
/// 按框架自己的默认配置装出来的管理台根本没有这个地址，被邀请人点开就是 404，而发信一切正常。
/// 前端那一半由 <c>accept-invitation-route.test.ts</c> 按默认 <c>basePath</c> 装配后的真实路径断言。
/// </para>
/// </remarks>
public class DefaultInvitationUrlGeneratorTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] pairs)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.ToDictionary(p => p.Key, p => p.Value))
            .Build();

    private static DefaultInvitationUrlGenerator Generator(IConfiguration? configuration, string? template = null)
    {
        var options = new IdentityOptions();
        options.Invitation.AcceptUrlTemplate = template;
        var monitor = new Mock<IOptionsMonitor<IdentityOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(options);
        return new DefaultInvitationUrlGenerator(monitor.Object, configuration);
    }

    private static readonly User Alice = new() { Id = Guid.NewGuid(), UserName = "alice" };

    [Fact]
    public void WithOnlySystemFrontendUrl_TheAcceptLinkIsAbsolute()
    {
        var url = Generator(Config(("System:FrontendUrl", "https://app.example/"))).GenerateUrl(Alice, "tok en");

        Assert.Equal("https://app.example/admin/accept-invitation?token=tok%20en", url);
    }

    [Fact]
    public void WithOnlyTheLegacyAppFrontendUrl_TheAcceptLinkIsStillAbsolute()
    {
        var url = Generator(Config(("App:FrontendUrl", "https://legacy.example"))).GenerateUrl(Alice, "abc");

        Assert.Equal("https://legacy.example/admin/accept-invitation?token=abc", url);
    }

    [Fact]
    public void TemplateWins_OverEitherKey()
    {
        var url = Generator(
            Config(("System:FrontendUrl", "https://app.example")),
            template: "https://mobile.example/invite/{token}").GenerateUrl(Alice, "abc");

        Assert.Equal("https://mobile.example/invite/abc", url);
    }

    [Fact]
    public void WithNeitherKey_TheLinkIsRelative()
    {
        // 相对路径只到得了 API 响应里的 acceptUrl；Hosting 的处理器对它拒发。
        Assert.Equal("/admin/accept-invitation?token=abc", Generator(Config()).GenerateUrl(Alice, "abc"));
        Assert.Equal("/admin/accept-invitation?token=abc", Generator(null).GenerateUrl(Alice, "abc"));
    }
}
