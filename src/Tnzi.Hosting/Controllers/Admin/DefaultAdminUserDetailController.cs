
namespace Tnzi.Hosting.Controllers.Admin;

/// <summary>
/// 默认管理员用户详情管理控制器
/// 路由：/api/admin/user-details（自动添加 /api 前缀）
/// 用户项目可以实现 AdminUserDetailController 继承此 Controller，然后重写方法
/// </summary>
[Route("admin/user-details")]
public class DefaultAdminUserDetailController : UserDetailAdminControllerBase
{
    public DefaultAdminUserDetailController(IUserDetailService userDetailService)
        : base(userDetailService)
    {
    }
}