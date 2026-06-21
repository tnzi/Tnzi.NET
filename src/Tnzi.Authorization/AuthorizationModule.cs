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

        // Options + startup-time shape validation. Catches typos like
        // "SuperAdminRole" (singular) inside the JSON array and empty/
        // duplicate role names. The semantic check "does this role exist
        // in Identity?" runs later in OnApplicationInitializationAsync,
        // since that requires Identity's DbContext to be ready.
        services.AddTnziOptions<Tnzi.Authorization.Options.AuthorizationOptions,
            Tnzi.Authorization.Options.AuthorizationOptionsValidator>(context.Configuration);

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

        // Provider→DB seeder: walks every IPermissionDefinitionProvider on
        // startup and upserts the declared modules/functions into the DB
        // so admin UI can see them, mark them system-managed, and let
        // them be assigned to roles like any admin-created permission.
        // Without this seeder, provider-declared permissions are in-memory
        // only and can't FK to RoleFunction.
        services.AddScoped<PermissionDbSeeder>();

        // Framework built-in permission catalogue: declares the ~68 codes the
        // admin shell's routes reference (identity.*, system.*, ai.*, …) so a
        // super-admin's GetUserPermissionNamesAsync returns them and admins can
        // assign them to roles. Centralised here because framework modules don't
        // depend on Authorization — see FrameworkPermissions for the rationale.
        services.AddTransient<IPermissionDefinitionProvider, FrameworkPermissions>();

        // IUserRoleService 由 Identity 模块注册
        // 通过 [DependsOn] 确保 Identity 模块先加载

        // 注册授权处理器
        services.AddScoped<IAuthorizationHandler, FunctionAuthorizationHandler>();
        // 让 [Authorize(Roles=...)] 与框架其余角色判断一样大小写不敏感（补充 handler，只放宽不收紧）。
        services.AddSingleton<IAuthorizationHandler, CaseInsensitiveRolesAuthorizationHandler>();

        // Event handlers — cross-module signal: when Identity flips a user's
        // role membership, we drop that user's cached permission set so the
        // change takes effect immediately (without waiting for the cache
        // TTL to expire). Framework rule: framework assemblies MUST manually
        // register event handlers — no auto-discovery.
        services.AddEventHandler<UserRolesChangedEvent,
            Tnzi.Authorization.Events.Handlers.UserRolesChangedEventHandler>();

        // Watch for role renames that would invalidate SuperAdminRoles by
        // name — purely diagnostic, but the alternative is "admin gets
        // locked out three days later and nobody knows why".
        services.AddEventHandler<RoleUpdatedEvent,
            Tnzi.Authorization.Events.Handlers.RoleRenamedSuperAdminWatcherHandler>();

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
    /// 应用初始化：预热权限管理器 + super-admin 配置校验
    /// </summary>
    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        // Seed BEFORE PermissionManager.RefreshAsync so the manager's
        // snapshot picks up provider-declared permissions on the SAME
        // run they're introduced — without seeding first they'd be
        // missing until the next restart (manager loads from DB).
        await using (var seedScope = context.ServiceProvider.CreateAsyncScope())
        {
            var providers = seedScope.ServiceProvider.GetServices<IPermissionDefinitionProvider>().ToList();
            if (providers.Count > 0)
            {
                try
                {
                    var seeder = seedScope.ServiceProvider.GetRequiredService<PermissionDbSeeder>();
                    await seeder.SeedAsync(providers);
                }
                catch (Exception ex)
                {
                    // Auxiliary step — startup must not block on it. The
                    // worst case is provider permissions stay invisible to
                    // admin UI; the runtime check still works because
                    // PermissionManager has the in-memory snapshot.
                    var seedLogger = seedScope.ServiceProvider.GetRequiredService<ILogger<AuthorizationModule>>();
                    seedLogger.LogError(ex,
                        "PermissionDbSeeder failed; provider-declared permissions may not appear in admin UI until the next restart.");
                }
            }
        }

        var permissionManager = context.ServiceProvider.GetRequiredService<IPermissionManager>();
        await permissionManager.RefreshAsync();

        // SuperAdmin role-existence diagnostic. Logged at Information so the
        // current super-admin mode is visible on every startup (helps the
        // "why am I locked out?" runbook), and at Warning when configured
        // roles aren't found in Identity (typo / role renamed). Non-fatal
        // by design: an empty SuperAdminRoles list is legitimate, and
        // failing startup over an Identity row mismatch would create a
        // chicken-and-egg with the seeder.
        var optionsSnapshot = context.ServiceProvider
            .GetRequiredService<IOptions<Tnzi.Authorization.Options.AuthorizationOptions>>().Value;
        var logger = context.ServiceProvider.GetRequiredService<ILogger<AuthorizationModule>>();
        if (optionsSnapshot.SuperAdminRoles.Count == 0)
        {
            logger.LogInformation(
                "Authorization SuperAdmin bypass is DISABLED (SuperAdminRoles is empty). " +
                "Every permission check will consult ModuleUser/ModuleRole/RoleFunction tables.");
        }
        else
        {
            logger.LogInformation(
                "Authorization SuperAdmin bypass enabled for roles: {Roles}",
                string.Join(", ", optionsSnapshot.SuperAdminRoles));

            // Verify every configured super-admin role actually exists in
            // Identity_Role. A missing role means the configured name will
            // never match — same outcome as misconfiguring the JSON key.
            await using var scope = context.ServiceProvider.CreateAsyncScope();
            var roleRepo = scope.ServiceProvider.GetService<IRepository<Tnzi.Identity.Entities.Role, Guid>>();
            if (roleRepo != null)
            {
                var existingNames = await roleRepo
                    .Where(r => r.Name != null)
                    .Select(r => r.Name!)
                    .ToListAsync();
                var existingSet = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
                var missing = optionsSnapshot.SuperAdminRoles
                    .Where(name => !existingSet.Contains(name))
                    .ToList();
                if (missing.Count > 0)
                {
                    logger.LogWarning(
                        "Authorization.SuperAdminRoles contains role names that do not exist in Identity: {Missing}. " +
                        "Users will never be treated as super-admin via those entries until a matching Identity role is created.",
                        string.Join(", ", missing));
                }
            }
        }
    }
}
