using AuthOptions = Tnzi.Authorization.Options.AuthorizationOptions;
using IdentityRole = Tnzi.Identity.Entities.Role;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.Authorization.Tests.Integration;

/// <summary>
/// 用户直授（UserFunction）集成测试:真实 SQLite 仓储 + 真实
/// <see cref="UserFunctionService"/> / <see cref="FunctionAuthorizationService"/>,
/// mock 角色成员关系。授权者 = 测试基类注册的 ICurrentUser。覆盖:
/// <list type="bullet">
///   <item>直授参与权限解析,与角色授权取并集;deny 行从并集中扣除(用户级优先);</item>
///   <item>禁用功能的直授不生效;deny 不产生权限、不影响超管;</item>
///   <item>Assign 增量(翻转 deny)/Set 覆盖(翻转落入集内的 deny)/SetDenied 覆盖(翻转落入集内的 allow)/Remove/Clear 往返;</item>
///   <item>委托护栏:subset 约束(grant 与 deny 同规)、超管目标保护、支配约束、超管绕过。</item>
/// </list>
/// </summary>
public class UserFunctionServiceIntegrationTests : IntegratedTestBase<AuthorizationTestDbContext>
{
    /// <summary>授权者 = 测试基类 ICurrentUser 的默认用户。</summary>
    private static readonly Guid GrantorId = TestHelper.DefaultTestUserId;
    private static readonly Guid GrantorRoleId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid TargetUserId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid TargetRoleId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    private readonly Mock<IUserRoleService> _userRoleService = new();
    private readonly AuthOptions _authOptions = new();
    private readonly Dictionary<Guid, string[]> _rolesByUser = new();
    private readonly Dictionary<Guid, Guid[]> _roleIdsByUser = new();

    public UserFunctionServiceIntegrationTests()
    {
        // 单一数据驱动 mock:各测试往字典塞用户→角色映射即可。
        _userRoleService
            .Setup(s => s.GetUserRolesAsync(It.IsAny<IEnumerable<Guid>>()))
            .ReturnsAsync((IEnumerable<Guid> ids) => ids
                .Where(_rolesByUser.ContainsKey)
                .ToDictionary(id => id, id => (IEnumerable<string>)_rolesByUser[id]));
        _userRoleService
            .Setup(s => s.GetUserRoleIdsAsync(It.IsAny<Guid>()))
            .ReturnsAsync((Guid id) => _roleIdsByUser.TryGetValue(id, out var roleIds)
                ? roleIds
                : Array.Empty<Guid>());
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        AddRepo<FunctionModule>(services);
        AddRepo<ModuleFunction>(services);
        AddRepo<RoleFunction>(services);
        AddRepo<UserFunction>(services);
        AddRepo<IdentityRole>(services);

        services.AddScoped(_ => _userRoleService.Object);
        services.AddScoped(_ => MsOptions.Create(_authOptions));

        services.AddScoped(sp => new FunctionAuthorizationService(
            sp.GetRequiredService<IRepository<FunctionModule, Guid>>(),
            sp.GetRequiredService<IRepository<ModuleFunction, Guid>>(),
            sp.GetRequiredService<IRepository<RoleFunction, Guid>>(),
            sp.GetRequiredService<IRepository<UserFunction, Guid>>(),
            sp,
            sp.GetRequiredService<IUserRoleService>(),
            functionAuthCache: null,
            options: sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthOptions>>(),
            roleRepository: sp.GetRequiredService<IRepository<IdentityRole, Guid>>()));

        services.AddScoped<IUserFunctionService>(sp => new UserFunctionService(
            sp,
            sp.GetRequiredService<IRepository<UserFunction, Guid>>(),
            sp.GetRequiredService<IRepository<ModuleFunction, Guid>>(),
            sp.GetRequiredService<FunctionAuthorizationService>(),
            functionAuthCache: null));
    }

    private static void AddRepo<TEntity>(IServiceCollection services) where TEntity : class, Tnzi.Domain.Entities.IEntity<Guid>
    {
        services.AddScoped<IRepository<TEntity, Guid>>(sp =>
            new EFCoreRepository<AuthorizationTestDbContext, TEntity, Guid>(
                sp.GetRequiredService<AuthorizationTestDbContext>(), serviceProvider: sp));
    }

    /// <summary>授权者提升为超管(护栏绕过,聚焦解析/CRUD 语义的测试用)。</summary>
    private void MakeGrantorSuperAdmin()
    {
        _authOptions.SuperAdminRoles.Add("SuperAdmin");
        _rolesByUser[GrantorId] = ["SuperAdmin"];
    }

    /// <summary>授权者设为普通管理员,经自己的角色持有指定功能码。</summary>
    private async Task MakeGrantorRegularWithAsync(params ModuleFunction[] heldFunctions)
    {
        _rolesByUser[GrantorId] = ["Staff"];
        _roleIdsByUser[GrantorId] = [GrantorRoleId];
        foreach (var fn in heldFunctions)
        {
            await DbContext.RoleFunctions.AddAsync(new RoleFunction
            {
                Id = Guid.NewGuid(), RoleId = GrantorRoleId, FunctionId = fn.Id,
                IsEnabled = true, CreationTime = DateTime.UtcNow,
            });
        }
        await DbContext.SaveChangesAsync();
    }

    /// <summary>播种模块 + 三个功能码 (userView / roleView / diagnostics)。</summary>
    private async Task<(ModuleFunction UserView, ModuleFunction RoleView, ModuleFunction Diagnostics)> SeedCatalogueAsync()
    {
        var module = new FunctionModule { Id = Guid.NewGuid(), Name = "System", Code = "system", CreationTime = DateTime.UtcNow };
        await DbContext.FunctionModules.AddAsync(module);

        var userView = NewFunction(module.Id, "View Users", "user.view");
        var roleView = NewFunction(module.Id, "View Roles", "role.view");
        var diagnostics = NewFunction(module.Id, "View Diagnostics", "system.diagnostics.view");
        await DbContext.ModuleFunctions.AddRangeAsync(userView, roleView, diagnostics);
        await DbContext.SaveChangesAsync();
        return (userView, roleView, diagnostics);
    }

    private static ModuleFunction NewFunction(Guid moduleId, string name, string code, bool isEnabled = true) => new()
    {
        Id = Guid.NewGuid(), Name = name, Code = code,
        ModuleId = moduleId, IsEnabled = isEnabled, CreationTime = DateTime.UtcNow,
    };

    private async Task SeedUserFunctionRowAsync(Guid userId, Guid functionId, bool isGranted = true, bool isEnabled = true)
    {
        await DbContext.UserFunctions.AddAsync(new UserFunction
        {
            Id = Guid.NewGuid(), UserId = userId, FunctionId = functionId,
            IsGranted = isGranted, IsEnabled = isEnabled, CreationTime = DateTime.UtcNow,
        });
        await DbContext.SaveChangesAsync();
    }

    private (IUserFunctionService UserFunctions, FunctionAuthorizationService Auth) GetServices()
    {
        var scope = ServiceProvider.CreateScope().ServiceProvider;
        return (scope.GetRequiredService<IUserFunctionService>(),
                scope.GetRequiredService<FunctionAuthorizationService>());
    }

    #region 权限解析(第四并集源)

    [Fact]
    public async Task Direct_grant_participates_in_permission_resolution()
    {
        var (userView, roleView, _) = await SeedCatalogueAsync();
        MakeGrantorSuperAdmin();
        var (userFunctions, auth) = GetServices();

        var assign = await userFunctions.AssignFunctionsToUserAsync(TargetUserId, [userView.Id]);
        assign.Succeeded.ShouldBeTrue();

        (await auth.CheckPermissionAsync(TargetUserId, "user.view")).ShouldBeTrue();
        (await auth.CheckPermissionAsync(TargetUserId, "role.view")).ShouldBeFalse();
        // 权限码大小写不敏感,与角色路径一致。
        (await auth.CheckPermissionAsync(TargetUserId, "User.View")).ShouldBeTrue();
        _ = roleView;
    }

    [Fact]
    public async Task Direct_grant_unions_with_role_grants()
    {
        var (userView, roleView, _) = await SeedCatalogueAsync();
        MakeGrantorSuperAdmin();

        // 目标用户经角色持有 role.view
        _roleIdsByUser[TargetUserId] = [TargetRoleId];
        await DbContext.RoleFunctions.AddAsync(new RoleFunction
        {
            Id = Guid.NewGuid(), RoleId = TargetRoleId, FunctionId = roleView.Id,
            IsEnabled = true, CreationTime = DateTime.UtcNow,
        });
        await DbContext.SaveChangesAsync();

        var (userFunctions, auth) = GetServices();
        (await userFunctions.AssignFunctionsToUserAsync(TargetUserId, [userView.Id])).Succeeded.ShouldBeTrue();

        var permissions = (await auth.GetUserPermissionNamesAsync(TargetUserId)).ToList();
        permissions.ShouldContain("user.view");
        permissions.ShouldContain("role.view");
    }

    [Fact]
    public async Task Disabled_function_direct_grant_is_not_effective()
    {
        var module = new FunctionModule { Id = Guid.NewGuid(), Name = "System", Code = "system", CreationTime = DateTime.UtcNow };
        await DbContext.FunctionModules.AddAsync(module);
        var disabledFn = NewFunction(module.Id, "Disabled", "system.disabled.view", isEnabled: false);
        await DbContext.ModuleFunctions.AddAsync(disabledFn);
        await DbContext.SaveChangesAsync();

        await SeedUserFunctionRowAsync(TargetUserId, disabledFn.Id);

        var (_, auth) = GetServices();
        (await auth.CheckPermissionAsync(TargetUserId, "system.disabled.view")).ShouldBeFalse();
    }

    [Fact]
    public async Task Deny_rows_do_not_grant_and_stay_out_of_the_allow_reads()
    {
        var (userView, _, _) = await SeedCatalogueAsync();
        await SeedUserFunctionRowAsync(TargetUserId, userView.Id, isGranted: false);

        var (userFunctions, auth) = GetServices();
        // deny 行是减法而非授权:不产生权限,也不出现在 allow 读端点。
        (await auth.CheckPermissionAsync(TargetUserId, "user.view")).ShouldBeFalse();
        (await userFunctions.GetUserFunctionIdsAsync(TargetUserId)).Data!.ShouldBeEmpty();
        var deniedIds = (await userFunctions.GetUserDeniedFunctionIdsAsync(TargetUserId)).Data!.ToList();
        deniedIds.ShouldBe(new[] { userView.Id });
    }

    [Fact]
    public async Task Deny_subtracts_role_derived_permission()
    {
        var (userView, roleView, _) = await SeedCatalogueAsync();
        MakeGrantorSuperAdmin();

        // 目标用户经角色持有 user.view + role.view
        _roleIdsByUser[TargetUserId] = [TargetRoleId];
        await DbContext.RoleFunctions.AddRangeAsync(
            new RoleFunction { Id = Guid.NewGuid(), RoleId = TargetRoleId, FunctionId = userView.Id, IsEnabled = true, CreationTime = DateTime.UtcNow },
            new RoleFunction { Id = Guid.NewGuid(), RoleId = TargetRoleId, FunctionId = roleView.Id, IsEnabled = true, CreationTime = DateTime.UtcNow });
        await DbContext.SaveChangesAsync();

        var (userFunctions, auth) = GetServices();
        (await auth.CheckPermissionAsync(TargetUserId, "user.view")).ShouldBeTrue();

        // deny user.view → 角色授予被用户级扣除,role.view 不受影响
        (await userFunctions.SetUserDeniedFunctionsAsync(TargetUserId, [userView.Id])).Succeeded.ShouldBeTrue();
        (await auth.CheckPermissionAsync(TargetUserId, "user.view")).ShouldBeFalse();
        (await auth.CheckPermissionAsync(TargetUserId, "role.view")).ShouldBeTrue();

        // 清空 deny 集 → 角色授予恢复
        (await userFunctions.SetUserDeniedFunctionsAsync(TargetUserId, [])).Succeeded.ShouldBeTrue();
        (await auth.CheckPermissionAsync(TargetUserId, "user.view")).ShouldBeTrue();
    }

    [Fact]
    public async Task Deny_does_not_affect_super_admin_members()
    {
        var (userView, _, _) = await SeedCatalogueAsync();
        _authOptions.SuperAdminRoles.Add("SuperAdmin");
        _rolesByUser[TargetUserId] = ["SuperAdmin"];
        await SeedUserFunctionRowAsync(TargetUserId, userView.Id, isGranted: false);

        var (_, auth) = GetServices();
        // 超管在检查最前短路,deny 行对其无效。
        (await auth.CheckPermissionAsync(TargetUserId, "user.view")).ShouldBeTrue();
    }

    [Fact]
    public async Task SetDenied_flips_existing_allow_row_and_assign_flips_deny_row()
    {
        var (userView, _, _) = await SeedCatalogueAsync();
        MakeGrantorSuperAdmin();
        var (userFunctions, auth) = GetServices();

        // allow → deny:唯一索引下同功能只有一行,后写者赢
        (await userFunctions.AssignFunctionsToUserAsync(TargetUserId, [userView.Id])).Succeeded.ShouldBeTrue();
        (await userFunctions.SetUserDeniedFunctionsAsync(TargetUserId, [userView.Id])).Succeeded.ShouldBeTrue();
        var rows = DbContext.UserFunctions.Where(uf => uf.UserId == TargetUserId).ToList();
        rows.ShouldHaveSingleItem();
        rows[0].IsGranted.ShouldBeFalse();
        (await auth.CheckPermissionAsync(TargetUserId, "user.view")).ShouldBeFalse();

        // deny → allow:显式授予翻转 deny 行
        (await userFunctions.AssignFunctionsToUserAsync(TargetUserId, [userView.Id])).Succeeded.ShouldBeTrue();
        rows = DbContext.UserFunctions.Where(uf => uf.UserId == TargetUserId).ToList();
        rows.ShouldHaveSingleItem();
        rows[0].IsGranted.ShouldBeTrue();
        (await auth.CheckPermissionAsync(TargetUserId, "user.view")).ShouldBeTrue();
    }

    [Fact]
    public async Task NonSuper_grantor_cannot_deny_codes_not_held()
    {
        var (userView, _, diagnostics) = await SeedCatalogueAsync();
        await MakeGrantorRegularWithAsync(userView);
        var (userFunctions, _) = GetServices();

        var result = await userFunctions.SetUserDeniedFunctionsAsync(TargetUserId, [diagnostics.Id]);
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        result.Message!.ShouldContain("deny");
        DbContext.UserFunctions.Count(uf => uf.UserId == TargetUserId).ShouldBe(0);
    }

    #endregion

    #region CRUD 往返

    [Fact]
    public async Task Assign_is_incremental_and_skips_existing()
    {
        var (userView, roleView, _) = await SeedCatalogueAsync();
        MakeGrantorSuperAdmin();
        var (userFunctions, _) = GetServices();

        (await userFunctions.AssignFunctionsToUserAsync(TargetUserId, [userView.Id])).Succeeded.ShouldBeTrue();
        (await userFunctions.AssignFunctionsToUserAsync(TargetUserId, [userView.Id, roleView.Id])).Succeeded.ShouldBeTrue();

        DbContext.UserFunctions.Count(uf => uf.UserId == TargetUserId).ShouldBe(2);
    }

    [Fact]
    public async Task Assign_missing_function_returns_404()
    {
        await SeedCatalogueAsync();
        MakeGrantorSuperAdmin();
        var (userFunctions, _) = GetServices();

        var result = await userFunctions.AssignFunctionsToUserAsync(TargetUserId, [Guid.NewGuid()]);
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task Remove_and_clear_roundtrip()
    {
        var (userView, roleView, _) = await SeedCatalogueAsync();
        MakeGrantorSuperAdmin();
        var (userFunctions, _) = GetServices();

        (await userFunctions.AssignFunctionsToUserAsync(TargetUserId, [userView.Id, roleView.Id])).Succeeded.ShouldBeTrue();

        (await userFunctions.RemoveFunctionsFromUserAsync(TargetUserId, [userView.Id])).Succeeded.ShouldBeTrue();
        var ids = (await userFunctions.GetUserFunctionIdsAsync(TargetUserId)).Data!.ToList();
        ids.ShouldBe(new[] { roleView.Id });

        (await userFunctions.ClearUserFunctionsAsync(TargetUserId)).Succeeded.ShouldBeTrue();
        (await userFunctions.GetUserFunctionIdsAsync(TargetUserId)).Data!.ShouldBeEmpty();
    }

    [Fact]
    public async Task Set_overwrites_allow_rows_and_preserves_unrelated_deny_rows()
    {
        var (userView, roleView, diagnostics) = await SeedCatalogueAsync();
        MakeGrantorSuperAdmin();
        var (userFunctions, _) = GetServices();

        (await userFunctions.AssignFunctionsToUserAsync(TargetUserId, [userView.Id])).Succeeded.ShouldBeTrue();
        // 预埋一条不在新 allow 集内的 deny 行:Set 只覆盖 allow 集,不动它。
        await SeedUserFunctionRowAsync(TargetUserId, diagnostics.Id, isGranted: false);

        (await userFunctions.SetUserFunctionsAsync(TargetUserId, [roleView.Id])).Succeeded.ShouldBeTrue();

        var rows = DbContext.UserFunctions.Where(uf => uf.UserId == TargetUserId).ToList();
        rows.Count.ShouldBe(2);
        rows.ShouldContain(uf => uf.FunctionId == roleView.Id && uf.IsGranted);
        rows.ShouldContain(uf => uf.FunctionId == diagnostics.Id && !uf.IsGranted);
        rows.ShouldNotContain(uf => uf.FunctionId == userView.Id);
    }

    #endregion

    #region 有界写入（切片）

    [Fact]
    public async Task Scoped_set_leaves_grants_outside_the_scope_untouched()
    {
        var (userView, roleView, diagnostics) = await SeedCatalogueAsync();
        MakeGrantorSuperAdmin();
        var (userFunctions, _) = GetServices();

        // 目标用户有两条直授:userView 落在消费方的切片内,diagnostics 在切片外
        // (例如超管从框架自己的授权页单独授出的)。
        (await userFunctions.AssignFunctionsToUserAsync(TargetUserId, [userView.Id, diagnostics.Id])).Succeeded.ShouldBeTrue();

        var scope = new[] { userView.Id, roleView.Id };
        (await userFunctions.SetUserFunctionsInScopeAsync(TargetUserId, scope, [roleView.Id])).Succeeded.ShouldBeTrue();

        var ids = (await userFunctions.GetUserFunctionIdsAsync(TargetUserId)).Data!.ToList();
        ids.ShouldContain(roleView.Id);          // 切片内的新集写入
        ids.ShouldNotContain(userView.Id);       // 切片内的旧集被覆盖
        ids.ShouldContain(diagnostics.Id);       // 切片外原样保留 ← 本方法存在的理由

        // 对照:同样的入参走整集覆盖,切片外的 diagnostics 会被静默删除。
        (await userFunctions.SetUserFunctionsAsync(TargetUserId, [roleView.Id])).Succeeded.ShouldBeTrue();
        (await userFunctions.GetUserFunctionIdsAsync(TargetUserId)).Data!.ShouldNotContain(diagnostics.Id);
    }

    [Fact]
    public async Task Scoped_set_overwrites_in_scope_allow_and_flips_in_scope_deny()
    {
        var (userView, roleView, _) = await SeedCatalogueAsync();
        MakeGrantorSuperAdmin();
        var (userFunctions, _) = GetServices();

        (await userFunctions.AssignFunctionsToUserAsync(TargetUserId, [userView.Id])).Succeeded.ShouldBeTrue();
        await SeedUserFunctionRowAsync(TargetUserId, roleView.Id, isGranted: false);

        // 切片内语义与 SetUserFunctionsAsync 一致:旧 allow 删除,命中的 deny 翻转为 allow。
        var scope = new[] { userView.Id, roleView.Id };
        (await userFunctions.SetUserFunctionsInScopeAsync(TargetUserId, scope, [roleView.Id])).Succeeded.ShouldBeTrue();

        var rows = DbContext.UserFunctions.Where(uf => uf.UserId == TargetUserId).ToList();
        rows.ShouldHaveSingleItem();
        rows[0].FunctionId.ShouldBe(roleView.Id);
        rows[0].IsGranted.ShouldBeTrue();
    }

    [Fact]
    public async Task Scoped_set_preserves_in_scope_deny_rows_it_is_not_granting()
    {
        var (userView, roleView, _) = await SeedCatalogueAsync();
        MakeGrantorSuperAdmin();
        var (userFunctions, _) = GetServices();

        // roleView 的 deny 行落在切片内但不在新 allow 集里 → 不翻转、不删除
        // (与整集版 Set_overwrites_allow_rows_and_preserves_unrelated_deny_rows 同规)。
        await SeedUserFunctionRowAsync(TargetUserId, roleView.Id, isGranted: false);

        var scope = new[] { userView.Id, roleView.Id };
        (await userFunctions.SetUserFunctionsInScopeAsync(TargetUserId, scope, [userView.Id])).Succeeded.ShouldBeTrue();

        var rows = DbContext.UserFunctions.Where(uf => uf.UserId == TargetUserId).ToList();
        rows.Count.ShouldBe(2);
        rows.ShouldContain(uf => uf.FunctionId == userView.Id && uf.IsGranted);
        rows.ShouldContain(uf => uf.FunctionId == roleView.Id && !uf.IsGranted);
    }

    [Fact]
    public async Task Scoped_set_denied_leaves_deny_rows_outside_the_scope_untouched()
    {
        var (userView, roleView, diagnostics) = await SeedCatalogueAsync();
        MakeGrantorSuperAdmin();
        var (userFunctions, _) = GetServices();

        await SeedUserFunctionRowAsync(TargetUserId, userView.Id, isGranted: false);
        await SeedUserFunctionRowAsync(TargetUserId, diagnostics.Id, isGranted: false);

        var scope = new[] { userView.Id, roleView.Id };
        (await userFunctions.SetUserDeniedFunctionsInScopeAsync(TargetUserId, scope, [roleView.Id])).Succeeded.ShouldBeTrue();

        var denied = (await userFunctions.GetUserDeniedFunctionIdsAsync(TargetUserId)).Data!.ToList();
        denied.ShouldContain(roleView.Id);
        denied.ShouldNotContain(userView.Id);
        denied.ShouldContain(diagnostics.Id);    // 切片外的 deny 行原样保留
    }

    [Fact]
    public async Task Scoped_set_rejects_ids_outside_the_declared_scope()
    {
        var (userView, roleView, diagnostics) = await SeedCatalogueAsync();
        MakeGrantorSuperAdmin();
        var (userFunctions, _) = GetServices();

        (await userFunctions.AssignFunctionsToUserAsync(TargetUserId, [userView.Id])).Succeeded.ShouldBeTrue();

        // 边界由框架强制:越界的 id 直接 400,一行都不写。
        var scope = new[] { roleView.Id };
        var granted = await userFunctions.SetUserFunctionsInScopeAsync(TargetUserId, scope, [roleView.Id, diagnostics.Id]);
        granted.Succeeded.ShouldBeFalse();
        granted.Code.ShouldBe(400);
        granted.Message!.ShouldContain(diagnostics.Id.ToString());

        var denied = await userFunctions.SetUserDeniedFunctionsInScopeAsync(TargetUserId, scope, [diagnostics.Id]);
        denied.Succeeded.ShouldBeFalse();
        denied.Code.ShouldBe(400);

        // 越界请求整体不落地,原有直授不受影响。
        var ids = (await userFunctions.GetUserFunctionIdsAsync(TargetUserId)).Data!.ToList();
        ids.ShouldBe(new[] { userView.Id });
    }

    [Fact]
    public async Task Scoped_set_still_enforces_the_delegation_guard()
    {
        var (userView, _, diagnostics) = await SeedCatalogueAsync();
        await MakeGrantorRegularWithAsync(userView);
        var (userFunctions, _) = GetServices();

        // 切片不是绕过护栏的口子:授权者仍只能授出/拒绝自己持有的码。
        var scope = new[] { diagnostics.Id };
        (await userFunctions.SetUserFunctionsInScopeAsync(TargetUserId, scope, [diagnostics.Id])).Code.ShouldBe(403);
        (await userFunctions.SetUserDeniedFunctionsInScopeAsync(TargetUserId, scope, [diagnostics.Id])).Code.ShouldBe(403);
        DbContext.UserFunctions.Count(uf => uf.UserId == TargetUserId).ShouldBe(0);
    }

    [Fact]
    public async Task Scoped_set_with_an_empty_set_clears_only_the_scope()
    {
        var (userView, _, diagnostics) = await SeedCatalogueAsync();
        MakeGrantorSuperAdmin();
        var (userFunctions, _) = GetServices();

        (await userFunctions.AssignFunctionsToUserAsync(TargetUserId, [userView.Id, diagnostics.Id])).Succeeded.ShouldBeTrue();

        // 空集 = 清空切片内的直授,而不是 ClearUserFunctionsAsync 的清空全部。
        (await userFunctions.SetUserFunctionsInScopeAsync(TargetUserId, [userView.Id], [])).Succeeded.ShouldBeTrue();

        var ids = (await userFunctions.GetUserFunctionIdsAsync(TargetUserId)).Data!.ToList();
        ids.ShouldBe(new[] { diagnostics.Id });
    }

    [Fact]
    public async Task Scoped_set_rejects_functions_that_do_not_exist()
    {
        var (userView, _, _) = await SeedCatalogueAsync();
        MakeGrantorSuperAdmin();
        var (userFunctions, _) = GetServices();

        var ghost = Guid.NewGuid();
        var result = await userFunctions.SetUserFunctionsInScopeAsync(TargetUserId, [userView.Id, ghost], [ghost]);
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    #endregion

    #region 委托护栏

    [Fact]
    public async Task NonSuper_grantor_cannot_grant_codes_not_held()
    {
        var (userView, _, diagnostics) = await SeedCatalogueAsync();
        await MakeGrantorRegularWithAsync(userView);
        var (userFunctions, _) = GetServices();

        var result = await userFunctions.AssignFunctionsToUserAsync(TargetUserId, [diagnostics.Id]);
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        result.Message!.ShouldContain("system.diagnostics.view");
        DbContext.UserFunctions.Count(uf => uf.UserId == TargetUserId).ShouldBe(0);
    }

    [Fact]
    public async Task NonSuper_grantor_can_grant_held_codes()
    {
        var (userView, _, _) = await SeedCatalogueAsync();
        await MakeGrantorRegularWithAsync(userView);
        var (userFunctions, auth) = GetServices();

        var result = await userFunctions.AssignFunctionsToUserAsync(TargetUserId, [userView.Id]);
        result.Succeeded.ShouldBeTrue();
        (await auth.CheckPermissionAsync(TargetUserId, "user.view")).ShouldBeTrue();
    }

    [Fact]
    public async Task NonSuper_grantor_cannot_manage_super_admin_target()
    {
        var (userView, _, _) = await SeedCatalogueAsync();
        _authOptions.SuperAdminRoles.Add("SuperAdmin");
        await MakeGrantorRegularWithAsync(userView);
        _rolesByUser[TargetUserId] = ["SuperAdmin"];
        var (userFunctions, _) = GetServices();

        var result = await userFunctions.AssignFunctionsToUserAsync(TargetUserId, [userView.Id]);
        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        result.Message!.ShouldContain("super administrator");
    }

    [Fact]
    public async Task NonSuper_grantor_cannot_manage_user_whose_direct_grants_exceed_own()
    {
        var (userView, _, diagnostics) = await SeedCatalogueAsync();
        await MakeGrantorRegularWithAsync(userView);
        // 目标用户已有一条授权者不持有的直授(如超管授予的诊断权限)。
        await SeedUserFunctionRowAsync(TargetUserId, diagnostics.Id);
        var (userFunctions, _) = GetServices();

        // 支配约束:remove / clear / assign 全部被拦。
        (await userFunctions.RemoveFunctionsFromUserAsync(TargetUserId, [diagnostics.Id])).Code.ShouldBe(403);
        (await userFunctions.ClearUserFunctionsAsync(TargetUserId)).Code.ShouldBe(403);
        (await userFunctions.AssignFunctionsToUserAsync(TargetUserId, [userView.Id])).Code.ShouldBe(403);
        DbContext.UserFunctions.Count(uf => uf.UserId == TargetUserId).ShouldBe(1);
    }

    [Fact]
    public async Task Super_admin_grantor_bypasses_guard()
    {
        var (_, _, diagnostics) = await SeedCatalogueAsync();
        MakeGrantorSuperAdmin();
        var (userFunctions, _) = GetServices();

        // 超管可授出任何码(包括自己"显式"并不持有的 Technical 码)。
        var result = await userFunctions.AssignFunctionsToUserAsync(TargetUserId, [diagnostics.Id]);
        result.Succeeded.ShouldBeTrue();
    }

    #endregion
}
