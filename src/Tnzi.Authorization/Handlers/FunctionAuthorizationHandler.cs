namespace Tnzi.Authorization.Handlers;

/// <summary>
/// 功能授权处理器
/// </summary>
public class FunctionAuthorizationHandler : AuthorizationHandler<FunctionAuthorizationRequirement>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ICurrentUser _currentUser;

    /// <summary>
    /// 初始化一个<see cref="FunctionAuthorizationHandler"/>类型的新实例
    /// </summary>
    public FunctionAuthorizationHandler(IServiceProvider serviceProvider, ICurrentUser currentUser)
    {
        _serviceProvider = Check.NotNull(serviceProvider);
        _currentUser = Check.NotNull(currentUser);
    }

    /// <summary>
    /// 处理授权要求
    /// </summary>
    /// <param name="context">授权上下文</param>
    /// <param name="requirement">授权要求</param>
    /// <returns>授权结果</returns>
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        FunctionAuthorizationRequirement requirement)
    {
        var httpContext = _serviceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext;
        if (httpContext == null)
        {
            context.Fail();
            return;
        }

        var endpoint = httpContext.GetEndpoint();

        // 检查是否有 AllowAnonymous 特性
        var hasAllowAnonymous = endpoint?.Metadata
            .OfType<AllowAnonymousAttribute>()
            .Any() ?? false;
        if (hasAllowAnonymous)
        {
            context.Succeed(requirement);
            return;
        }

        // 检查用户是否已认证
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            context.Fail();
            return;
        }

        // 从 Requirement 获取权限名称（仅当显式通过 ApiAuthorize(PermissionName="xxx") 指定时才有值）
        string? permissionName = requirement.PermissionName;

        // 未显式指定权限时：仅验证登录，不校验具体权限
        if (string.IsNullOrEmpty(permissionName))
        {
            context.Succeed(requirement);
            return;
        }

        // 验证权限
        var userId = _currentUser.Id;
        if (userId == null)
        {
            context.Fail();
            return;
        }

        var authorizationService = _serviceProvider.GetRequiredService<IFunctionAuthorizationService>();
        bool hasPermission = await authorizationService.CheckPermissionAsync(userId.Value, permissionName);

        if (hasPermission)
        {
            context.Succeed(requirement);
        }
        else
        {
            context.Fail();
        }
    }
}

/// <summary>
/// 功能授权要求
/// </summary>
public class FunctionAuthorizationRequirement : IAuthorizationRequirement
{
    /// <summary>
    /// 获取或设置 权限名称
    /// </summary>
    public string? PermissionName { get; set; }

    /// <summary>
    /// 初始化一个<see cref="FunctionAuthorizationRequirement"/>类型的新实例
    /// </summary>
    public FunctionAuthorizationRequirement()
    {
    }

    /// <summary>
    /// 初始化一个<see cref="FunctionAuthorizationRequirement"/>类型的新实例
    /// </summary>
    /// <param name="permissionName">权限名称</param>
    public FunctionAuthorizationRequirement(string permissionName)
    {
        PermissionName = permissionName;
    }
}
