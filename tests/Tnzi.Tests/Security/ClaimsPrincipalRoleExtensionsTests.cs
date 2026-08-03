using System.Security.Claims;
using Tnzi.Security.Claims;

namespace Tnzi.Tests.Security;

/// <summary>
/// 框架角色判断统一为大小写不敏感（IsInRoleIgnoreCase），区别于 BCL 大小写敏感的 IsInRole。
/// </summary>
public class ClaimsPrincipalRoleExtensionsTests
{
    private static ClaimsPrincipal UserWithRoles(params string[] roles)
    {
        var claims = roles.Select(r => new Claim(ClaimTypes.Role, r));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"));
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("ADMIN")]
    [InlineData("Admin")]
    public void IsInRoleIgnoreCase_MatchesRegardlessOfCase(string query)
    {
        var user = UserWithRoles("Admin");

        Assert.True(user.IsInRoleIgnoreCase(query));
    }

    [Fact]
    public void IsInRoleIgnoreCase_ReturnsFalse_ForMissingRole()
    {
        var user = UserWithRoles("Admin");

        Assert.False(user.IsInRoleIgnoreCase("user"));
    }

    [Fact]
    public void IsInRoleIgnoreCase_ReturnsFalse_ForEmptyRole()
    {
        var user = UserWithRoles("Admin");

        Assert.False(user.IsInRoleIgnoreCase(""));
    }

    [Fact]
    public void BclIsInRole_IsCaseSensitive_DocumentingTheDifference()
    {
        var user = UserWithRoles("Admin");

        // BCL 对角色值大小写敏感 —— 这正是我们用扩展统一掉的分歧。
        Assert.False(user.IsInRole("admin"));
        Assert.True(user.IsInRole("Admin"));
    }
}
