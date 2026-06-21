using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Tnzi.Authorization;
using Xunit;

namespace Tnzi.Authorization.Tests;

/// <summary>
/// [Authorize(Roles="...")] 经本 handler 后大小写不敏感，且只放宽不收紧。
/// </summary>
public class CaseInsensitiveRolesAuthorizationHandlerTests
{
    private static ClaimsPrincipal UserWithRole(string role) =>
        new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role) }, authenticationType: "test"));

    private static async Task<bool> EvaluateAsync(string[] allowedRoles, ClaimsPrincipal user)
    {
        var requirement = new RolesAuthorizationRequirement(allowedRoles);
        var context = new AuthorizationHandlerContext(new[] { requirement }, user, resource: null);
        await new CaseInsensitiveRolesAuthorizationHandler().HandleAsync(context);
        return context.HasSucceeded;
    }

    [Fact]
    public async Task Succeeds_WhenRoleMatchesIgnoringCase()
    {
        Assert.True(await EvaluateAsync(new[] { "Admin" }, UserWithRole("admin")));
    }

    [Fact]
    public async Task Succeeds_WhenAnyAllowedRoleMatches()
    {
        Assert.True(await EvaluateAsync(new[] { "Editor", "ADMIN" }, UserWithRole("admin")));
    }

    [Fact]
    public async Task DoesNotSucceed_WhenNoRoleMatches()
    {
        // 只放宽不收紧：不匹配时本 handler 不 Succeed（也绝不 Fail）。
        Assert.False(await EvaluateAsync(new[] { "Admin" }, UserWithRole("guest")));
    }
}
