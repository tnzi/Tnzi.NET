namespace Tnzi.Identity.Tests;

/// <summary>
/// 单元测试用：多租户未开启，范围不裁剪。真实的 <see cref="UserTenantScopeProvider"/>，
/// 不是 mock —— 不裁剪时它一个字节都不查库，仓储替身永远不会被碰到。
/// </summary>
internal static class UnscopedUserTenantScope
{
    public static IUserTenantScopeProvider Create() =>
        new UserTenantScopeProvider(new Mock<IRepository<User, Guid>>().Object);
}
