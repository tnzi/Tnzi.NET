namespace Tnzi.Authorization.Security;

/// <summary>
/// Tnzi 授权策略提供程序
/// 支持动态创建基于权限名称的授权策略
/// </summary>
public class TnziAuthorizationPolicyProvider : DefaultAuthorizationPolicyProvider
{
    private const string PermissionPolicyPrefix = "Permission:";

    /// <summary>
    /// 初始化一个<see cref="TnziAuthorizationPolicyProvider"/>类型的新实例
    /// </summary>
    public TnziAuthorizationPolicyProvider(IOptions<AuthorizationOptions> options)
        : base(options)
    {
    }

    /// <summary>
    /// 获取授权策略
    /// </summary>
    /// <param name="policyName">策略名称</param>
    /// <returns>授权策略</returns>
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        // 先尝试从基类获取已注册的策略
        var policy = await base.GetPolicyAsync(policyName);
        if (policy != null)
        {
            return policy;
        }

        // 如果是权限策略（以 "Permission:" 开头），动态创建
        if (policyName.StartsWith(PermissionPolicyPrefix, StringComparison.Ordinal))
        {
            var permissionName = policyName[PermissionPolicyPrefix.Length..];
            var policyBuilder = new AuthorizationPolicyBuilder();
            policyBuilder.Requirements.Add(new FunctionAuthorizationRequirement(permissionName));
            return policyBuilder.Build();
        }

        return null;
    }
}
