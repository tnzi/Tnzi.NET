
namespace Tnzi.Hosting.Controllers.Admin;

/// <summary>
/// 默认管理员工作流管理控制器
/// 路由：/api/admin/workflows（自动添加 /api 前缀）
/// 用户项目可以实现 AdminWorkflowController 继承此 Controller，然后重写方法
/// </summary>
[Route("admin/workflows")]
public class DefaultAdminWorkflowController : WorkflowAdminControllerBase
{
    public DefaultAdminWorkflowController(IWorkflowService workflowService)
        : base(workflowService)
    {
    }
}