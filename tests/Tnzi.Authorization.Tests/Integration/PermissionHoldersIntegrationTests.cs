using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using AuthOptions = Tnzi.Authorization.Options.AuthorizationOptions;
using IdentityRole = Tnzi.Identity.Entities.Role;
using IdentityUser = Tnzi.Identity.Entities.User;
using IdentityUserRole = Tnzi.Identity.Entities.UserRole;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.Authorization.Tests.Integration;

/// <summary>
/// 持有者反查（<c>GET admin/function-authorization/permission/holders</c> 背后的
/// <see cref="FunctionAuthorizationService.GetPermissionHoldersAsync"/>）：真实 SQLite 仓储与真实
/// <c>UserRoleService</c>。守的是「名单与运行时检查逐人一致」：报出来的持有者恰好是
/// <see cref="FunctionAuthorizationService.CheckPermissionAsync"/> 答 true 的那些账号，
/// 覆盖角色授权、行停用、用户直授、用户否定、超管（含被否定的超管）、功能停用与退役。
/// </summary>
public class PermissionHoldersIntegrationTests : IntegratedTestBase<AuthorizationTestDbContext>
{
    private const string Code = "catalog.product.export";

    private static readonly Guid RootRole = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid StaffRole = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid AuditorRole = Guid.Parse("10000000-0000-0000-0000-000000000003");
    private static readonly Guid ViewerRole = Guid.Parse("10000000-0000-0000-0000-000000000004");
    private static readonly Guid OperatorRole = Guid.Parse("10000000-0000-0000-0000-000000000005"); // 只授 user.view，调用者的角色

    /// <summary>调用者（测试基类的当前用户）。默认经 Operator 持有 user.view，所以名单带用户名。</summary>
    private static readonly Guid Caller = TestHelper.DefaultTestUserId;

    private static readonly Guid Alice = Guid.Parse("20000000-0000-0000-0000-00000000000a"); // Staff
    private static readonly Guid Bob = Guid.Parse("20000000-0000-0000-0000-00000000000b"); // Staff + 否定
    private static readonly Guid Carol = Guid.Parse("20000000-0000-0000-0000-00000000000c"); // 直授
    private static readonly Guid Dave = Guid.Parse("20000000-0000-0000-0000-00000000000d"); // Auditor（授权行停用）
    private static readonly Guid Erin = Guid.Parse("20000000-0000-0000-0000-00000000000e"); // 超管
    private static readonly Guid Frank = Guid.Parse("20000000-0000-0000-0000-00000000000f"); // 超管 + 否定
    private static readonly Guid Grace = Guid.Parse("20000000-0000-0000-0000-000000000010"); // Viewer（不授予这个码）
    private static readonly Guid Heidi = Guid.Parse("20000000-0000-0000-0000-000000000011"); // Staff + 直授
    private static readonly Guid Judy = Guid.Parse("20000000-0000-0000-0000-000000000012"); // 直授行停用
    private static readonly Guid Mallory = Guid.Parse("20000000-0000-0000-0000-000000000013"); // 无角色无授权

    private static readonly Guid[] Everyone = [Alice, Bob, Carol, Dave, Erin, Frank, Grace, Heidi, Judy, Mallory];

    // 静态：拦截器实例是 EF 内部服务提供者缓存键的一部分，每个测试实例一个新拦截器会让缓存一路涨下去。
    // 同一个测试类里的用例顺序执行，取差值即可。
    private static readonly CommandCounter Commands = new();

    private readonly AuthOptions _authOptions = new() { SuperAdminRoles = ["Root"] };

    protected override void ConfigureDbContextOptions(DbContextOptionsBuilder options)
        => options.AddInterceptors(Commands);

    protected override void ConfigureServices(IServiceCollection services)
    {
        AddRepo<FunctionModule>(services);
        AddRepo<ModuleFunction>(services);
        AddRepo<RoleFunction>(services);
        AddRepo<UserFunction>(services);
        AddRepo<IdentityRole>(services);
        AddRepo<IdentityUser>(services);

        services.AddScoped<IUserRoleService>(sp =>
            new Tnzi.Identity.Services.UserRoleService(sp.GetRequiredService<AuthorizationTestDbContext>(), sp));
        services.AddScoped(_ => MsOptions.Create(_authOptions));
    }

    private static void AddRepo<TEntity>(IServiceCollection services) where TEntity : class, Tnzi.Domain.Entities.IEntity<Guid>
    {
        services.AddScoped<IRepository<TEntity, Guid>>(sp =>
            new EFCoreRepository<AuthorizationTestDbContext, TEntity, Guid>(
                sp.GetRequiredService<AuthorizationTestDbContext>(), serviceProvider: sp));
    }

    private FunctionAuthorizationService CreateService(
        IUserTenantScopeProvider? userScope = null,
        bool withUserRepository = true,
        bool withUserScope = true,
        bool withRoleRepository = true,
        IUserRoleService? userRoleService = null,
        bool withUserRoleService = true)
    {
        var sp = ServiceProvider.CreateScope().ServiceProvider;
        return new FunctionAuthorizationService(
            sp.GetRequiredService<IRepository<FunctionModule, Guid>>(),
            sp.GetRequiredService<IRepository<ModuleFunction, Guid>>(),
            sp.GetRequiredService<IRepository<RoleFunction, Guid>>(),
            sp.GetRequiredService<IRepository<UserFunction, Guid>>(),
            sp,
            withUserRoleService ? userRoleService ?? sp.GetRequiredService<IUserRoleService>() : null,
            functionAuthCache: null,
            options: sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthOptions>>(),
            roleRepository: withRoleRepository ? sp.GetRequiredService<IRepository<IdentityRole, Guid>>() : null,
            userRepository: withUserRepository ? sp.GetRequiredService<IRepository<IdentityUser, Guid>>() : null,
            userScope: withUserScope ? userScope ?? UnrestrictedScope() : null);
    }

    /// <summary>单租户部署的范围：不裁剪任何人。</summary>
    private static IUserTenantScopeProvider UnrestrictedScope()
    {
        var scope = new Mock<IUserTenantScopeProvider>();
        scope.Setup(s => s.FilterAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) => ids.Distinct().ToList());
        return scope.Object;
    }

    // ── 种子 ────────────────────────────────────────────────────────────────

    private sealed record Seed(ModuleFunction Target, ModuleFunction Other);

    /// <summary>
    /// 一个目标码 + 一个无关码；四个角色（超管 Root、授予的 Staff、授权行停用的 Auditor、只授予无关码的 Viewer）；
    /// 十个账号覆盖每一条持有路径与每一种「看起来像持有、其实不持有」的情形。
    /// 另有调用者自己的 Operator 角色，只授 <c>user.view</c>：<paramref name="callerCanViewUsers"/> 为 false 时调用者不在其中。
    /// </summary>
    private async Task<Seed> SeedAsync(bool callerCanViewUsers = true)
    {
        var module = new FunctionModule { Id = Guid.NewGuid(), Name = "Catalog", Code = "catalog", Order = 1, IsEnabled = true, CreationTime = DateTime.UtcNow };
        await DbContext.FunctionModules.AddAsync(module);
        var target = NewFunction(module.Id, "Export Products", Code);
        var other = NewFunction(module.Id, "View Products", "catalog.product.view");
        var identity = new FunctionModule { Id = Guid.NewGuid(), Name = "Identity", Code = "identity", Order = 0, IsEnabled = true, CreationTime = DateTime.UtcNow };
        await DbContext.FunctionModules.AddAsync(identity);
        var userView = NewFunction(identity.Id, "View Users", "user.view");
        await DbContext.ModuleFunctions.AddRangeAsync(target, other, userView);

        await DbContext.IdentityRoles.AddRangeAsync(
            NewRole(RootRole, "Root"), NewRole(StaffRole, "Staff"), NewRole(AuditorRole, "Auditor"), NewRole(ViewerRole, "Viewer"),
            NewRole(OperatorRole, "Operator"));
        Grant(OperatorRole, userView, enabled: true);
        if (callerCanViewUsers)
        {
            Member(Caller, OperatorRole);
        }
        await DbContext.IdentityUsers.AddRangeAsync(Everyone.Select(NewUser));

        Member(Alice, StaffRole);
        Member(Bob, StaffRole);
        Member(Dave, AuditorRole);
        Member(Erin, RootRole);
        Member(Frank, RootRole);
        Member(Grace, ViewerRole);
        Member(Heidi, StaffRole);

        Grant(StaffRole, target, enabled: true);
        Grant(AuditorRole, target, enabled: false);
        Grant(ViewerRole, other, enabled: true);

        Direct(Carol, target, isGranted: true);
        Direct(Heidi, target, isGranted: true);
        Direct(Judy, target, isGranted: true, enabled: false);
        Direct(Bob, target, isGranted: false);
        Direct(Frank, target, isGranted: false);

        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return new Seed(target, other);
    }

    private static ModuleFunction NewFunction(Guid moduleId, string name, string code) => new()
    {
        Id = Guid.NewGuid(), Name = name, Code = code, ModuleId = moduleId, IsEnabled = true, CreationTime = DateTime.UtcNow,
    };

    private static IdentityRole NewRole(Guid id, string name) => new()
    {
        Id = id, Name = name, NormalizedName = name.ToUpperInvariant(), CreationTime = DateTime.UtcNow,
    };

    private static IdentityUser NewUser(Guid id) => new()
    {
        Id = id, UserName = UserNameOf(id), NormalizedUserName = UserNameOf(id).ToUpperInvariant(), CreationTime = DateTime.UtcNow,
    };

    private static string UserNameOf(Guid id) => id switch
    {
        _ when id == Alice => "alice",
        _ when id == Bob => "bob",
        _ when id == Carol => "carol",
        _ when id == Dave => "dave",
        _ when id == Erin => "erin",
        _ when id == Frank => "frank",
        _ when id == Grace => "grace",
        _ when id == Heidi => "heidi",
        _ when id == Judy => "judy",
        _ when id == Mallory => "mallory",
        _ => "user-" + id.ToString("N")[..8],
    };

    private void Member(Guid userId, Guid roleId)
        => DbContext.IdentityUserRoles.Add(new IdentityUserRole { UserId = userId, RoleId = roleId });

    private void Grant(Guid roleId, ModuleFunction function, bool enabled)
        => DbContext.RoleFunctions.Add(new RoleFunction
        {
            Id = Guid.NewGuid(), RoleId = roleId, FunctionId = function.Id, IsEnabled = enabled, CreationTime = DateTime.UtcNow,
        });

    private void Direct(Guid userId, ModuleFunction function, bool isGranted, bool enabled = true)
        => DbContext.UserFunctions.Add(new UserFunction
        {
            Id = Guid.NewGuid(), UserId = userId, FunctionId = function.Id, IsGranted = isGranted, IsEnabled = enabled, CreationTime = DateTime.UtcNow,
        });

    private async Task SetFunctionStateAsync(Guid functionId, bool isEnabled, bool isRetired)
    {
        var function = await DbContext.ModuleFunctions.SingleAsync(f => f.Id == functionId);
        function.IsEnabled = isEnabled;
        function.IsRetired = isRetired;
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private async Task<HashSet<Guid>> ResolvedHoldersAsync(FunctionAuthorizationService service, IEnumerable<Guid> universe)
    {
        var holders = new HashSet<Guid>();
        foreach (var userId in universe)
        {
            if (await service.CheckPermissionAsync(userId, Code)) holders.Add(userId);
        }

        return holders;
    }

    // ── 与运行时检查一致 ────────────────────────────────────────────────────

    /// <summary>
    /// 核心契约：功能生效、停用、退役三种状态下，报出来的持有者都恰好是运行时检查答 true 的那些账号。
    /// 不生效时两边都只剩超管：超管在检查最前短路，连功能是否生效都不看。
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Holders_AreExactlyTheAccountsTheRuntimeCheckGrants(bool isEnabled, bool isRetired)
    {
        var seed = await SeedAsync();
        await SetFunctionStateAsync(seed.Target.Id, isEnabled, isRetired);
        var service = CreateService();

        var result = await service.GetPermissionHoldersAsync(Code);

        result.Succeeded.ShouldBeTrue(result.Message);
        var reported = result.Data!.Users.Select(u => u.Id).ToHashSet();
        var resolved = await ResolvedHoldersAsync(service, Everyone);
        reported.ShouldBe(resolved, ignoreOrder: true);

        // 锚点：一致性不是因为两边都空。
        var live = isEnabled && !isRetired;
        Guid[] expected = live ? [Alice, Carol, Erin, Frank, Heidi] : [Erin, Frank];
        resolved.ShouldBe(expected, ignoreOrder: true);
        result.Data.IsInEffect.ShouldBe(live);
    }

    [Fact]
    public async Task EachHolder_SaysHowTheyHoldIt_WithRoleNames()
    {
        await SeedAsync();
        var service = CreateService();

        var dto = (await service.GetPermissionHoldersAsync(Code)).Data!;

        dto.Code.ShouldBe(Code);
        dto.Name.ShouldBe("Export Products");
        dto.UserNamesHidden.ShouldBeFalse();
        dto.Users.Select(u => u.UserName).ShouldBe(["alice", "carol", "erin", "frank", "heidi"]);

        var byName = dto.Users.ToDictionary(u => u.UserName);
        byName["alice"].ShouldSatisfyAllConditions(
            u => u.IsSuperAdmin.ShouldBeFalse(),
            u => u.IsDirect.ShouldBeFalse(),
            u => u.Roles.Select(r => r.Name).ShouldBe(["Staff"]));
        byName["carol"].ShouldSatisfyAllConditions(
            u => u.IsDirect.ShouldBeTrue(),
            u => u.Roles.ShouldBeEmpty());
        byName["heidi"].ShouldSatisfyAllConditions(
            u => u.IsDirect.ShouldBeTrue(),
            u => u.Roles.Select(r => r.Name).ShouldBe(["Staff"]));
        byName["erin"].ShouldSatisfyAllConditions(
            u => u.IsSuperAdmin.ShouldBeTrue(),
            u => u.Roles.Select(r => r.Name).ShouldBe(["Root"]));
        // 超管的否定行不起作用：他仍然持有，也不出现在被否定名单里。
        byName["frank"].IsSuperAdmin.ShouldBeTrue();
        dto.DeniedUsers.Select(u => u.UserName).ShouldBe(["bob"]);
    }

    /// <summary>
    /// 角色视图：超管角色在前且标为隐式持有；授权行停用的角色标出来而不是当作授予；
    /// 只授予别的码的角色不出现。成员数按账号计。
    /// </summary>
    [Fact]
    public async Task Roles_ShowSuperAdminRoles_AndFlagGrantsThatDoNotApply()
    {
        await SeedAsync();
        var service = CreateService();

        var roles = (await service.GetPermissionHoldersAsync(Code)).Data!.Roles;

        roles.Select(r => (r.Name, r.State, r.MemberCount)).ShouldBe(
        [
            ("Root", PermissionRoleGrantState.SuperAdmin, 2),
            ("Staff", PermissionRoleGrantState.Effective, 3),
            ("Auditor", PermissionRoleGrantState.GrantDisabled, 1),
        ]);
    }

    /// <summary>
    /// 功能停用 / 退役时授权行是休眠的：标为 Dormant 而不是藏起来：功能恢复时它们原样回来，
    /// 藏起来等于让「重新启用一个码」成为一次没人审过的授权。
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Roles_ForADormantFunction_FlagTheGrantsAsDormant(bool isEnabled, bool isRetired)
    {
        var seed = await SeedAsync();
        await SetFunctionStateAsync(seed.Target.Id, isEnabled, isRetired);
        var service = CreateService();

        var dto = (await service.GetPermissionHoldersAsync(Code)).Data!;

        dto.IsInEffect.ShouldBeFalse();
        dto.IsEnabled.ShouldBe(isEnabled);
        dto.IsRetired.ShouldBe(isRetired);
        dto.Roles.Select(r => (r.Name, r.State)).ShouldBe(
        [
            ("Root", PermissionRoleGrantState.SuperAdmin),
            ("Staff", PermissionRoleGrantState.Dormant),
            ("Auditor", PermissionRoleGrantState.GrantDisabled),
        ]);
        dto.DeniedUsers.ShouldBeEmpty();
    }

    // ── 名单的边界 ──────────────────────────────────────────────────────────

    /// <summary>用户表不按租户过滤：范围外的账号既不进名单，也不计入成员数。</summary>
    [Fact]
    public async Task Holders_AreNarrowedToTheCallersTenantScope()
    {
        await SeedAsync();
        var scope = new Mock<IUserTenantScopeProvider>();
        scope.Setup(s => s.FilterAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                ids.Where(id => id != Alice && id != Erin && id != Bob).ToList());
        var service = CreateService(scope.Object);

        var dto = (await service.GetPermissionHoldersAsync(Code)).Data!;

        dto.Users.Select(u => u.UserName).ShouldBe(["carol", "frank", "heidi"]);
        dto.DeniedUsers.ShouldBeEmpty();
        dto.Roles.Single(r => r.Name == "Root").MemberCount.ShouldBe(1);
        dto.Roles.Single(r => r.Name == "Staff").MemberCount.ShouldBe(1);
    }

    /// <summary>已删除的账号登不进来，不是持有者；它残留的角色成员关系也不计数。</summary>
    [Fact]
    public async Task SoftDeletedAccounts_AreNotHolders()
    {
        await SeedAsync();
        var alice = await DbContext.IdentityUsers.SingleAsync(u => u.Id == Alice);
        alice.IsDeleted = true;
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        var service = CreateService();

        var dto = (await service.GetPermissionHoldersAsync(Code)).Data!;

        dto.Users.Select(u => u.UserName).ShouldNotContain("alice");
        dto.Roles.Single(r => r.Name == "Staff").MemberCount.ShouldBe(2);
    }

    [Fact]
    public async Task Lookup_IsCaseInsensitive_LikeTheRuntimeCheck()
    {
        await SeedAsync();
        var service = CreateService();

        var result = await service.GetPermissionHoldersAsync("  CATALOG.Product.EXPORT ");

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Code.ShouldBe(Code);
        result.Data.Users.Count.ShouldBe(5);
    }

    [Fact]
    public async Task UnknownCode_Is404()
    {
        await SeedAsync();
        var service = CreateService();

        var result = await service.GetPermissionHoldersAsync("catalog.product.nope");

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    /// <summary>
    /// 超管角色按名字认，不区分大小写，包括非 ASCII：配置写 <c>Général</c>、库里存 <c>général</c>。
    /// SQL 的 upper() 在 SQLite 上只折叠 ASCII（<c>upper('général') = 'GéNéRAL'</c>），交给它预筛就会漏掉这个角色，
    /// 而 <c>IsSuperAdminAsync</c> 在内存里比较、照样认他为超管。
    /// </summary>
    [Fact]
    public async Task SuperAdminRole_IsRecognisedByName_WhateverTheCase_IncludingNonAscii()
    {
        await SeedAsync();
        var general = Guid.Parse("10000000-0000-0000-0000-000000000006");
        await DbContext.IdentityRoles.AddAsync(NewRole(general, "général"));
        Member(Grace, general);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        _authOptions.SuperAdminRoles.Add("Général");
        var service = CreateService();

        var dto = (await service.GetPermissionHoldersAsync(Code)).Data!;

        (await service.IsSuperAdminAsync(Grace)).ShouldBeTrue();
        var grace = dto.Users.Single(u => u.Id == Grace);
        grace.IsSuperAdmin.ShouldBeTrue();
        grace.Roles.Select(r => r.Name).ShouldBe(["général"]);
        dto.Roles.Single(r => r.Id == general).State.ShouldBe(PermissionRoleGrantState.SuperAdmin);
        dto.Users.Select(u => u.Id).ToHashSet().ShouldBe(await ResolvedHoldersAsync(service, Everyone), ignoreOrder: true);
    }

    // ── 用户名只给看得了账号的人 ────────────────────────────────────────────

    /// <summary>
    /// 端点的门是 authorization.permission.view；成批的账号名（含每一个超管账号）属于用户管理面，要 user.view。
    /// 没有它时名单照样完整，只是只有 id。
    /// </summary>
    [Fact]
    public async Task WithoutUserView_TheListIsComplete_ButCarriesIdsOnly()
    {
        await SeedAsync(callerCanViewUsers: false);
        var service = CreateService();

        var dto = (await service.GetPermissionHoldersAsync(Code)).Data!;

        dto.UserNamesHidden.ShouldBeTrue();
        dto.Users.Select(u => u.Id).ShouldBe([Alice, Carol, Erin, Frank, Heidi], ignoreOrder: true);
        dto.Users.ShouldAllBe(u => u.UserName == string.Empty);
        dto.DeniedUsers.Select(u => u.Id).ShouldBe([Bob]);
        dto.DeniedUsers.ShouldAllBe(u => u.UserName == string.Empty);
        // 持有路径与角色视图不受影响。
        dto.Users.Single(u => u.Id == Erin).IsSuperAdmin.ShouldBeTrue();
        dto.Users.Single(u => u.Id == Alice).Roles.Select(r => r.Name).ShouldBe(["Staff"]);
        dto.Roles.Single(r => r.Name == "Staff").MemberCount.ShouldBe(3);
    }

    [Fact]
    public async Task ASuperAdminCaller_SeesUserNames_WithoutAnExplicitUserViewGrant()
    {
        await SeedAsync(callerCanViewUsers: false);
        Member(Caller, RootRole);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        var service = CreateService();

        var dto = (await service.GetPermissionHoldersAsync(Code)).Data!;

        dto.UserNamesHidden.ShouldBeFalse();
        dto.Users.Single(u => u.Id == Alice).UserName.ShouldBe("alice");
        dto.DeniedUsers.Single().UserName.ShouldBe("bob");
    }

    // ── 依赖缺席 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 三个依赖各缺一个都答 501：缺角色成员关系或角色表会漏掉超管与全部经角色持有的人，
    /// 缺租户范围会把别家租户的账号一起列出来。不退化成一份看起来完整的名单。
    /// 501 而不是 503：缺席是永久性的（宿主没装那一块），不是暂时故障。
    /// </summary>
    [Theory]
    [InlineData("userRoleService")]
    [InlineData("roleRepository")]
    [InlineData("userScope")]
    public async Task AMissingIdentityDependency_Is501(string missing)
    {
        await SeedAsync();
        var service = CreateService(
            withUserRoleService: missing != "userRoleService",
            withRoleRepository: missing != "roleRepository",
            withUserScope: missing != "userScope");

        var result = await service.GetPermissionHoldersAsync(Code);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(501);
    }

    /// <summary>自己实现 <see cref="IUserRoleService"/> 的宿主可能对批量成员查询答 null：当作「没有成员」，不抛空引用。</summary>
    [Fact]
    public async Task AMembershipServiceAnsweringNull_IsTreatedAsNoMembers()
    {
        await SeedAsync();
        var membership = new Mock<IUserRoleService>();
        membership.Setup(s => s.GetRoleUserIdsAsync(It.IsAny<IEnumerable<Guid>>()))
            .ReturnsAsync((IDictionary<Guid, IEnumerable<Guid>>)null!);
        membership.Setup(s => s.GetUserRolesAsync(It.IsAny<IEnumerable<Guid>>()))
            .ReturnsAsync(new Dictionary<Guid, IEnumerable<string>>());
        membership.Setup(s => s.GetUserRoleIdsAsync(It.IsAny<Guid>())).ReturnsAsync(Array.Empty<Guid>());
        var service = CreateService(userRoleService: membership.Object);

        var result = await service.GetPermissionHoldersAsync(Code);

        result.Succeeded.ShouldBeTrue(result.Message);
        result.Data!.Users.Select(u => u.Id).ShouldBe([Carol, Heidi], ignoreOrder: true);
    }

    [Fact]
    public async Task WithoutTheUserRepository_HoldersStillResolve_WithEmptyNames()
    {
        await SeedAsync();
        var service = CreateService(withUserRepository: false);

        var dto = (await service.GetPermissionHoldersAsync(Code)).Data!;

        dto.Users.Select(u => u.Id).ShouldBe([Alice, Carol, Erin, Frank, Heidi], ignoreOrder: true);
        dto.Users.ShouldAllBe(u => u.UserName == string.Empty);
    }

    // ── 批量，不逐行 ────────────────────────────────────────────────────────

    /// <summary>
    /// 查询次数与角色数、人数无关：再加五个授予角色、每个两名成员，外加五个直授账号，SQL 条数一条不多。
    /// 逐角色取成员、逐人取名字的实现在这里会随规模线性增长。
    /// </summary>
    [Fact]
    public async Task QueryCount_DoesNotGrowWithRolesOrHolders()
    {
        var seed = await SeedAsync();
        var small = await CountQueriesAsync();

        for (var i = 0; i < 5; i++)
        {
            var roleId = Guid.NewGuid();
            await DbContext.IdentityRoles.AddAsync(NewRole(roleId, $"Extra{i}"));
            Grant(roleId, seed.Target, enabled: true);
            for (var j = 0; j < 2; j++)
            {
                var member = Guid.NewGuid();
                await DbContext.IdentityUsers.AddAsync(NewUser(member));
                Member(member, roleId);
            }

            var direct = Guid.NewGuid();
            await DbContext.IdentityUsers.AddAsync(NewUser(direct));
            Direct(direct, seed.Target, isGranted: true);
        }
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var large = await CountQueriesAsync();

        large.Holders.ShouldBe(small.Holders + 15);
        large.Queries.ShouldBe(small.Queries);
    }

    private async Task<(int Holders, int Queries)> CountQueriesAsync()
    {
        var service = CreateService();
        var before = Commands.Count;
        var dto = (await service.GetPermissionHoldersAsync(Code)).Data!;
        return (dto.Users.Count, Commands.Count - before);
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Interlocked.Increment(ref _count);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _count);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
