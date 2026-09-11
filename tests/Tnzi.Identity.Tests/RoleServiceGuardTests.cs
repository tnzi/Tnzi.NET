using Tnzi.Security.Authorization;

namespace Tnzi.Identity.Tests;

/// <summary>
/// 角色<b>定义</b>写路径的护栏：受保护角色名 + 委托支配。
/// </summary>
/// <remarks>
/// <para>
/// ★★★ 守的是一条顺序提权：成员变更早有委托护栏（<c>CanManageRoleAsync</c>），
/// 但它读的是角色<b>当时的名字</b>。于是把顺序倒过来就能绕开 ——
/// ①建一个零权限的普通角色（空集被任何人包含，护栏平凡通过）
/// ②把自己加进去（同上）
/// ③把它<b>改名</b>成配置里的超管角色名（改名此前没有任何守卫）。
/// 第三步之后 <c>IsSuperAdminAsync</c>（按名字匹配、无缓存）立刻为真。
/// </para>
/// <para>
/// 此前唯一挡住这条路的是数据库的角色名唯一约束（出厂配置会播种出那个角色，改名撞重名）——
/// 那是一次巧合的兜底：把 <c>SeedBuiltInAdminRoles</c> 关掉、或者配一个应用自己不会创建的名字，
/// 兜底就没了。安全边界不该押在一个恰好存在的唯一索引上。
/// </para>
/// </remarks>
public class RoleServiceGuardTests
{
    private const string SuperAdminRoleName = "SuperAdmin";

    private readonly Mock<RoleManager<Role>> _roleManagerMock;
    private readonly Mock<IFunctionAuthorizationService> _authMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly RoleService _service;
    private readonly Guid _actorId = Guid.NewGuid();

    public RoleServiceGuardTests()
    {
        // 成功路径会把实体映射成 DTO，需要一个已初始化的 mapper。
        var mapperConfig = new TypeAdapterConfig();
        MapperExtensions.SetMapper(new Mapper(mapperConfig));

        var roleStore = new Mock<IRoleStore<Role>>();
        _roleManagerMock = new Mock<RoleManager<Role>>(roleStore.Object, null!, null!, null!, null!);
        _roleManagerMock.Setup(x => x.UpdateAsync(It.IsAny<Role>())).ReturnsAsync(IdentityResult.Success);
        _roleManagerMock.Setup(x => x.CreateAsync(It.IsAny<Role>())).ReturnsAsync(IdentityResult.Success);
        _roleManagerMock.Setup(x => x.DeleteAsync(It.IsAny<Role>())).ReturnsAsync(IdentityResult.Success);
        _roleManagerMock.Setup(x => x.RoleExistsAsync(It.IsAny<string>())).ReturnsAsync(false);

        _authMock.Setup(x => x.GetSuperAdminRoleNames()).Returns([SuperAdminRoleName]);
        _authMock.Setup(x => x.IsSuperAdminAsync(It.IsAny<Guid>())).ReturnsAsync(false);
        _authMock.Setup(x => x.CanManageRoleAsync(It.IsAny<Guid>(), It.IsAny<Guid>())).ReturnsAsync(true);

        _currentUserMock.Setup(x => x.Id).Returns(_actorId);

        var serviceProvider = new Mock<IServiceProvider>();
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        serviceProvider.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);
        serviceProvider.Setup(x => x.GetService(typeof(ICurrentUser))).Returns(_currentUserMock.Object);

        _service = new RoleService(
            _roleManagerMock.Object,
            new Mock<DbContext>().Object,
            serviceProvider.Object,
            _authMock.Object);
    }

    private Role ArrangeExistingRole(string name, bool isSystem = false)
    {
        var role = new Role { Id = Guid.NewGuid(), Name = name, IsSystem = isSystem };
        _roleManagerMock.Setup(x => x.FindByIdAsync(role.Id.ToString())).ReturnsAsync(role);
        return role;
    }

    /// <summary>★★★ 把一个普通角色改名成超管角色名 → 403，且不落库。</summary>
    [Fact]
    public async Task Update_RenamingIntoASuperAdminName_IsRejected()
    {
        var role = ArrangeExistingRole("Support");

        var result = await _service.UpdateAsync(role.Id, new UpdateRoleDto { Name = SuperAdminRoleName });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        _roleManagerMock.Verify(x => x.UpdateAsync(It.IsAny<Role>()), Times.Never);
    }

    /// <summary>对照组：改成一个普通名字照常成功 —— 否则上面那条恒真。</summary>
    [Fact]
    public async Task Update_RenamingIntoAnOrdinaryName_Succeeds()
    {
        var role = ArrangeExistingRole("Support");

        var result = await _service.UpdateAsync(role.Id, new UpdateRoleDto { Name = "Helpdesk" });

        result.Succeeded.ShouldBeTrue();
        _roleManagerMock.Verify(x => x.UpdateAsync(role), Times.Once);
    }

    /// <summary>
    /// 超管角色本身改别的字段（描述等）不受这条拦 —— 判据是「变成超管」，不是「碰了超管」。
    /// </summary>
    [Fact]
    public async Task Update_KeepingAnExistingSuperAdminName_IsNotBlockedByTheNameGuard()
    {
        var role = ArrangeExistingRole(SuperAdminRoleName);

        var result = await _service.UpdateAsync(role.Id, new UpdateRoleDto { Name = SuperAdminRoleName, Description = "x" });

        result.Succeeded.ShouldBeTrue();
    }

    /// <summary>★★ 凭空创建一个叫超管角色名的角色，同样拒绝（那是这条路的另一半）。</summary>
    [Fact]
    public async Task Create_WithASuperAdminName_IsRejected()
    {
        var result = await _service.CreateAsync(new CreateRoleDto { Name = SuperAdminRoleName });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        _roleManagerMock.Verify(x => x.CreateAsync(It.IsAny<Role>()), Times.Never);
    }

    /// <summary>★ 支配不了的角色，改不动 —— 与成员变更同一判据。</summary>
    [Fact]
    public async Task Update_OnANonDominatedRole_IsRejected()
    {
        var role = ArrangeExistingRole("Finance");
        _authMock.Setup(x => x.CanManageRoleAsync(_actorId, role.Id)).ReturnsAsync(false);

        var result = await _service.UpdateAsync(role.Id, new UpdateRoleDto { Name = "Finance2" });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        _roleManagerMock.Verify(x => x.UpdateAsync(It.IsAny<Role>()), Times.Never);
    }

    /// <summary>★ 支配不了的角色也删不掉：删掉别人的角色是变相的越权削权。</summary>
    [Fact]
    public async Task Delete_OnANonDominatedRole_IsRejected()
    {
        var role = ArrangeExistingRole("Finance");
        _authMock.Setup(x => x.CanManageRoleAsync(_actorId, role.Id)).ReturnsAsync(false);

        var result = await _service.DeleteAsync(role.Id);

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(403);
        _roleManagerMock.Verify(x => x.DeleteAsync(It.IsAny<Role>()), Times.Never);
    }

    /// <summary>超管本人不受支配判定约束（它支配一切角色）。</summary>
    [Fact]
    public async Task Update_BySuperAdmin_SkipsTheDominationCheck()
    {
        var role = ArrangeExistingRole("Finance");
        _authMock.Setup(x => x.IsSuperAdminAsync(_actorId)).ReturnsAsync(true);
        _authMock.Setup(x => x.CanManageRoleAsync(_actorId, role.Id)).ReturnsAsync(false);

        var result = await _service.UpdateAsync(role.Id, new UpdateRoleDto { Name = "Finance2" });

        result.Succeeded.ShouldBeTrue();
    }
}
