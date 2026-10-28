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
    /// 允许授权中间件缓存每个端点合成后的策略。
    /// </summary>
    /// <remarks>
    /// 基类只对它自己这个确切类型答 true，派生类不覆写就等于关掉缓存：每个请求都对端点上的每个
    /// <c>[ApiAuthorize]</c> 重新取一遍策略、重新合成。这里的策略只由名字决定（<c>Permission:</c> 前缀的按名字生成，
    /// 其余来自启动后不再变化的 <see cref="AuthorizationOptions"/>），缓存是安全的。
    /// 权限判定本身仍在每个请求里由处理器执行，缓存的只是「这个端点要哪些码」。
    /// </remarks>
    public override bool AllowsCachingPolicies => true;

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
