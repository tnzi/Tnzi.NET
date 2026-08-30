using Tnzi.Identity.Controllers.Admin;
using Tnzi.Modules;
using Tnzi.Identity.Permissions;
using Tnzi.Security.Authorization;

namespace Tnzi.Identity.Tests;

/// <summary>
/// 未加载 <c>Tnzi.Identity.Organization</c> 时，身份内核退化成什么。
/// </summary>
/// <remarks>
/// <para>
/// 本测试项目<b>刻意不引用</b> <c>Tnzi.Identity.Organization</c>，所以进程里既没有
/// <see cref="IOrganizationService"/> 的实现，也没有那张表 —— 这正是消费方少加载一个包时的现场，
/// 不需要额外搭夹具去模拟（可空可选依赖直接传 null）。
/// </para>
/// <para>
/// ★ 要守住的命题是<b>「少一点能力，绝不是错的行为」</b>：
/// 组织端点答 <b>501</b> 并指名要加载什么（不是 503：503 意味着「暂时坏了，等会再试」，
/// 会让监控与客户端重试逻辑追着一件永远不会恢复的事；也不是 404：路由确实存在），
/// 用户 DTO 的组织名留空而不是报错，而「把用户放进一个这台宿主根本没有的组织」必须<b>失败关闭</b>。
/// </para>
/// </remarks>
public class OrganizationPackageAbsenceTests
{
    private static DefaultUserAdminController Controller()
        => new(Mock.Of<IUserService>(), Mock.Of<IPasswordService>(), organizationService: null);

    /// <summary>
    /// 锚：容器里真的一个实现都没有。哪天有人在父模块里注册了一个默认实现，
    /// 下面几条测的就不再是"缺席"了。
    /// </summary>
    [Fact]
    public async Task TheCoreShipsNoOrganizationServiceImplementation()
    {
        var services = new ServiceCollection();
        var context = new ServiceConfigurationContext(services, new ConfigurationBuilder().Build());

        await new IdentityModule().ConfigureServicesAsync(context);

        services.Any(d => d.ServiceType == typeof(IOrganizationService)).ShouldBeFalse(
            "核心又注册了 IOrganizationService —— 组织包就不再是可选的了");
    }

    [Fact]
    public async Task AssignToOrganization_Answers501_NamingTheModule()
    {
        var result = await Controller().AssignToOrganization(Guid.NewGuid(), new AssignOrganizationDto { OrganizationId = Guid.NewGuid() });

        result.Success.ShouldBeFalse();
        result.Code.ShouldBe(501);
        result.Message!.ShouldContain("Tnzi.Identity.Organization");
    }

    [Fact]
    public async Task RemoveFromOrganization_Answers501_NamingTheModule()
    {
        var result = await Controller().RemoveFromOrganization(Guid.NewGuid());

        result.Success.ShouldBeFalse();
        result.Code.ShouldBe(501);
        result.Message!.ShouldContain("Tnzi.Identity.Organization");
    }

    /// <summary>
    /// 503 与 404 都是错的答案，且各自错在会诱导出一种具体的错误行为。
    /// </summary>
    [Fact]
    public async Task Absence_IsNeither503Nor404()
    {
        var result = await Controller().RemoveFromOrganization(Guid.NewGuid());

        result.Code.ShouldNotBe(503, "503 会让监控和客户端一直重试一件永远不会恢复的事");
        result.Code.ShouldNotBe(404, "404 会让调用方以为自己拼错了 URL");
    }

    /// <summary>
    /// 核心不再声明那 4 个权限码 —— 没有组织树的宿主，权限矩阵里不会多出一块永远授不出去的功能面。
    /// </summary>
    [Theory]
    [InlineData("organization.view")]
    [InlineData("organization.create")]
    [InlineData("organization.update")]
    [InlineData("organization.delete")]
    public void WithoutThePackage_TheOrganizationCodesAreNotDeclared(string code)
    {
        var context = new PermissionDefinitionContext();
        new IdentityPermissions().Define(context);

        context.Permissions.ContainsKey(code).ShouldBeFalse();
    }

    /// <summary>对照组：核心自己的码一个不少，组也还在。</summary>
    [Fact]
    public void WithoutThePackage_TheCoreCodesAreIntact()
    {
        var context = new PermissionDefinitionContext();
        new IdentityPermissions().Define(context);

        context.Permissions.ContainsKey("user.view").ShouldBeTrue();
        context.Permissions.ContainsKey("session.view").ShouldBeTrue();
        context.Groups.ContainsKey("identity").ShouldBeTrue();
    }

    /// <summary>
    /// 核心的 <c>User</c> 实体不得有指向组织的导航属性 —— 有它，这个包就拆不出去。
    /// </summary>
    /// <remarks>
    /// 用类型形状而不是名字断言：这里看的是"User 有没有引用一个住在别的程序集里的实体"，
    /// 而不是"有没有一个叫 Organization 的属性"。
    /// </remarks>
    [Fact]
    public void TheCoreUserEntityHasNoNavigationIntoTheOrganizationPackage()
    {
        var identityAssembly = typeof(User).Assembly;

        typeof(User).GetProperties()
            .Where(p => p.PropertyType.IsClass && p.PropertyType != typeof(string))
            .Where(p => p.PropertyType.Assembly.GetName().Name?.StartsWith("Tnzi.", StringComparison.Ordinal) == true)
            .Where(p => p.PropertyType.Assembly != identityAssembly)
            .ShouldBeEmpty("User 长出了一个指向别的 Tnzi 程序集的导航属性：父模块开始引用子模块的实体了");

        typeof(User).GetProperty("Organization").ShouldBeNull();
    }

    /// <summary>
    /// <c>OrganizationId</c> 与它的索引留在核心：加不加载那个包，User 表的列形状必须一样。
    /// </summary>
    [Fact]
    public void TheOrganizationIdColumnStaysOnTheCoreUser()
    {
        typeof(User).GetProperty(nameof(User.OrganizationId))!.PropertyType.ShouldBe(typeof(Guid?));
    }

    /// <summary>
    /// 没有组织包时，「建一个属于某组织的用户」<b>失败关闭</b>：501 + 指名要加载什么，且用户不落库。
    /// </summary>
    /// <remarks>
    /// ★ 这一条比端点那两条更要紧。拆分之前，一个不存在的 OrganizationId 会一路走到外键，
    /// 由数据库抛出来变成一个不透明的 500；拆分之后外键随包走，不加载包时连那道兜底都没有 ——
    /// 判定必须提前到服务层，否则「缺席」就从"少一点能力"变成"悄悄写下一个指向虚空的 Id"。
    /// </remarks>
    [Fact]
    public async Task CreateUser_WithAnOrganizationId_FailsClosed()
    {
        var service = UserServiceWithoutOrganizations(out var userManager);

        var result = await service.CreateAsync(new CreateUserDto
        {
            UserName = "u",
            Password = "Password123!",
            OrganizationId = Guid.NewGuid(),
        });

        result.Succeeded.ShouldBeFalse();
        result.Code.ShouldBe(501);
        result.Message!.ShouldContain("Tnzi.Identity.Organization");
        userManager.Verify(m => m.CreateAsync(It.IsAny<User>(), It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// 对照组：不带组织的创建请求照常通过守卫 —— 缺席只挡住组织那一件事，不是把创建用户挡死。
    /// </summary>
    [Fact]
    public async Task CreateUser_WithoutAnOrganizationId_IsUnaffected()
    {
        var service = UserServiceWithoutOrganizations(out var userManager);
        userManager.Setup(m => m.CreateAsync(It.IsAny<User>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "stop here" }));

        var result = await service.CreateAsync(new CreateUserDto { UserName = "u", Password = "Password123!" });

        // 走到了 UserManager（并在那里被我们故意拦下），说明组织守卫放行了。
        result.Code.ShouldBe(400);
        userManager.Verify(m => m.CreateAsync(It.IsAny<User>(), It.IsAny<string>()), Times.Once);
    }

    private static UserService UserServiceWithoutOrganizations(out Mock<UserManager<User>> userManager)
    {
        var config = new TypeAdapterConfig();
        config.NewConfig<CreateUserDto, User>();
        MapperExtensions.SetMapper(new Mapper(config));

        var store = new Mock<IUserStore<User>>();
        userManager = new Mock<UserManager<User>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        var roleStore = new Mock<IRoleStore<Role>>();
        var roleManager = new Mock<RoleManager<Role>>(roleStore.Object, null!, null!, null!, null!);

        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(x => x.GetService(typeof(ILoggerFactory))).Returns(loggerFactory.Object);

        return new UserService(
            userManager.Object,
            roleManager.Object,
            Mock.Of<IRepository<User, Guid>>(),
            serviceProvider.Object,
            organizationService: null);
    }
}
