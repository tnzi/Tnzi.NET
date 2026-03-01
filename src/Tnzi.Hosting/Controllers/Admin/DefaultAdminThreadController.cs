
namespace Tnzi.Hosting.Controllers.Admin;

/// <summary>
/// 默认管理员线程管理控制器
/// 路由：/api/admin/threads（自动添加 /api 前缀）
/// </summary>
[Route("admin/threads")]
public class DefaultAdminThreadController : ThreadAdminControllerBase
{
    public DefaultAdminThreadController(IAgentThreadService threadService)
        : base(threadService)
    {
    }
}
