
namespace Tnzi.Hosting.Controllers.Admin;

/// <summary>
/// 默认管理员配额管理控制器
/// 路由：/api/admin/quotas（自动添加 /api 前缀）
/// 用户项目可以实现 AdminQuotaController 继承此 Controller，然后重写方法
/// </summary>
[Route("admin/quotas")]
public class DefaultAdminQuotaController : QuotaAdminControllerBase
{
    public DefaultAdminQuotaController(IQuotaService quotaService)
        : base(quotaService)
    {
    }
}