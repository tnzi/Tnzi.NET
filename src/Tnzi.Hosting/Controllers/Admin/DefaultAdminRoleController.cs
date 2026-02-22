
namespace Tnzi.Hosting.Controllers.Admin;

/// <summary>
/// 默认管理员角色管理控制器
/// 路由：/api/admin/roles（自动添加 /api 前缀）
/// 用户项目可以实现 AdminRoleController 继承此 Controller，然后重写方法
/// </summary>
[Route("admin/roles")]
public class DefaultAdminRoleController : RoleAdminControllerBase
{
    public DefaultAdminRoleController(IRoleService roleService)
        : base(roleService)
    {
    }
}
