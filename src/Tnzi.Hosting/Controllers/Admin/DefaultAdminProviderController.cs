
namespace Tnzi.Hosting.Controllers.Admin;

/// <summary>
/// 默认管理员 AI Provider 管理控制器
/// 路由：/api/admin/ai/providers（自动添加 /api 前缀）
/// </summary>
[Route("admin/ai/providers")]
public class DefaultAdminProviderController : ProviderAdminControllerBase
{
    public DefaultAdminProviderController(IChatClientFactory clientFactory)
        : base(clientFactory)
    {
    }
}
