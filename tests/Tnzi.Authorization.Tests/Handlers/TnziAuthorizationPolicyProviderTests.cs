using AspNetAuthorizationOptions = Microsoft.AspNetCore.Authorization.AuthorizationOptions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.Authorization.Tests.Handlers;

/// <summary>
/// <see cref="TnziAuthorizationPolicyProvider"/>：按名字动态生成权限策略，且允许授权中间件缓存端点的合成策略。
/// </summary>
public class TnziAuthorizationPolicyProviderTests
{
    private static TnziAuthorizationPolicyProvider CreateProvider()
        => new(MsOptions.Create(new AspNetAuthorizationOptions()));

    /// <summary>
    /// 派生自默认提供者而不声明可缓存，授权中间件就关掉端点策略缓存，每个请求重新合成一遍策略。
    /// 这里的策略只由名字决定，可以缓存。
    /// </summary>
    [Fact]
    public void Combined_endpoint_policies_may_be_cached()
    {
        CreateProvider().AllowsCachingPolicies.ShouldBeTrue();
    }

    [Fact]
    public async Task A_permission_policy_carries_the_named_requirement()
    {
        var policy = await CreateProvider().GetPolicyAsync("Permission:catalog.product.view");

        policy.ShouldNotBeNull();
        policy!.Requirements.OfType<FunctionAuthorizationRequirement>().Single().PermissionName.ShouldBe("catalog.product.view");
    }

    [Fact]
    public async Task An_unknown_policy_name_yields_no_policy()
    {
        (await CreateProvider().GetPolicyAsync("SomethingElse")).ShouldBeNull();
    }
}
