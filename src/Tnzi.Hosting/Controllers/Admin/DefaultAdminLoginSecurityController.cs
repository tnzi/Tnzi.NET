
namespace Tnzi.Hosting.Controllers.Admin;

/// <summary>
/// 默认管理员登录安全管理控制器
/// 路由：/api/admin/login-security（自动添加 /api 前缀）
/// 用户项目可以实现 AdminLoginSecurityController 继承此 Controller，然后重写方法
/// </summary>
[Route("admin/login-security")]
public class DefaultAdminLoginSecurityController : LoginSecurityAdminControllerBase
{
    public DefaultAdminLoginSecurityController(ILoginSecurityService loginSecurityService)
        : base(loginSecurityService)
    {
    }
}
