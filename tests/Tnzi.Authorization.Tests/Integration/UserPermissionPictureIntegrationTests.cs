using AuthOptions = Tnzi.Authorization.Options.AuthorizationOptions;
using IdentityRole = Tnzi.Identity.Entities.Role;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.Authorization.Tests.Integration;

/// <summary>
/// 权限全景（<c>GET admin/user-functions/user/{id}/picture</c> 背后的
/// <see cref="IUserFunctionService.GetUserPermissionPictureAsync"/>）：真实 SQLite 仓储，
/// mock 角色成员关系。守的是「目录 / 按角色的基线 / 覆盖 / 生效集」四段彼此一致，
/// 且生效集与运行时检查 <see cref="FunctionAuthorizationService.CheckPermissionAsync"/> 同一个答案。
/// </summary>
public class UserPermissionPictureIntegrationTests : IntegratedTestBase<AuthorizationTestDbContext>
{
    private static readonly Guid TargetUserId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid StaffRoleId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid AuditorRoleId = Guid.Parse("88888888-8888-8888-8888-888888888888");

    private readonly Mock<IUserRoleService> _userRoleService = new();
    private readonly AuthOptions _authOptions = new();
    private readonly Dictionary<Guid, string[]> _rolesByUser = new();
    private readonly Dictionary<Guid, Guid[]> _roleIdsByUser = new();

    public UserPermissionPictureIntegrationTests()
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
            functionAuthCache: null,
            roleFunctionRepository: sp.GetRequiredService<IRepository<RoleFunction, Guid>>(),
            userRoleService: sp.GetRequiredService<IUserRoleService>(),
            roleRepository: sp.GetRequiredService<IRepository<IdentityRole, Guid>>()));
    }

    private static void AddRepo<TEntity>(IServiceCollection services) where TEntity : class, Tnzi.Domain.Entities.IEntity<Guid>
    {
        services.AddScoped<IRepository<TEntity, Guid>>(sp =>
            new EFCoreRepository<AuthorizationTestDbContext, TEntity, Guid>(
                sp.GetRequiredService<AuthorizationTestDbContext>(), serviceProvider: sp));
    }

    private sealed record Catalogue(ModuleFunction UserView, ModuleFunction UserSecurity, ModuleFunction CatalogView, ModuleFunction Retired);

    /// <summary>两个模块、四个码：三个生效（identity 两个、catalog 一个）+ 一个退役的。</summary>
    private async Task<Catalogue> SeedCatalogueAsync()
    {
        var identity = new FunctionModule { Id = Guid.NewGuid(), Name = "Identity", Code = "identity", Order = 1, CreationTime = DateTime.UtcNow };
        var catalog = new FunctionModule { Id = Guid.NewGuid(), Name = "Catalog", Code = "catalog", Order = 2, CreationTime = DateTime.UtcNow };
        await DbContext.FunctionModules.AddRangeAsync(identity, catalog);

        var userView = NewFunction(identity.Id, "View Users", "user.view", order: 1);
        var userSecurity = NewFunction(identity.Id, "Manage Sign-in Security", "user.security", order: 2);
        var catalogView = NewFunction(catalog.Id, "View Catalog", "catalog.product.view", order: 1);
        var retired = NewFunction(catalog.Id, "Old", "catalog.legacy.view", order: 2);
        retired.IsRetired = true;
        await DbContext.ModuleFunctions.AddRangeAsync(userView, userSecurity, catalogView, retired);

        await DbContext.IdentityRoles.AddRangeAsync(
            new IdentityRole { Id = StaffRoleId, Name = "Staff", NormalizedName = "STAFF", CreationTime = DateTime.UtcNow },
            new IdentityRole { Id = AuditorRoleId, Name = "Auditor", NormalizedName = "AUDITOR", CreationTime = DateTime.UtcNow });
        await DbContext.SaveChangesAsync();
        return new Catalogue(userView, userSecurity, catalogView, retired);
    }

    private static ModuleFunction NewFunction(Guid moduleId, string name, string code, int order) => new()
    {
        Id = Guid.NewGuid(), Name = name, Code = code, ModuleId = moduleId, Order = order, IsEnabled = true, CreationTime = DateTime.UtcNow,
    };

    private async Task GrantRoleAsync(Guid roleId, params ModuleFunction[] functions)
    {
        foreach (var fn in functions)
        {
            await DbContext.RoleFunctions.AddAsync(new RoleFunction
            {
                Id = Guid.NewGuid(), RoleId = roleId, FunctionId = fn.Id, IsEnabled = true, CreationTime = DateTime.UtcNow,
            });
        }
        await DbContext.SaveChangesAsync();
    }

    private async Task OverrideAsync(Guid functionId, bool isGranted)
    {
        await DbContext.UserFunctions.AddAsync(new UserFunction
        {
            Id = Guid.NewGuid(), UserId = TargetUserId, FunctionId = functionId, IsGranted = isGranted, IsEnabled = true, CreationTime = DateTime.UtcNow,
        });
        await DbContext.SaveChangesAsync();
    }

    private (IUserFunctionService UserFunctions, FunctionAuthorizationService Auth) GetServices()
    {
        var scope = ServiceProvider.CreateScope().ServiceProvider;
        return (scope.GetRequiredService<IUserFunctionService>(), scope.GetRequiredService<FunctionAuthorizationService>());
    }

    [Fact]
    public async Task Picture_ShowsRoleBaselinePerRole_OverridesAndTheResolvedEffectiveSet()
    {
        var c = await SeedCatalogueAsync();
        _roleIdsByUser[TargetUserId] = [StaffRoleId, AuditorRoleId];
        await GrantRoleAsync(StaffRoleId, c.UserView, c.CatalogView);
        await GrantRoleAsync(AuditorRoleId, c.UserView);
        await OverrideAsync(c.UserSecurity.Id, isGranted: true);   // 单独授予
        await OverrideAsync(c.CatalogView.Id, isGranted: false);   // 角色给了，单独否定
        var (service, auth) = GetServices();

        var result = await service.GetUserPermissionPictureAsync(TargetUserId);

        result.Succeeded.ShouldBeTrue();
        var picture = result.Data!;
        picture.IsSuperAdmin.ShouldBeFalse();
        picture.Scope.ShouldBeNull();

        // 目录：只有生效的三个，退役的不在；按模块序再功能序。
        picture.Catalogue.Select(i => i.Code).ShouldBe(["user.view", "user.security", "catalog.product.view"]);
        picture.Catalogue.First().ModuleCode.ShouldBe("identity");
        picture.Catalogue.Last().ModuleName.ShouldBe("Catalog");

        // 角色基线按角色分开给，名字来自角色表。
        picture.Roles.Select(r => r.Name).ShouldBe(["Staff", "Auditor"]);
        picture.Roles[0].Granted.ShouldBe(["catalog.product.view", "user.view"]);
        picture.Roles[1].Granted.ShouldBe(["user.view"]);
        picture.RoleGranted.ShouldBe(["catalog.product.view", "user.view"]);

        picture.Allowed.ShouldBe(["user.security"]);
        picture.Denied.ShouldBe(["catalog.product.view"]);

        // 生效 = (角色 ∪ 允许) − 拒绝，且与运行时检查逐码一致。
        picture.Effective.ShouldBe(["user.security", "user.view"]);
        foreach (var item in picture.Catalogue)
        {
            (await auth.CheckPermissionAsync(TargetUserId, item.Code)).ShouldBe(picture.Effective.Contains(item.Code), item.Code);
        }
    }

    /// <summary>
    /// 不存在的账号、别家租户的账号一律 404：前者否则被画成「一个什么权限都没有的账号」，
    /// 后者否则把别家租户那个人的角色 id 与生效集交给了本租户的管理员（用户-角色关联表不按租户过滤）。
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Picture_ForAnUnknownOrOutOfScopeUser_IsNotFound(bool exists, bool inScope)
    {
        var c = await SeedCatalogueAsync();
        _roleIdsByUser[TargetUserId] = [StaffRoleId];
        await GrantRoleAsync(StaffRoleId, c.UserView);
        var sp = ServiceProvider.CreateScope().ServiceProvider;

        var users = new Mock<IRepository<Tnzi.Identity.Entities.User, Guid>>();
        var rows = exists
            ? new List<Tnzi.Identity.Entities.User> { new() { Id = TargetUserId, UserName = "target" } }
            : [];
        users.Setup(r => r.AsQueryable(It.IsAny<bool>())).Returns(() => rows.BuildMock());
        var userScope = new Mock<IUserTenantScopeProvider>();
        userScope.Setup(s => s.ContainsAsync(TargetUserId, It.IsAny<CancellationToken>())).ReturnsAsync(inScope);

        var service = new UserFunctionService(
            sp,
            sp.GetRequiredService<IRepository<UserFunction, Guid>>(),
            sp.GetRequiredService<IRepository<ModuleFunction, Guid>>(),
            sp.GetRequiredService<FunctionAuthorizationService>(),
            functionAuthCache: null,
            roleFunctionRepository: sp.GetRequiredService<IRepository<RoleFunction, Guid>>(),
            userRoleService: sp.GetRequiredService<IUserRoleService>(),
            roleRepository: sp.GetRequiredService<IRepository<IdentityRole, Guid>>(),
            userRepository: users.Object,
            userScope: userScope.Object);

        var result = await service.GetUserPermissionPictureAsync(TargetUserId);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(404);
    }

    [Fact]
    public async Task Picture_WithScope_NarrowsEverySectionToThePrefix()
    {
        var c = await SeedCatalogueAsync();
        _roleIdsByUser[TargetUserId] = [StaffRoleId];
        await GrantRoleAsync(StaffRoleId, c.UserView, c.CatalogView);
        await OverrideAsync(c.UserSecurity.Id, isGranted: true);
        var (service, _) = GetServices();

        var picture = (await service.GetUserPermissionPictureAsync(TargetUserId, "catalog.")).Data!;

        picture.Scope.ShouldBe("catalog.");
        picture.Catalogue.Select(i => i.Code).ShouldBe(["catalog.product.view"]);
        picture.Roles.Single().Granted.ShouldBe(["catalog.product.view"]);
        picture.RoleGranted.ShouldBe(["catalog.product.view"]);
        picture.Allowed.ShouldBeEmpty();
        picture.Effective.ShouldBe(["catalog.product.view"]);
    }

    /// <summary>超管的生效集是整个目录，覆盖行对他没有效果；界面据此改成说明而不是矩阵。</summary>
    [Fact]
    public async Task Picture_ForASuperAdmin_EffectiveIsTheWholeCatalogueRegardlessOfDenies()
    {
        var c = await SeedCatalogueAsync();
        _authOptions.SuperAdminRoles.Add("Root");
        _rolesByUser[TargetUserId] = ["Root"];
        await OverrideAsync(c.UserView.Id, isGranted: false);
        var (service, _) = GetServices();

        var picture = (await service.GetUserPermissionPictureAsync(TargetUserId)).Data!;

        picture.IsSuperAdmin.ShouldBeTrue();
        picture.Denied.ShouldBe(["user.view"]);
        picture.Effective.ShouldBe(["catalog.product.view", "user.security", "user.view"]);
    }

    [Fact]
    public async Task Picture_ForAnAccountWithNoRolesAndNoOverrides_IsEmptyButWellFormed()
    {
        await SeedCatalogueAsync();
        var (service, _) = GetServices();

        var picture = (await service.GetUserPermissionPictureAsync(TargetUserId)).Data!;

        picture.Catalogue.Count.ShouldBe(3);
        picture.Roles.ShouldBeEmpty();
        picture.RoleGranted.ShouldBeEmpty();
        picture.Allowed.ShouldBeEmpty();
        picture.Denied.ShouldBeEmpty();
        picture.Effective.ShouldBeEmpty();
    }
}
