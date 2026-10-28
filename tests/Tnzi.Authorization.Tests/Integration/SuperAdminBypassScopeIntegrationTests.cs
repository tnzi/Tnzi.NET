using Tnzi.Authorization.Options;
using AuthOptions = Tnzi.Authorization.Options.AuthorizationOptions;
using IdentityRole = Tnzi.Identity.Entities.Role;
using IdentityUser = Tnzi.Identity.Entities.User;
using IdentityUserRole = Tnzi.Identity.Entities.UserRole;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.Authorization.Tests.Integration;

/// <summary>
/// <c>Authorization:SuperAdminBypassScope</c>：真实 SQLite 仓储与真实 <c>UserRoleService</c>。
/// 守的是两件事：①默认 <see cref="SuperAdminBypassScope.All"/> 与没有策略时逐字是今天的行为（超管通过一切码，含未声明的码）；
/// ②收窄到 <see cref="SuperAdminBypassScope.Platform"/> 时，单查、批量、名单过滤、有效集、访问画像、持有者反查、权限全景
/// 七个读面对每一个（账号, 码）给出同一个答案：超管只对平台码走旁路，应用码与未声明的码只认显式授权（否定行照常扣除）。
/// </summary>
public class SuperAdminBypassScopeIntegrationTests : IntegratedTestBase<AuthorizationTestDbContext>
{
    private const string PlatformCode = "user.view";
    private const string AppView = "app.matter.view";
    private const string AppUpdate = "app.matter.update";
    private const string Undeclared = "legacy.report.view";

    private static readonly string[] AllCodes = [PlatformCode, AppView, AppUpdate, Undeclared];

    private static readonly Guid RootRole = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid OwnerRole = Guid.Parse("30000000-0000-0000-0000-000000000002");

    private static readonly Guid Erin = Guid.Parse("40000000-0000-0000-0000-00000000000e");   // 超管
    private static readonly Guid Frank = Guid.Parse("40000000-0000-0000-0000-00000000000f");  // 超管 + Owner
    private static readonly Guid Gina = Guid.Parse("40000000-0000-0000-0000-000000000010");   // 超管 + Owner + 否定 AppView + 否定 PlatformCode
    private static readonly Guid Owen = Guid.Parse("40000000-0000-0000-0000-000000000011");   // Owner
    private static readonly Guid Pat = Guid.Parse("40000000-0000-0000-0000-000000000012");    // 超管 + 直授 AppUpdate

    private static readonly Guid[] Everyone = [Erin, Frank, Gina, Owen, Pat];

    private readonly AuthOptions _authOptions = new() { SuperAdminRoles = ["Root"] };

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

    // ── 声明：一个平台组（框架程序集默认）、一个显式声明为应用的组 ─────────────────

    private sealed class PlatformProvider : IPermissionDefinitionProvider
    {
        public void Define(IPermissionDefinitionContext context)
        {
            context.AddGroup("identity", "Identity");
            context.AddPermission(PlatformCode, "View Users", parentName: "identity");
        }
    }

    private sealed class AppProvider : IPermissionDefinitionProvider
    {
        public void Define(IPermissionDefinitionContext context)
        {
            context.AddGroup("app", "App").Scope = PermissionScope.Application;
            context.AddPermission(AppView, "View Matters", parentName: "app");
            context.AddPermission(AppUpdate, "Edit Matters", parentName: AppView);
        }
    }

    private static PermissionScopeRegistry Registry(IReadOnlyDictionary<string, PermissionScope>? overrides = null)
        => PermissionScopeRegistry.Build([new PlatformProvider(), new AppProvider()], overrides);

    private static SuperAdminBypassPolicy Platform(IReadOnlyDictionary<string, PermissionScope>? overrides = null)
        => SuperAdminBypassPolicy.Create(SuperAdminBypassScope.Platform, Registry(overrides));

    private static SuperAdminBypassPolicy All() => SuperAdminBypassPolicy.Create(SuperAdminBypassScope.All, Registry());

    private FunctionAuthorizationService CreateService(SuperAdminBypassPolicy? policy)
    {
        var sp = ServiceProvider.CreateScope().ServiceProvider;
        return new FunctionAuthorizationService(
            sp.GetRequiredService<IRepository<FunctionModule, Guid>>(),
            sp.GetRequiredService<IRepository<ModuleFunction, Guid>>(),
            sp.GetRequiredService<IRepository<RoleFunction, Guid>>(),
            sp.GetRequiredService<IRepository<UserFunction, Guid>>(),
            sp,
            sp.GetRequiredService<IUserRoleService>(),
            functionAuthCache: null,
            options: sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthOptions>>(),
            roleRepository: sp.GetRequiredService<IRepository<IdentityRole, Guid>>(),
            userRepository: sp.GetRequiredService<IRepository<IdentityUser, Guid>>(),
            userScope: UnrestrictedScope(),
            bypassPolicy: policy);
    }

    private UserFunctionService CreatePictureService(SuperAdminBypassPolicy? policy)
    {
        var sp = ServiceProvider.CreateScope().ServiceProvider;
        return new UserFunctionService(
            sp,
            sp.GetRequiredService<IRepository<UserFunction, Guid>>(),
            sp.GetRequiredService<IRepository<ModuleFunction, Guid>>(),
            CreateService(policy),
            functionAuthCache: null,
            roleFunctionRepository: sp.GetRequiredService<IRepository<RoleFunction, Guid>>(),
            userRoleService: sp.GetRequiredService<IUserRoleService>(),
            roleRepository: sp.GetRequiredService<IRepository<IdentityRole, Guid>>(),
            bypassPolicy: policy);
    }

    private static IUserTenantScopeProvider UnrestrictedScope()
    {
        var scope = new Mock<IUserTenantScopeProvider>();
        scope.Setup(s => s.FilterAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) => ids.Distinct().ToList());
        scope.Setup(s => s.ContainsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return scope.Object;
    }

    // ── 种子 ────────────────────────────────────────────────────────────────

    private async Task SeedAsync()
    {
        var identity = NewModule("Identity", "identity", 0);
        var app = NewModule("App", "app", 1);
        var legacy = NewModule("Legacy", "legacy", 2); // 后台手建，没有任何 provider 声明
        await DbContext.FunctionModules.AddRangeAsync(identity, app, legacy);

        var userView = NewFunction(identity.Id, PlatformCode);
        var matterView = NewFunction(app.Id, AppView);
        var matterUpdate = NewFunction(app.Id, AppUpdate);
        var legacyReport = NewFunction(legacy.Id, Undeclared);
        await DbContext.ModuleFunctions.AddRangeAsync(userView, matterView, matterUpdate, legacyReport);

        await DbContext.IdentityRoles.AddRangeAsync(NewRole(RootRole, "Root"), NewRole(OwnerRole, "Owner"));
        await DbContext.IdentityUsers.AddRangeAsync(Everyone.Select(NewUser));

        Member(Erin, RootRole);
        Member(Frank, RootRole);
        Member(Frank, OwnerRole);
        Member(Gina, RootRole);
        Member(Gina, OwnerRole);
        Member(Owen, OwnerRole);
        Member(Pat, RootRole);

        Grant(OwnerRole, matterView);
        Direct(Gina, matterView, isGranted: false);
        Direct(Gina, userView, isGranted: false);
        Direct(Pat, matterUpdate, isGranted: true);

        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private static FunctionModule NewModule(string name, string code, int order) => new()
    {
        Id = Guid.NewGuid(), Name = name, Code = code, Order = order, IsEnabled = true, IsSystemManaged = code != "legacy", CreationTime = DateTime.UtcNow,
    };

    private static ModuleFunction NewFunction(Guid moduleId, string code) => new()
    {
        Id = Guid.NewGuid(), Name = code, Code = code, ModuleId = moduleId, IsEnabled = true, CreationTime = DateTime.UtcNow,
    };

    private static IdentityRole NewRole(Guid id, string name) => new()
    {
        Id = id, Name = name, NormalizedName = name.ToUpperInvariant(), CreationTime = DateTime.UtcNow,
    };

    private static IdentityUser NewUser(Guid id) => new()
    {
        Id = id, UserName = "u" + id.ToString("N")[^4..], NormalizedUserName = ("u" + id.ToString("N")[^4..]).ToUpperInvariant(), CreationTime = DateTime.UtcNow,
    };

    private void Member(Guid userId, Guid roleId)
        => DbContext.IdentityUserRoles.Add(new IdentityUserRole { UserId = userId, RoleId = roleId });

    private void Grant(Guid roleId, ModuleFunction function)
        => DbContext.RoleFunctions.Add(new RoleFunction
        {
            Id = Guid.NewGuid(), RoleId = roleId, FunctionId = function.Id, IsEnabled = true, CreationTime = DateTime.UtcNow,
        });

    private void Direct(Guid userId, ModuleFunction function, bool isGranted)
        => DbContext.UserFunctions.Add(new UserFunction
        {
            Id = Guid.NewGuid(), UserId = userId, FunctionId = function.Id, IsGranted = isGranted, IsEnabled = true, CreationTime = DateTime.UtcNow,
        });

    /// <summary>收窄模式下的期望答案：平台码对超管恒真（否定行无效），其余码只看显式授权。</summary>
    private static readonly Dictionary<(Guid User, string Code), bool> PlatformExpectations = new()
    {
        [(Erin, PlatformCode)] = true, [(Erin, AppView)] = false, [(Erin, AppUpdate)] = false, [(Erin, Undeclared)] = false,
        [(Frank, PlatformCode)] = true, [(Frank, AppView)] = true, [(Frank, AppUpdate)] = false, [(Frank, Undeclared)] = false,
        [(Gina, PlatformCode)] = true, [(Gina, AppView)] = false, [(Gina, AppUpdate)] = false, [(Gina, Undeclared)] = false,
        [(Owen, PlatformCode)] = false, [(Owen, AppView)] = true, [(Owen, AppUpdate)] = false, [(Owen, Undeclared)] = false,
        [(Pat, PlatformCode)] = true, [(Pat, AppView)] = false, [(Pat, AppUpdate)] = true, [(Pat, Undeclared)] = false,
    };

    // ── 默认模式：今天的行为 ──────────────────────────────────────────────────

    [Fact]
    public async Task DefaultScope_SuperAdminPassesEveryCode_SameAsWithoutAPolicy()
    {
        await SeedAsync();

        foreach (var policy in new[] { null, All() })
        {
            var service = CreateService(policy);
            foreach (var code in AllCodes)
            {
                (await service.CheckPermissionAsync(Erin, code)).ShouldBeTrue($"{code} with policy {policy?.Scope}");
                (await service.CheckPermissionAsync(Gina, code)).ShouldBeTrue($"deny rows do not touch a super admin ({code})");
            }
            (await service.CheckPermissionsAsync(Erin, AllCodes)).Values.ShouldAllBe(v => v);
            (await service.FilterGrantedAsync(Everyone, AppView)).ShouldBe([Erin, Frank, Gina, Owen, Pat], ignoreOrder: true);
            (await service.GetUserPermissionNamesAsync(Erin)).ShouldBe(AllCodes, ignoreOrder: true);

            var profile = (await service.GetAccessProfileAsync(Erin)).Data!;
            profile.IsSuperAdmin.ShouldBeTrue();
            profile.Permissions.ShouldBe(AllCodes, ignoreOrder: true);
            profile.SuperAdminBypassScope.ShouldBeNull("the default mode does not add the field to the wire");

            (await service.GetPermissionCatalogueAsync()).Data!.ShouldAllBe(m => m.Scope == null);
        }
    }

    // ── 收窄模式：七个读面逐人逐码一致 ─────────────────────────────────────────

    [Fact]
    public async Task PlatformScope_SingleCheck_FollowsTheExpectationTable()
    {
        await SeedAsync();
        var service = CreateService(Platform());

        foreach (var ((user, code), expected) in PlatformExpectations)
        {
            (await service.CheckPermissionAsync(user, code)).ShouldBe(expected, $"user {user} code {code}");
        }
    }

    [Fact]
    public async Task PlatformScope_BatchFilterAndEffectiveSet_AgreeWithTheSingleCheck()
    {
        await SeedAsync();
        var service = CreateService(Platform());

        foreach (var user in Everyone)
        {
            var batch = await service.CheckPermissionsAsync(user, AllCodes);
            var names = (await service.GetUserPermissionNamesAsync(user)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var profile = (await service.GetAccessProfileAsync(user)).Data!;
            foreach (var code in AllCodes)
            {
                var expected = PlatformExpectations[(user, code)];
                batch[code].ShouldBe(expected, $"batch: user {user} code {code}");
                names.Contains(code).ShouldBe(expected, $"effective set: user {user} code {code}");
                profile.Permissions.Contains(code, StringComparer.OrdinalIgnoreCase).ShouldBe(expected, $"access profile: user {user} code {code}");
            }
        }

        foreach (var code in AllCodes)
        {
            var granted = await service.FilterGrantedAsync(Everyone, code);
            granted.ShouldBe(Everyone.Where(u => PlatformExpectations[(u, code)]), ignoreOrder: true, $"filter: code {code}");
        }
    }

    [Fact]
    public async Task PlatformScope_AccessProfile_StillReportsTheSuperAdminAndCarriesTheScope()
    {
        await SeedAsync();
        var profile = (await CreateService(Platform()).GetAccessProfileAsync(Erin)).Data!;

        profile.IsSuperAdmin.ShouldBeTrue();
        profile.SuperAdminBypassScope.ShouldBe(SuperAdminBypassScope.Platform);
        profile.Permissions.ShouldBe([PlatformCode]);
    }

    [Fact]
    public async Task PlatformScope_Holders_ListOnlyExplicitHoldersForAnApplicationCode()
    {
        await SeedAsync();
        var service = CreateService(Platform());

        foreach (var code in AllCodes)
        {
            var dto = (await service.GetPermissionHoldersAsync(code)).Data!;
            dto.Users.Select(u => u.Id)
                .ShouldBe(Everyone.Where(u => PlatformExpectations[(u, code)]), ignoreOrder: true, $"holders: code {code}");
        }

        var app = (await service.GetPermissionHoldersAsync(AppView)).Data!;
        app.Roles.ShouldNotContain(r => r.State == PermissionRoleGrantState.SuperAdmin);
        app.Users.ShouldAllBe(u => !u.IsSuperAdmin);
        app.DeniedUsers.Select(u => u.Id).ShouldBe([Gina], "a deny row applies to a super admin on an application code");

        var platform = (await service.GetPermissionHoldersAsync(PlatformCode)).Data!;
        platform.Roles.ShouldContain(r => r.Id == RootRole && r.State == PermissionRoleGrantState.SuperAdmin);
        platform.DeniedUsers.ShouldBeEmpty("a deny row does not touch a super admin on a platform code");
    }

    [Fact]
    public async Task PlatformScope_Picture_EffectiveMatchesTheRuntimeCheck()
    {
        await SeedAsync();
        var check = CreateService(Platform());
        var pictures = CreatePictureService(Platform());

        foreach (var user in Everyone)
        {
            var picture = (await pictures.GetUserPermissionPictureAsync(user)).Data!;
            foreach (var code in AllCodes)
            {
                picture.Effective.Contains(code).ShouldBe(await check.CheckPermissionAsync(user, code), $"picture: user {user} code {code}");
            }
        }
    }

    [Fact]
    public async Task PlatformScope_Catalogue_StampsGroupScopes_AdminCreatedModulesAreApplication()
    {
        await SeedAsync();
        var catalogue = (await CreateService(Platform()).GetPermissionCatalogueAsync()).Data!;

        catalogue.Single(m => m.Code == "identity").Scope.ShouldBe(PermissionScope.Platform);
        catalogue.Single(m => m.Code == "app").Scope.ShouldBe(PermissionScope.Application);
        catalogue.Single(m => m.Code == "legacy").Scope.ShouldBe(PermissionScope.Application);
    }

    [Fact]
    public async Task PlatformScope_DeploymentOverride_CanHandAnApplicationGroupBackToTheBypass()
    {
        await SeedAsync();
        var service = CreateService(Platform(new Dictionary<string, PermissionScope> { ["APP"] = PermissionScope.Platform }));

        (await service.CheckPermissionAsync(Erin, AppView)).ShouldBeTrue();
        (await service.CheckPermissionAsync(Erin, AppUpdate)).ShouldBeTrue("the nested code follows its group");
        (await service.CheckPermissionAsync(Erin, Undeclared)).ShouldBeFalse("undeclared codes stay outside the bypass");
    }

    [Fact]
    public async Task PlatformScope_RoleDelegation_IsNotNarrowed()
    {
        await SeedAsync();
        var service = CreateService(Platform());

        (await service.CanManageRoleAsync(Erin, OwnerRole)).ShouldBeTrue("super admins still design business roles");
        (await service.CanManageRoleMembershipAsync(Erin, OwnerRole)).ShouldBeTrue();
        (await service.IsSuperAdminAsync(Erin)).ShouldBeTrue("identity questions are not narrowed");
    }
}
