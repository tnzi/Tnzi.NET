
namespace Tnzi.Hosting.Controllers.Admin;

/// <summary>
/// 默认管理员登录日志管理控制器
/// 路由：/api/admin/login-logs（自动添加 /api 前缀）
/// 用户项目可以实现 AdminLoginLogController 继承此 Controller，然后重写方法
/// </summary>
[Route("admin/login-logs")]
public class DefaultAdminLoginLogController : LoginLogAdminControllerBase
{
    public DefaultAdminLoginLogController(ILoginLogService loginLogService)
        : base(loginLogService)
    {
    }
}