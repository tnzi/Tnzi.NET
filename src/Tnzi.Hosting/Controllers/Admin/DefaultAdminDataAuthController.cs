




namespace Tnzi.Hosting.Controllers.Admin;

/// <summary>
/// 默认管理员数据授权控制器
/// 路由：/api/admin/data-auth（自动添加 /api 前缀）
/// 用户项目可以实现 AdminDataAuthController 继承此 Controller，然后重写方法
/// </summary>
[Route("admin/data-auth")]
public class DefaultAdminDataAuthController : DataAuthAdminControllerBase
{
    public DefaultAdminDataAuthController(IDataAuthService dataAuthService)
        : base(dataAuthService)
    {
    }
}