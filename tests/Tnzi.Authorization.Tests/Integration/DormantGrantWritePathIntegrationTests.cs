using AuthOptions = Tnzi.Authorization.Options.AuthorizationOptions;
using IdentityRole = Tnzi.Identity.Entities.Role;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.Authorization.Tests.Integration;

/// <summary>
/// 「休眠授权」（指向退役或已禁用功能的 RoleFunction / UserFunction 行）在<b>写路径</b>上的处置。
/// </summary>
/// <remarks>
/// <para>
/// 退役模式 <c>Disable</c> 的承诺是「保留行与全部授权，重新声明时原样恢复」。
/// 这组用例钉住让这个承诺在<b>有人保存过矩阵之后</b>依然成立的三条规则：
/// </para>
/// <list type="number">
///   <item>读 id 列表只返回当前生效的功能 —— 矩阵只渲染生效项，GET → PUT 的往返必须是一次空操作；</item>
///   <item>覆盖写入（set / set-in-scope）绕开休眠行 —— 它们在矩阵够不到的地方，删掉等于把「保留」变成「清空」；</item>
///   <item>把休眠功能当作新授权提交时<b>拒绝</b>（404，文案给出码）—— 运行时永不读它，接受它就是一次报成功的静默失效。</item>
/// </list>
/// <para>
/// 显式的 <c>clear</c> 仍然清空一切：那是管理员明确要的「全部删除」，不是矩阵保存的副作用。
/// </para>
/// </remarks>
public class DormantGrantWritePathIntegrationTests : IntegratedTestBase<AuthorizationTestDbContext>
{
    private static readonly Guid GrantorId = TestHelper.DefaultTestUserId;
    private static readonly Guid GrantorRoleId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid RoleId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid TargetUserId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private readonly Mock<IUserRoleService> _userRoleService = new();
    private readonly AuthOptions _authOptions = new();
    private readonly Dictionary<Guid, string[]> _rolesByUser = new();
    private readonly Dictionary<Guid, Guid[]> _roleIdsByUser = new();

    public DormantGrantWritePathIntegrationTests()
    {
        _userRoleService
            .Setup(s => s.GetUserRolesAsync(It.IsAny<IEnumerable<Guid>>()))
            .ReturnsAsync((IEnumerable<Guid> ids) => ids
                .Where(_rolesByUser.ContainsKey)
                .ToDictionary(id => id, id => (IEnumerable<string>)_rolesByUser[id]));
        _userRoleService
            .Setup(s => s.GetUserRoleIdsAsync(It.IsAny<Guid>()))
            .ReturnsAsync((Guid id) => _roleIdsByUser.TryGetValue(id, out var roleIds) ? roleIds : Array.Empty<Guid>());
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

    private void MakeGrantorSuperAdmin()
    {
        _authOptions.SuperAdminRoles.Add("SuperAdmin");
        _rolesByUser[GrantorId] = ["SuperAdmin"];
    }

    /// <summary>授权者设为普通管理员，经自己的角色持有指定功能码。</summary>
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

    /// <summary>一个生效功能、一个退役功能（模块未加载）、一个被管理员禁用的功能。</summary>
    private async Task<(ModuleFunction Live, ModuleFunction Retired, ModuleFunction Disabled, ModuleFunction OtherLive)> SeedCatalogueAsync()
    {
        var module = new FunctionModule { Id = Guid.NewGuid(), Name = "Demo", Code = "demo", CreationTime = DateTime.UtcNow };
        await DbContext.FunctionModules.AddAsync(module);

        var live = NewFunction(module.Id, "View Things", "demo.thing.view");
        var otherLive = NewFunction(module.Id, "Create Things", "demo.thing.create");
        var retired = NewFunction(module.Id, "Gone", "demo.gone.view", isRetired: true);
        var disabled = NewFunction(module.Id, "Paused", "demo.paused.view", isEnabled: false);
        await DbContext.ModuleFunctions.AddRangeAsync(live, otherLive, retired, disabled);
        await DbContext.SaveChangesAsync();
        return (live, retired, disabled, otherLive);
    }

    private static ModuleFunction NewFunction(Guid moduleId, string name, string code, bool isEnabled = true, bool isRetired = false) => new()
    {
        Id = Guid.NewGuid(), Name = name, Code = code, ModuleId = moduleId,
        IsEnabled = isEnabled, IsRetired = isRetired, CreationTime = DateTime.UtcNow,
    };

    private async Task SeedRoleGrantAsync(Guid roleId, Guid functionId)
    {
        await DbContext.RoleFunctions.AddAsync(new RoleFunction
        {
            Id = Guid.NewGuid(), RoleId = roleId, FunctionId = functionId, IsEnabled = true, CreationTime = DateTime.UtcNow,
        });
        await DbContext.SaveChangesAsync();
    }

    private async Task SeedUserRowAsync(Guid userId, Guid functionId, bool isGranted = true)
    {
        await DbContext.UserFunctions.AddAsync(new UserFunction
        {
            Id = Guid.NewGuid(), UserId = userId, FunctionId = functionId,
            IsGranted = isGranted, IsEnabled = true, CreationTime = DateTime.UtcNow,
        });
        await DbContext.SaveChangesAsync();
    }

    private (FunctionAuthorizationService Roles, IUserFunctionService Users) GetServices()
    {
        var scope = ServiceProvider.CreateScope().ServiceProvider;
        return (scope.GetRequiredService<FunctionAuthorizationService>(), scope.GetRequiredService<IUserFunctionService>());
    }

    private List<Guid> RoleFunctionIdsInDb(Guid roleId)
        => DbContext.RoleFunctions.AsNoTracking().Where(rf => rf.RoleId == roleId).Select(rf => rf.FunctionId).ToList();

    private List<(Guid FunctionId, bool IsGranted)> UserRowsInDb(Guid userId)
        => DbContext.UserFunctions.AsNoTracking().Where(uf => uf.UserId == userId).Select(uf => new { uf.FunctionId, uf.IsGranted })
            .AsEnumerable().Select(r => (r.FunctionId, r.IsGranted)).ToList();

    #region 角色侧

    [Fact]
    public async Task Role_function_ids_exclude_retired_and_disabled_grants()
    {
        var (live, retired, disabled, _) = await SeedCatalogueAsync();
        await SeedRoleGrantAsync(RoleId, live.Id);
        await SeedRoleGrantAsync(RoleId, retired.Id);
        await SeedRoleGrantAsync(RoleId, disabled.Id);
        var (roles, _) = GetServices();

        var ids = (await roles.GetRoleFunctionIdsAsync(RoleId)).Data!.ToList();

        ids.ShouldBe(new[] { live.Id });
    }

    /// <summary>★P1-1 的现形用例：矩阵页读什么就保存什么，这一步此前必然 404。</summary>
    [Fact]
    public async Task Saving_the_role_matrix_round_trip_succeeds_and_keeps_the_dormant_grant()
    {
        var (live, retired, _, _) = await SeedCatalogueAsync();
        await SeedRoleGrantAsync(RoleId, live.Id);
        await SeedRoleGrantAsync(RoleId, retired.Id);
        MakeGrantorSuperAdmin();
        var (roles, _) = GetServices();

        var ids = (await roles.GetRoleFunctionIdsAsync(RoleId)).Data!.ToList();
        var saved = await roles.SetRoleFunctionsAsync(RoleId, ids);

        saved.Succeeded.ShouldBeTrue(saved.Message);
        // 退役行原样还在 —— 模块回来时授权照常恢复，这正是 Disable 模式存在的理由。
        RoleFunctionIdsInDb(RoleId).ShouldBe(new[] { live.Id, retired.Id }, ignoreOrder: true);
    }

    [Fact]
    public async Task Replacing_the_live_set_leaves_the_dormant_grant_alone()
    {
        var (live, retired, disabled, otherLive) = await SeedCatalogueAsync();
        await SeedRoleGrantAsync(RoleId, live.Id);
        await SeedRoleGrantAsync(RoleId, retired.Id);
        await SeedRoleGrantAsync(RoleId, disabled.Id);
        MakeGrantorSuperAdmin();
        var (roles, _) = GetServices();

        (await roles.SetRoleFunctionsAsync(RoleId, [otherLive.Id])).Succeeded.ShouldBeTrue();

        RoleFunctionIdsInDb(RoleId).ShouldBe(new[] { otherLive.Id, retired.Id, disabled.Id }, ignoreOrder: true);
        (await roles.GetRoleFunctionIdsAsync(RoleId)).Data!.ShouldBe(new[] { otherLive.Id });
    }

    [Fact]
    public async Task Setting_an_empty_live_set_keeps_the_dormant_grant()
    {
        var (live, retired, _, _) = await SeedCatalogueAsync();
        await SeedRoleGrantAsync(RoleId, live.Id);
        await SeedRoleGrantAsync(RoleId, retired.Id);
        MakeGrantorSuperAdmin();
        var (roles, _) = GetServices();

        (await roles.SetRoleFunctionsAsync(RoleId, [])).Succeeded.ShouldBeTrue();

        RoleFunctionIdsInDb(RoleId).ShouldBe(new[] { retired.Id });
    }

    [Fact]
    public async Task Granting_a_retired_function_to_a_role_is_rejected_by_code()
    {
        var (live, retired, disabled, _) = await SeedCatalogueAsync();
        MakeGrantorSuperAdmin();
        var (roles, _) = GetServices();

        var set = await roles.SetRoleFunctionsAsync(RoleId, [live.Id, retired.Id]);
        set.Succeeded.ShouldBeFalse();
        set.Code.ShouldBe(404);
        // 文案要给出码而不是一串 guid，管理员才知道该去哪里修。
        set.Message!.ShouldContain("demo.gone.view");

        var assign = await roles.AssignFunctionsToRoleAsync(RoleId, [disabled.Id]);
        assign.Succeeded.ShouldBeFalse();
        assign.Code.ShouldBe(404);
        assign.Message!.ShouldContain("demo.paused.view");

        RoleFunctionIdsInDb(RoleId).ShouldBeEmpty();
    }

    [Fact]
    public async Task Clearing_a_role_explicitly_removes_dormant_grants_too()
    {
        var (live, retired, _, _) = await SeedCatalogueAsync();
        await SeedRoleGrantAsync(RoleId, live.Id);
        await SeedRoleGrantAsync(RoleId, retired.Id);
        MakeGrantorSuperAdmin();
        var (roles, _) = GetServices();

        (await roles.ClearRoleFunctionsAsync(RoleId)).Succeeded.ShouldBeTrue();

        RoleFunctionIdsInDb(RoleId).ShouldBeEmpty();
    }

    [Fact]
    public async Task Cloning_copies_only_live_grants()
    {
        var (live, retired, _, _) = await SeedCatalogueAsync();
        var sourceRoleId = Guid.NewGuid();
        await SeedRoleGrantAsync(sourceRoleId, live.Id);
        await SeedRoleGrantAsync(sourceRoleId, retired.Id);
        MakeGrantorSuperAdmin();
        var (roles, _) = GetServices();

        var cloned = await roles.CloneRoleFunctionsAsync(sourceRoleId, RoleId);

        cloned.Succeeded.ShouldBeTrue();
        cloned.Data.ShouldBe(1);
        RoleFunctionIdsInDb(RoleId).ShouldBe(new[] { live.Id });
    }

    [Fact]
    public async Task Importing_a_retired_code_reports_it_as_not_found_instead_of_writing_a_dormant_grant()
    {
        var (live, _, _, _) = await SeedCatalogueAsync();
        MakeGrantorSuperAdmin();
        var (roles, _) = GetServices();

        var imported = await roles.ImportRolePermissionsAsync(RoleId, new RolePermissionExportDto
        {
            FunctionCodes = ["demo.thing.view", "demo.gone.view"],
        });

        imported.Succeeded.ShouldBeTrue();
        imported.Data!.Imported.ShouldBe(1);
        imported.Data.NotFound.ShouldBe(new[] { "demo.gone.view" });
        RoleFunctionIdsInDb(RoleId).ShouldBe(new[] { live.Id });
    }

    #endregion

    #region 用户侧

    /// <summary>★P1-2 的现形用例：此前 200、行落库、读回可见，而权限解析永远答 false。</summary>
    [Fact]
    public async Task Assigning_a_retired_function_directly_to_a_user_is_rejected()
    {
        var (_, retired, _, _) = await SeedCatalogueAsync();
        MakeGrantorSuperAdmin();
        var (_, users) = GetServices();

        var result = await users.AssignFunctionsToUserAsync(TargetUserId, [retired.Id]);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
        result.Message!.ShouldContain("demo.gone.view");
        UserRowsInDb(TargetUserId).ShouldBeEmpty();
    }

    [Fact]
    public async Task Every_user_write_path_rejects_a_retired_function()
    {
        var (live, retired, _, _) = await SeedCatalogueAsync();
        MakeGrantorSuperAdmin();
        var (_, users) = GetServices();
        var scope = new[] { live.Id, retired.Id };

        (await users.SetUserFunctionsAsync(TargetUserId, [retired.Id])).Code.ShouldBe(404);
        (await users.SetUserDeniedFunctionsAsync(TargetUserId, [retired.Id])).Code.ShouldBe(404);
        (await users.SetUserFunctionsInScopeAsync(TargetUserId, scope, [retired.Id])).Code.ShouldBe(404);
        (await users.SetUserDeniedFunctionsInScopeAsync(TargetUserId, scope, [retired.Id])).Code.ShouldBe(404);

        UserRowsInDb(TargetUserId).ShouldBeEmpty();
    }

    [Fact]
    public async Task User_reads_exclude_dormant_rows_of_both_polarities()
    {
        var (live, retired, disabled, otherLive) = await SeedCatalogueAsync();
        await SeedUserRowAsync(TargetUserId, live.Id);
        await SeedUserRowAsync(TargetUserId, retired.Id);
        await SeedUserRowAsync(TargetUserId, otherLive.Id, isGranted: false);
        await SeedUserRowAsync(TargetUserId, disabled.Id, isGranted: false);
        var (_, users) = GetServices();

        (await users.GetUserFunctionIdsAsync(TargetUserId)).Data!.ShouldBe(new[] { live.Id });
        (await users.GetUserDeniedFunctionIdsAsync(TargetUserId)).Data!.ShouldBe(new[] { otherLive.Id });
        (await users.GetUserFunctionsAsync(TargetUserId)).Data!.Select(f => f.Id).ShouldBe(new[] { live.Id });
    }

    [Fact]
    public async Task Saving_the_user_drawer_round_trip_keeps_dormant_rows_of_both_polarities()
    {
        var (live, retired, disabled, otherLive) = await SeedCatalogueAsync();
        await SeedUserRowAsync(TargetUserId, live.Id);
        await SeedUserRowAsync(TargetUserId, retired.Id);
        await SeedUserRowAsync(TargetUserId, disabled.Id, isGranted: false);
        MakeGrantorSuperAdmin();
        var (_, users) = GetServices();

        // 抽屉的保存顺序：先 deny 后 allow，各自拿读回来的集合原样写回。
        var allow = (await users.GetUserFunctionIdsAsync(TargetUserId)).Data!.ToList();
        var deny = (await users.GetUserDeniedFunctionIdsAsync(TargetUserId)).Data!.ToList();
        (await users.SetUserDeniedFunctionsAsync(TargetUserId, deny)).Succeeded.ShouldBeTrue();
        (await users.SetUserFunctionsAsync(TargetUserId, allow)).Succeeded.ShouldBeTrue();

        UserRowsInDb(TargetUserId).ShouldBe(
            new[] { (live.Id, true), (retired.Id, true), (disabled.Id, false) }, ignoreOrder: true);

        // 换一个生效集，休眠行依旧不动。
        (await users.SetUserFunctionsAsync(TargetUserId, [otherLive.Id])).Succeeded.ShouldBeTrue();
        UserRowsInDb(TargetUserId).ShouldBe(
            new[] { (otherLive.Id, true), (retired.Id, true), (disabled.Id, false) }, ignoreOrder: true);
    }

    [Fact]
    public async Task Scoped_writes_do_not_touch_a_dormant_row_even_when_the_scope_names_it()
    {
        var (live, retired, _, otherLive) = await SeedCatalogueAsync();
        await SeedUserRowAsync(TargetUserId, live.Id);
        await SeedUserRowAsync(TargetUserId, retired.Id);
        MakeGrantorSuperAdmin();
        var (_, users) = GetServices();

        // 消费方把整块切片（含退役 id）都报进来了 —— 切片声明的是「我看得见的范围」，
        // 而退役码在任何矩阵里都不可见，所以它不能被这次写入删掉。
        var scope = new[] { live.Id, retired.Id, otherLive.Id };
        (await users.SetUserFunctionsInScopeAsync(TargetUserId, scope, [otherLive.Id])).Succeeded.ShouldBeTrue();

        UserRowsInDb(TargetUserId).ShouldBe(new[] { (otherLive.Id, true), (retired.Id, true) }, ignoreOrder: true);
    }

    /// <summary>
    /// 支配约束只看生效的码：目标用户身上一条退役直授不该把每个非超管授权者都锁在外面
    /// （退役码谁的有效集里都没有，永远「不被包含」）。
    /// </summary>
    [Fact]
    public async Task A_dormant_direct_grant_does_not_lock_regular_grantors_out_of_managing_the_user()
    {
        var (live, retired, _, otherLive) = await SeedCatalogueAsync();
        await MakeGrantorRegularWithAsync(live, otherLive);
        await SeedUserRowAsync(TargetUserId, retired.Id);
        var (_, users) = GetServices();

        var result = await users.AssignFunctionsToUserAsync(TargetUserId, [live.Id]);

        result.Succeeded.ShouldBeTrue(result.Message);
        UserRowsInDb(TargetUserId).ShouldBe(new[] { (retired.Id, true), (live.Id, true) }, ignoreOrder: true);
    }

    #endregion
}
