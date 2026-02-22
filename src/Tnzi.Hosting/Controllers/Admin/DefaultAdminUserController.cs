
namespace Tnzi.Hosting.Controllers.Admin;

/// <summary>
/// 默认管理员用户管理控制器
/// 路由：/api/admin/users（自动添加 /api 前缀）
/// 用户项目可以实现 AdminUserController 继承此 Controller，然后重写方法
/// </summary>
[Route("admin/users")]
public class DefaultAdminUserController : UserAdminControllerBase
{
    public DefaultAdminUserController(
        IUserService userService,
        IPasswordService passwordService,
        IOrganizationService? organizationService = null)
        : base(userService, passwordService, organizationService)
    {
    }
}