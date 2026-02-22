
namespace Tnzi.Hosting.Controllers.Admin;

/// <summary>
/// 默认管理员配置管理控制器
/// 路由：/api/admin/settings（自动添加 /api 前缀）
/// 用户项目可以实现 AdminSettingController 继承此 Controller，然后重写方法
/// </summary>
[Route("admin/settings")]
public class DefaultAdminSettingController : SettingAdminControllerBase
{
    public DefaultAdminSettingController(ISettingService settingService)
        : base(settingService)
    {
    }
}
