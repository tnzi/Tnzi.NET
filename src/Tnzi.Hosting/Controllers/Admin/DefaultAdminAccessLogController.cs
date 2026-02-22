
namespace Tnzi.Hosting.Controllers.Admin;

/// <summary>
/// 默认管理员访问日志控制器
/// 路由：/api/admin/access-logs（自动添加 /api 前缀）
/// 用户项目可以实现 AdminAccessLogController 继承此 Controller，然后重写方法
/// </summary>
[Route("admin/access-logs")]
public class DefaultAdminAccessLogController : AccessLogAdminControllerBase
{
    public DefaultAdminAccessLogController(IAccessLogService accessLogService)
        : base(accessLogService)
    {
    }
}
