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
        // PostConfigure applies the out-of-the-box convention (unconfigured
        // SuperAdminRoles → ["SuperAdmin"]). It CANNOT be a property
        // initializer: the configuration binder APPENDS to a pre-populated
        // List, so a class default would duplicate configured entries and
        // trip the validator's duplicate check. Validation runs after
        // PostConfigure, so it always sees the final value.
        services.AddTnziOptions<Tnzi.Authorization.Options.AuthorizationOptions,
            Tnzi.Authorization.Options.AuthorizationOptionsValidator>(context.Configuration)
            .PostConfigure(Tnzi.Authorization.Options.AuthorizationOptions.ApplyConventionDefaults);

        // 注册授权服务（单一实现，多接口注册）
        services.AddScoped<FunctionAuthorizationService>();
        services.AddScoped<Tnzi.Authorization.Services.IFunctionAuthorizationService>(sp => sp.GetRequiredService<FunctionAuthorizationService>());
        services.AddScoped<IModuleManagementService>(sp => sp.GetRequiredService<FunctionAuthorizationService>());
        services.AddScoped<IRoleFunctionService>(sp => sp.GetRequiredService<FunctionAuthorizationService>());
        services.AddScoped<Tnzi.Security.Authorization.IFunctionAuthorizationService>(sp => sp.GetRequiredService<FunctionAuthorizationService>());
        services.AddScoped<IUserFunctionService, UserFunctionService>();
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

        // First-super-admin bootstrap (Authorization:BootstrapSuperAdminUsers),
        // invoked from RunStartupTasksAsync after role seeding.
        services.AddScoped<SuperAdminBootstrapper>();

        // Post-migration startup task: runs the permission-catalogue seed +
        // PermissionManager.RefreshAsync + built-in role seed + super-admin
        // bootstrap AFTER database migrations (this used to run in module init,
        // which executes BEFORE migrations and silently failed on an empty DB,
        // needing a second boot). Not an IDataSeeder, so it always runs (not
        // gated by the seed switch) and isn't double-registered by DataSeederManager.
        services.AddTransient<IPostMigrationStartupTask, AuthorizationStartupTask>();

        // This module's own permission codes (authorization.*). Every other
        // module declares its codes in-module the same way — the declaration
        // contract lives in core Tnzi.Security.Authorization, so no module
        // needs to reference this assembly to declare permissions
        // (docs/coding-standards/permissions.md).
        services.AddTransient<IPermissionDefinitionProvider, AuthorizationPermissions>();

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
    /// <summary>
    /// Post-migration startup work: permission-catalogue seed + <c>PermissionManager.RefreshAsync</c>
    /// + built-in super-admin role seed + first-super-admin bootstrap + role-existence
    /// diagnostics. Runs via <see cref="AuthorizationStartupTask"/> (an
    /// <see cref="IPostMigrationStartupTask"/>) AFTER database migrations — this used to
    /// live in module init, which runs BEFORE migrations and therefore failed silently on
    /// a brand-new empty database, requiring a second boot to take effect.
    /// </summary>
    internal static async Task RunStartupTasksAsync(IServiceProvider serviceProvider)
    {
        // Seed BEFORE PermissionManager.RefreshAsync so the manager's
        // snapshot picks up provider-declared permissions on the SAME
        // run they're introduced — without seeding first they'd be
        // missing until the next restart (manager loads from DB).
        await using (var seedScope = serviceProvider.CreateAsyncScope())
        {
            var providers = seedScope.ServiceProvider.GetServices<IPermissionDefinitionProvider>().ToList();
            if (providers.Count > 0)
            {
                try
                {
                    var seeder = seedScope.ServiceProvider.GetRequiredService<PermissionDbSeeder>();
                    var touched = await seeder.SeedAsync(providers);

                    // Seed writes can add/enable codes. With a SHARED cache
                    // (e.g. Redis) and rolling deploys, instances still
                    // running keep a hot super-admin catalogue for up to its
                    // TTL - a freshly seeded code would stay invisible to
                    // super admins. Invalidate explicitly; no-op for a cold
                    // per-process cache.
                    if (touched > 0)
                    {
                        var functionAuthCache = seedScope.ServiceProvider.GetService<FunctionAuthCache>();
                        if (functionAuthCache != null)
                        {
                            await functionAuthCache.InvalidateSuperAdminCatalogueAsync();
                        }
                    }
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

        var permissionManager = serviceProvider.GetRequiredService<IPermissionManager>();
        await permissionManager.RefreshAsync();

        var optionsSnapshot = serviceProvider
            .GetRequiredService<IOptions<Tnzi.Authorization.Options.AuthorizationOptions>>().Value;
        var logger = serviceProvider.GetRequiredService<ILogger<AuthorizationModule>>();

        // Built-in role seeding (default ON): create an IsSystem role for
        // every configured super-admin role name that doesn't exist yet, so
        // the convention (role named SuperAdmin) works out of the box. Runs
        // BEFORE the existence diagnostics below so freshly seeded roles
        // don't trigger the missing-role warning. Never modifies existing
        // roles. The empty-list guard covers DisableSuperAdminBypass.
        if (optionsSnapshot.SeedBuiltInAdminRoles && optionsSnapshot.SuperAdminRoles.Count > 0)
        {
            await SeedBuiltInAdminRolesAsync(serviceProvider, optionsSnapshot, logger);
        }

        // First-super-admin bootstrap: assign the configured user names to
        // the first existing super-admin role while ALL super-admin roles
        // have zero members (recovery semantics — see SuperAdminBootstrapper).
        // Non-fatal by design: a failed bootstrap logs and startup continues,
        // matching the seeding blocks above.
        if (optionsSnapshot.BootstrapSuperAdminUsers.Count > 0 && optionsSnapshot.SuperAdminRoles.Count > 0)
        {
            try
            {
                await using var bootstrapScope = serviceProvider.CreateAsyncScope();
                var bootstrapper = bootstrapScope.ServiceProvider.GetRequiredService<SuperAdminBootstrapper>();
                await bootstrapper.BootstrapAsync(optionsSnapshot.SuperAdminRoles, optionsSnapshot.BootstrapSuperAdminUsers);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Super-admin bootstrap failed; startup continues. Assign a user to a super-admin role manually if needed.");
            }
        }

        // Admin-tier role-existence diagnostics. Logged at Information so the
        // current admin-tier mode is visible on every startup (helps the
        // "why am I locked out?" runbook), and at Warning when configured
        // roles aren't found in Identity (typo / role renamed). Non-fatal
        // by design: empty role lists are legitimate, and failing startup
        // over an Identity row mismatch would create a chicken-and-egg with
        // the seeder.

        if (optionsSnapshot.SuperAdminRoles.Count == 0)
        {
            logger.LogInformation(
                "Authorization SuperAdmin bypass is DISABLED (SuperAdminRoles is empty). " +
                "Every permission check will consult RoleFunction/UserFunction tables.");
        }
        else
        {
            logger.LogInformation(
                "Authorization SuperAdmin bypass enabled for roles: {Roles}",
                string.Join(", ", optionsSnapshot.SuperAdminRoles));
        }

        // Verify every configured super-admin role actually exists in
        // Identity_Role. A missing role means the configured name will
        // never match — same outcome as misconfiguring the JSON key.
        var configuredRoles = optionsSnapshot.SuperAdminRoles
            .Select(name => (name, list: "SuperAdminRoles"))
            .ToList();
        if (configuredRoles.Count > 0)
        {
            await using var scope = serviceProvider.CreateAsyncScope();
            var roleRepo = scope.ServiceProvider.GetService<IRepository<Tnzi.Identity.Entities.Role, Guid>>();
            if (roleRepo != null)
            {
                var existingNames = await roleRepo
                    .Where(r => r.Name != null)
                    .Select(r => r.Name!)
                    .ToListAsync();
                var existingSet = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
                foreach (var listGroup in configuredRoles
                             .Where(entry => !existingSet.Contains(entry.name))
                             .GroupBy(entry => entry.list))
                {
                    logger.LogWarning(
                        "Authorization.{List} contains role names that do not exist in Identity: {Missing}. " +
                        "Users will never be matched via those entries until a matching Identity role is created.",
                        listGroup.Key,
                        string.Join(", ", listGroup.Select(e => e.name)));
                }
            }
        }
    }

    /// <summary>
    /// Create an <c>IsSystem</c> Identity role for every configured admin-tier
    /// role name that doesn't exist yet. Additive only; failures are logged
    /// and never block startup (a startup race across instances is settled by
    /// the unique index on the normalized role name).
    /// </summary>
    private static async Task SeedBuiltInAdminRolesAsync(
        IServiceProvider serviceProvider,
        Tnzi.Authorization.Options.AuthorizationOptions options,
        ILogger logger)
    {
        var roleNames = options.SuperAdminRoles
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (roleNames.Count == 0)
        {
            logger.LogWarning(
                "Authorization.SeedBuiltInAdminRoles is enabled but SuperAdminRoles is empty; nothing to seed.");
            return;
        }

        await using var scope = serviceProvider.CreateAsyncScope();
        var roleRepo = scope.ServiceProvider.GetService<IRepository<Tnzi.Identity.Entities.Role, Guid>>();
        if (roleRepo == null)
        {
            logger.LogWarning(
                "Authorization.SeedBuiltInAdminRoles is enabled but the Identity role repository is unavailable; skipping.");
            return;
        }

        var existingNames = await roleRepo
            .Where(r => r.Name != null)
            .Select(r => r.Name!)
            .ToListAsync();
        var existingSet = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);

        foreach (var name in roleNames.Where(n => !existingSet.Contains(n)))
        {
            try
            {
                await roleRepo.InsertAsync(new Tnzi.Identity.Entities.Role
                {
                    Name = name,
                    NormalizedName = name.ToUpperInvariant(),
                    Description = "Built-in admin-tier role seeded by Authorization:SeedBuiltInAdminRoles.",
                    IsSystem = true,
                });
                logger.LogInformation("Seeded built-in admin role '{Role}' (IsSystem).", name);
            }
            catch (Exception ex)
            {
                // Most likely a startup race with another instance — the
                // unique index on NormalizedName settles the winner; either
                // way the role exists afterwards, which is all we need.
                logger.LogWarning(ex, "Seeding built-in admin role '{Role}' failed; continuing startup.", name);
            }
        }
    }
}
