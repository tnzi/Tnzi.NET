
namespace Tnzi.Hosting.Controllers.Admin;

/// <summary>
/// 默认管理员操作审计管理控制器
/// 路由：/api/admin/audit-operations（自动添加 /api 前缀）
/// 用户项目可以实现 AdminAuditOperationController 继承此 Controller，然后重写方法
/// </summary>
[Route("admin/audit-operations")]
public class DefaultAdminAuditOperationController : AuditOperationAdminControllerBase
{
    public DefaultAdminAuditOperationController(IAuditOperationService auditOperationService)
        : base(auditOperationService)
    {
    }
}