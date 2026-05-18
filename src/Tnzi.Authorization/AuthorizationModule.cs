namespace Tnzi.Authorization;

/// <summary>
/// 授权模块
/// 依赖 Identity 模块以获取用户角色信息
/// </summary>
[DependsOn(typeof(IdentityModule))]
public class AuthorizationModule : TnziApplicationModule
{
    /// <summary>
    /// 授权模块在 Identity 之后加载
    /// </summary>
    public override int LoadOrder => 10;
    
    /// <summary>
    /// 表名前缀
    /// </summary>
    public override string? TableNamePrefix => "Auth";

    /// <summary>
    /// 配置服务
    /// </summary>
    /// <param name="context">服务配置上下文</param>
    public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
    {
        var services = context.Services;

        // 注意：Authorization 模块实体已通过实体配置类自动注册到主 DbContext
        // 不再需要独立的 AuthorizationDbContext，实体配置会自动应用到主 DbContext

        // Options
        services.Configure<Tnzi.Authorization.Options.AuthorizationOptions>(
            context.Configuration.GetSection("Authorization"));

        // 注册授权服务（单一实现，多接口注册）
        services.AddScoped<FunctionAuthorizationService>();
        services.AddScoped<Tnzi.Authorization.Services.IFunctionAuthorizationService>(sp => sp.GetRequiredService<FunctionAuthorizationService>());
        services.AddScoped<IModuleManagementService>(sp => sp.GetRequiredService<FunctionAuthorizationService>());
        services.AddScoped<IRoleFunctionService>(sp => sp.GetRequiredService<FunctionAuthorizationService>());
        services.AddScoped<Tnzi.Security.Authorization.IFunctionAuthorizationService>(sp => sp.GetRequiredService<FunctionAuthorizationService>());
        services.AddScoped<IDataAuthService, DataAuthService>();
        services.AddSingleton<FunctionAuthCache>();

        // 注册权限管理器和权限检查器
        services.AddSingleton<IPermissionManager, PermissionManager>();
        services.AddScoped<Tnzi.Security.Authorization.IPermissionChecker, PermissionChecker>();

        // IUserRoleService 由 Identity 模块注册
        // 通过 [DependsOn] 确保 Identity 模块先加载

        // 注册授权处理器
        services.AddScoped<IAuthorizationHandler, FunctionAuthorizationHandler>();

        // 配置授权策略
        services.AddAuthorization(options =>
        {
            options.AddPolicy("FunctionAuthorization", policy =>
            {
                policy.Requirements.Add(new FunctionAuthorizationRequirement());
            });
        });

        // 注册自定义策略提供程序（必须在 AddAuthorization 之后）
        services.AddSingleton<IAuthorizationPolicyProvider, TnziAuthorizationPolicyProvider>();

        return Task.CompletedTask;
    }

    /// <summary>
    /// 应用初始化：预热权限管理器
    /// </summary>
    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        var permissionManager = context.ServiceProvider.GetRequiredService<IPermissionManager>();
        await permissionManager.RefreshAsync();
    }
}
