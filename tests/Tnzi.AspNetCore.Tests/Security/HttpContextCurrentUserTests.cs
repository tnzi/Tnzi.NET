using System.Security.Claims;
using Tnzi.Security.Claims;

namespace Tnzi.AspNetCore.Tests.Security;

/// <summary>
/// HttpContextCurrentUser 自定义 claim 访问测试（F2）。
/// </summary>
public class HttpContextCurrentUserTests
{
    private static HttpContextCurrentUser CreateUser(params Claim[] claims)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"))
        };
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns(httpContext);
        return new HttpContextCurrentUser(accessor.Object);
    }

    [Fact]
    public void FindClaim_ShouldReturnValue_WhenClaimExists()
    {
        var user = CreateUser(new Claim("ai_roles", "admin recruiter"));

        Assert.Equal("admin recruiter", user.FindClaim("ai_roles"));
    }

    [Fact]
    public void FindClaim_ShouldReturnNull_WhenClaimMissing()
    {
        var user = CreateUser();

        Assert.Null(user.FindClaim("ai_roles"));
    }

    [Fact]
    public void FindClaim_ShouldReturnNull_WhenHttpContextIsNull()
    {
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(x => x.HttpContext).Returns((HttpContext?)null);
        var user = new HttpContextCurrentUser(accessor.Object);

        Assert.Null(user.FindClaim("ai_roles"));
    }

    [Fact]
    public void FindClaims_ShouldReturnAllValues_WhenMultipleClaimsPresent()
    {
        var user = CreateUser(
            new Claim("scope", "read"),
            new Claim("scope", "write"));

        Assert.Equal(new[] { "read", "write" }, user.FindClaims("scope"));
    }

    [Fact]
    public void FindClaims_ShouldReturnEmpty_WhenClaimMissing()
    {
        var user = CreateUser();

        Assert.Empty(user.FindClaims("scope"));
    }

    [Fact]
    public void FindClaims_AsInterfaceDefault_ReturnsEmpty_ForImplementationWithoutClaims()
    {
        // 验证 StableApi 默认接口方法对不覆盖的实现返回安全默认值（零破坏）。
        ICurrentUser bare = new BareCurrentUser();

        Assert.Null(bare.FindClaim("anything"));
        Assert.Empty(bare.FindClaims("anything"));
    }

    private sealed class BareCurrentUser : ICurrentUser
    {
        public bool IsAuthenticated => false;
        public Guid? Id => null;
        public string? UserName => null;
        public Guid? TenantId => null;
        public string[] Roles => Array.Empty<string>();
        public bool IsInRole(string roleName) => false;
    }
}
