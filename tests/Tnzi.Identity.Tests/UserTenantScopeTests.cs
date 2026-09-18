using Tnzi.MultiTenancy;

namespace Tnzi.Identity.Tests;

/// <summary>
/// <see cref="UserTenantScope.Resolve"/> 的四条口径：多租户未开启 / 没有租户上下文 / 没有已认证主体 ⇒ 不裁剪；
/// 其余按租户裁剪且本人恒在范围内。
/// </summary>
public class UserTenantScopeTests
{
    private static ICurrentTenant Tenant(Guid? id)
    {
        var mock = new Mock<ICurrentTenant>();
        mock.Setup(t => t.Id).Returns(id);
        return mock.Object;
    }

    private static ICurrentUser Principal(bool authenticated, Guid? id = null, Guid? tenantId = null)
    {
        var mock = new Mock<ICurrentUser>();
        mock.Setup(u => u.IsAuthenticated).Returns(authenticated);
        mock.Setup(u => u.Id).Returns(id);
        mock.Setup(u => u.TenantId).Returns(tenantId);
        return mock.Object;
    }

    [Fact]
    public void Resolve_WhenMultiTenancyIsOff_IsUnrestricted()
    {
        var scope = UserTenantScope.Resolve(false, Tenant(Guid.NewGuid()), Principal(true, Guid.NewGuid()));

        Assert.True(scope.IsUnrestricted);
    }

    [Fact]
    public void Resolve_WithAnAuthenticatedPrincipalInATenant_RestrictsToThatTenant_AndSelf()
    {
        var tenantId = Guid.NewGuid();
        var self = Guid.NewGuid();

        var scope = UserTenantScope.Resolve(true, Tenant(tenantId), Principal(true, self));

        Assert.False(scope.IsUnrestricted);
        Assert.True(scope.Contains(Guid.NewGuid(), tenantId));
        Assert.True(scope.Contains(self, null));
        Assert.False(scope.Contains(Guid.NewGuid(), Guid.NewGuid()));
        Assert.False(scope.Contains(Guid.NewGuid(), null));
    }

    /// <summary>
    /// 登录链路（多端登录顶替、登出）与后台任务没有已认证主体，而租户上下文可能来自 header 或
    /// <c>DefaultTenantId</c>、与正在登录的人不同租户 —— 那时不裁剪，否则顶替静默失效。
    /// </summary>
    [Fact]
    public void Resolve_WithoutAnAuthenticatedPrincipal_IsUnrestricted_EvenInsideATenantContext()
    {
        var scope = UserTenantScope.Resolve(true, Tenant(Guid.NewGuid()), Principal(false));

        Assert.True(scope.IsUnrestricted);
    }

    [Fact]
    public void Resolve_WithNoCurrentUserAtAll_IsUnrestricted()
    {
        var scope = UserTenantScope.Resolve(true, Tenant(Guid.NewGuid()), null);

        Assert.True(scope.IsUnrestricted);
    }

    [Fact]
    public void Resolve_ForAGlobalAdminWithoutATenantContext_IsUnrestricted()
    {
        var scope = UserTenantScope.Resolve(true, Tenant(null), Principal(true, Guid.NewGuid()));

        Assert.True(scope.IsUnrestricted);
    }
}
