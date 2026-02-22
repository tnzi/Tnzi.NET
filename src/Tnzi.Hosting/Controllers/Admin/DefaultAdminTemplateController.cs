
namespace Tnzi.Hosting.Controllers.Admin;

/// <summary>
/// 默认管理员模板管理控制器
/// 路由：/api/admin/templates（自动添加 /api 前缀）
/// 用户项目可以实现 AdminTemplateController 继承此 Controller，然后重写方法
/// </summary>
[Route("admin/templates")]
public class DefaultAdminTemplateController : TemplateAdminControllerBase
{
    public DefaultAdminTemplateController(ITemplateStoreService templateStoreService, ITemplateEngine? templateEngine = null)
        : base(templateStoreService, templateEngine)
    {
    }
}
