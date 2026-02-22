
namespace Tnzi.Hosting.Controllers.Admin;

/// <summary>
/// 默认管理员会话管理控制器
/// 路由：/api/admin/sessions（自动添加 /api 前缀）
/// 用户项目可以实现 AdminSessionController 继承此 Controller，然后重写方法
/// </summary>
[Route("admin/sessions")]
public class DefaultAdminSessionController : SessionAdminControllerBase
{
    public DefaultAdminSessionController(ISessionService sessionService)
        : base(sessionService)
    {
    }
}