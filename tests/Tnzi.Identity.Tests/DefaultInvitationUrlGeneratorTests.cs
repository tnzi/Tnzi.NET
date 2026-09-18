using IdentityOptions = Tnzi.Identity.Options.IdentityOptions;

namespace Tnzi.Identity.Tests;

/// <summary>
/// 默认邀请链接生成器读前端 origin 的键必须与框架其它地方同一个。
/// </summary>
/// <remarks>
/// ★ 参考消费方只配了 <c>System:FrontendUrl</c>（Hosting 邮件处理器读的那个键），而这里此前只读
/// <c>App:FrontendUrl</c>：密码重置信正常，邀请信的接受链接却是相对路径 —— 处理器失败关闭拒发，
/// 管理端仍报「已发出」。现在两边都经 <c>FrontendUrlResolver</c>：主键 <c>System:FrontendUrl</c>，旧键回退。
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

        Assert.Equal("https://app.example/accept-invitation?token=tok%20en", url);
    }

    [Fact]
    public void WithOnlyTheLegacyAppFrontendUrl_TheAcceptLinkIsStillAbsolute()
    {
        var url = Generator(Config(("App:FrontendUrl", "https://legacy.example"))).GenerateUrl(Alice, "abc");

        Assert.Equal("https://legacy.example/accept-invitation?token=abc", url);
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
        Assert.Equal("/accept-invitation?token=abc", Generator(Config()).GenerateUrl(Alice, "abc"));
        Assert.Equal("/accept-invitation?token=abc", Generator(null).GenerateUrl(Alice, "abc"));
    }
}
