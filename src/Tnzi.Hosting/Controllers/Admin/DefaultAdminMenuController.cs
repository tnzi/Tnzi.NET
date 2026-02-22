
namespace Tnzi.Hosting.Controllers.Admin;

/// <summary>
/// 默认管理员菜单管理控制器
/// 路由：/api/admin/menus（自动添加 /api 前缀）
/// 用户项目可以实现 AdminMenuController 继承此 Controller，然后重写方法
/// </summary>
[Route("admin/menus")]
public class DefaultAdminMenuController : MenuAdminControllerBase
{
    public DefaultAdminMenuController(IMenuService menuService)
        : base(menuService)
    {
    }
}
