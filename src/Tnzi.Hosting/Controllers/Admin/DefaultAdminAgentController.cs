
namespace Tnzi.Hosting.Controllers.Admin;

/// <summary>
/// 默认管理员Agent管理控制器
/// 路由：/api/admin/agents（自动添加 /api 前缀）
/// 用户项目可以实现 AdminAgentController 继承此 Controller，然后重写方法
/// </summary>
[Route("admin/agents")]
public class DefaultAdminAgentController : AgentAdminControllerBase
{
    public DefaultAdminAgentController(IAgentService agentService)
        : base(agentService)
    {
    }
}