using TnziIdentityOptions = Tnzi.Identity.Options.IdentityOptions;
using AspNetIdentityOptions = Microsoft.AspNetCore.Identity.IdentityOptions;

namespace Tnzi.Identity.Tests;

/// <summary>
/// 后端兜底的重置密码页：页面上的有效期与最短长度提示取实际生效的值。
/// </summary>
/// <remarks>
/// 此前写死「此链接将在30分钟后过期」与「至少6位」：有效期是运行时设置，密码策略可以要求更长，
/// 页面说的与服务端判的是两回事。
/// </remarks>
public class IdentityPageResetFormTests
{
    private static IdentityPageService CreateService(int resetMinutes, int requiredLength)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var identityOptions = new TnziIdentityOptions();
        identityOptions.Recovery.ResetTokenExpirationMinutes = resetMinutes;
        var monitor = new Mock<IOptionsMonitor<TnziIdentityOptions>>();
        monitor.SetupGet(m => m.CurrentValue).Returns(identityOptions);

        var aspNet = new AspNetIdentityOptions();
        aspNet.Password.RequiredLength = requiredLength;

        return new IdentityPageService(
            services.BuildServiceProvider(),
            configuration: null,
            identityOptions: monitor.Object,
            aspNetIdentityOptions: Microsoft.Extensions.Options.Options.Create(aspNet));
    }

    [Fact]
    public void The_reset_page_states_the_configured_link_lifetime()
    {
        var html = CreateService(resetMinutes: 15, requiredLength: 6).GenerateResetPasswordFormHtml("a@example.com", "token");

        html.ShouldContain("This link expires 15 minutes after it was sent.");
        html.ShouldNotContain("30 minutes");
    }

    [Fact]
    public void The_reset_page_asks_for_the_enforced_minimum_length()
    {
        var html = CreateService(resetMinutes: 30, requiredLength: 10).GenerateResetPasswordFormHtml("a@example.com", "token");

        html.ShouldContain("minlength=\"10\"");
        html.ShouldContain("newPassword.length < 10");
        html.ShouldNotContain("minlength=\"6\"");
    }
}
