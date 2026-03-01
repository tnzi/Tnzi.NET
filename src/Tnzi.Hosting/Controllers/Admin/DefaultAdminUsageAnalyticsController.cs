
namespace Tnzi.Hosting.Controllers.Admin;

/// <summary>
/// 默认管理员使用量分析控制器
/// 路由：/api/admin/ai/usage（自动添加 /api 前缀）
/// </summary>
[Route("admin/ai/usage")]
public class DefaultAdminUsageAnalyticsController : UsageAnalyticsAdminControllerBase
{
    public DefaultAdminUsageAnalyticsController(IUsageAnalyticsService analyticsService)
        : base(analyticsService)
    {
    }
}
