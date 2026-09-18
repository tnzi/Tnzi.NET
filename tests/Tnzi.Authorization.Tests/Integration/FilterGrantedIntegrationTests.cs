using AuthOptions = Tnzi.Authorization.Options.AuthorizationOptions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Tnzi.Authorization.Tests.Integration;

/// <summary>
/// <see cref="FunctionAuthorizationService.FilterGrantedAsync"/>：一次解析 N 个用户对某个码的授予，
/// 结果必须与逐个 <see cref="FunctionAuthorizationService.CheckPermissionAsync"/> 逐用户一致
/// （超管旁路 / 角色授权 / 用户直授 / 用户级 deny / 禁用功能 / 大小写）。
///
/// 真实 SQLite 仓储 + 真实服务，只 mock 角色成员关系（<see cref="IUserRoleService"/>）。
/// </summary>
public class FilterGrantedIntegrationTests : IntegratedTestBase<AuthorizationTestDbContext>
{
    private static readonly Guid GrantingRoleId = Guid.NewGuid();
    private static readonly Guid OtherRoleId = Guid.NewGuid();

    private static readonly Guid SuperAdmin = Guid.NewGuid();
    private static readonly Guid ViaRole = Guid.NewGuid();
    private static readonly Guid ViaUserGrant = Guid.NewGuid();
    private static readonly Guid RoleButDenied = Guid.NewGuid();
    private static readonly Guid OtherRoleOnly = Guid.NewGuid();
    private static readonly Guid NoRoles = Guid.NewGuid();

    private readonly Mock<IUserRoleService> _userRoleService = new();
    private readonly AuthOptions _authOptions = new() { SuperAdminRoles = ["SuperAdmin"] };

    private readonly Dictionary<Guid, (string[] Names, Guid[] Ids)> _roles = new()
    {
        [SuperAdmin] = (["SuperAdmin"], [OtherRoleId]),
        [ViaRole] = (["Staff"], [GrantingRoleId]),
        [ViaUserGrant] = ([], []),
        [RoleButDenied] = (["Staff"], [GrantingRoleId]),
        [OtherRoleOnly] = (["Guest"], [OtherRoleId]),
        [NoRoles] = ([], []),
    };

    protected override void ConfigureServices(IServiceCollection services)
    {
        AddRepo<FunctionModule>(services);
        AddRepo<ModuleFunction>(services);
        AddRepo<RoleFunction>(services);
        AddRepo<UserFunction>(services);

        _userRoleService
            .Setup(s => s.GetUserRolesAsync(It.IsAny<IEnumerable<Guid>>()))
            .ReturnsAsync((IEnumerable<Guid> ids) => ids
                .Where(_roles.ContainsKey)
                .ToDictionary(id => id, id => _roles[id].Names.AsEnumerable()));
        _userRoleService
            .Setup(s => s.GetUserRoleIdsAsync(It.IsAny<Guid>()))
            .ReturnsAsync((Guid id) => _roles.TryGetValue(id, out var r) ? r.Ids : []);
        _userRoleService
            .Setup(s => s.GetUserRoleIdsAsync(It.IsAny<IEnumerable<Guid>>()))
            .ReturnsAsync((IEnumerable<Guid> ids) => (IDictionary<Guid, IEnumerable<Guid>>)ids
                .Where(_roles.ContainsKey)
                .ToDictionary(id => id, id => _roles[id].Ids.AsEnumerable()));

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
            options: sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthOptions>>()));
    }

    private static void AddRepo<TEntity>(IServiceCollection services) where TEntity : class, Tnzi.Domain.Entities.IEntity<Guid>
    {
        services.AddScoped<IRepository<TEntity, Guid>>(sp =>
            new EFCoreRepository<AuthorizationTestDbContext, TEntity, Guid>(
                sp.GetRequiredService<AuthorizationTestDbContext>(), serviceProvider: sp));
    }

    private FunctionAuthorizationService GetService()
        => ServiceProvider.CreateScope().ServiceProvider.GetRequiredService<FunctionAuthorizationService>();

    private async Task<ModuleFunction> SeedAsync(bool enabled = true)
    {
        var module = new FunctionModule { Id = Guid.NewGuid(), Name = "Chat", Code = "chat", CreationTime = DateTime.UtcNow };
        await DbContext.FunctionModules.AddAsync(module);
        var fn = new ModuleFunction
        {
            Id = Guid.NewGuid(), Name = "Use chat", Code = "chat.use",
            ModuleId = module.Id, IsEnabled = enabled, Category = PermissionCategory.Business,
            CreationTime = DateTime.UtcNow,
        };
        await DbContext.ModuleFunctions.AddAsync(fn);
        await DbContext.RoleFunctions.AddAsync(new RoleFunction
        {
            Id = Guid.NewGuid(), RoleId = GrantingRoleId, FunctionId = fn.Id, IsEnabled = true, CreationTime = DateTime.UtcNow,
        });
        await DbContext.UserFunctions.AddAsync(new UserFunction
        {
            Id = Guid.NewGuid(), UserId = ViaUserGrant, FunctionId = fn.Id, IsEnabled = true, IsGranted = true, CreationTime = DateTime.UtcNow,
        });
        await DbContext.UserFunctions.AddAsync(new UserFunction
        {
            Id = Guid.NewGuid(), UserId = RoleButDenied, FunctionId = fn.Id, IsEnabled = true, IsGranted = false, CreationTime = DateTime.UtcNow,
        });
        await DbContext.SaveChangesAsync();
        return fn;
    }

    private Guid[] Everyone => _roles.Keys.ToArray();

    [Fact]
    public async Task FilterGranted_MatchesPerUserCheck()
    {
        await SeedAsync();
        var service = GetService();

        var batch = await service.FilterGrantedAsync(Everyone, "chat.use");

        var expected = new HashSet<Guid>();
        foreach (var id in Everyone)
        {
            if (await service.CheckPermissionAsync(id, "chat.use")) expected.Add(id);
        }

        batch.ShouldBe(expected);
        // 逐条锁定各条路径的方向，免得「两边都错」时对账照样相等。
        batch.ShouldContain(SuperAdmin);
        batch.ShouldContain(ViaRole);
        batch.ShouldContain(ViaUserGrant);
        batch.ShouldNotContain(RoleButDenied);
        batch.ShouldNotContain(OtherRoleOnly);
        batch.ShouldNotContain(NoRoles);
    }

    [Fact]
    public async Task FilterGranted_IsCaseInsensitive_LikeTheSingleCheck()
    {
        await SeedAsync();
        var service = GetService();

        var batch = await service.FilterGrantedAsync([ViaRole, NoRoles], "CHAT.USE");

        batch.ShouldBe(new HashSet<Guid> { ViaRole });
    }

    [Fact]
    public async Task FilterGranted_DisabledFunction_GrantsNobodyButSuperAdmins()
    {
        await SeedAsync(enabled: false);
        var service = GetService();

        var batch = await service.FilterGrantedAsync(Everyone, "chat.use");

        batch.ShouldBe(new HashSet<Guid> { SuperAdmin });
    }

    [Fact]
    public async Task FilterGranted_UsesBatchRoleLookups_NotOnePerUser()
    {
        await SeedAsync();
        var service = GetService();

        await service.FilterGrantedAsync(Everyone, "chat.use");

        _userRoleService.Verify(s => s.GetUserRoleIdsAsync(It.IsAny<Guid>()), Times.Never);
        _userRoleService.Verify(s => s.GetUserRolesAsync(It.IsAny<IEnumerable<Guid>>()), Times.Once);
        _userRoleService.Verify(s => s.GetUserRoleIdsAsync(It.IsAny<IEnumerable<Guid>>()), Times.Once);
    }

    [Fact]
    public async Task FilterGranted_EmptyInput_ReturnsEmpty()
    {
        await SeedAsync();
        var service = GetService();

        (await service.FilterGrantedAsync([], "chat.use")).ShouldBeEmpty();
        (await service.FilterGrantedAsync([Guid.Empty], "chat.use")).ShouldBeEmpty();
        (await service.FilterGrantedAsync([ViaRole], "")).ShouldBeEmpty();
    }
}
