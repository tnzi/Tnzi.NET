using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Tnzi.Identity.Controllers;

namespace Tnzi.Identity.Tests;

/// <summary>
/// 被邀请人这一侧的两个匿名端点：邀请令牌只能走请求体，不能进 URL。
/// </summary>
/// <remarks>
/// ★★ 请求路径会被原样写进请求日志（框架的请求日志默认开启，记录 <c>RequestPath</c>）与访问日志表。
/// 预览不消费令牌，接受在「还差几步」时也不消费：能读日志的人拿着路径里的令牌抢先接受邀请，
/// 设上自己的密码，就拿到了带着预设角色的新账号。
/// </remarks>
public class DefaultInvitationControllerTests
{
    [Fact]
    public void NoInvitationEndpoint_CarriesTheTokenInItsRoute()
    {
        var templates = typeof(DefaultInvitationController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>())
            .Select(a => a.Template ?? string.Empty)
            .ToList();

        Assert.NotEmpty(templates);
        Assert.DoesNotContain(templates, t => t.Contains("token", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Preview_IsAPostThatReadsTheTokenFromTheBody()
    {
        var preview = typeof(DefaultInvitationController).GetMethod(nameof(DefaultInvitationController.Preview))!;

        Assert.NotNull(preview.GetCustomAttribute<HttpPostAttribute>());
        Assert.Null(preview.GetCustomAttribute<HttpGetAttribute>());
        var parameter = Assert.Single(preview.GetParameters());
        Assert.NotNull(parameter.GetCustomAttribute<FromBodyAttribute>());
    }

    [Fact]
    public async Task Preview_HandsTheBodyTokenToTheService()
    {
        var service = new Mock<IInvitationService>();
        service.Setup(s => s.PreviewAsync("the-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<InvitationPreviewDto>.Success(new InvitationPreviewDto { UserName = "newhire" }));
        var controller = new DefaultInvitationController(service.Object);

        var result = await controller.Preview(new PreviewInvitationDto { Token = "the-token" });

        Assert.True(result.Succeeded);
        Assert.Equal("newhire", result.Data!.UserName);
    }
}
