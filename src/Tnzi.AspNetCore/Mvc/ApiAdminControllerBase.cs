
namespace Tnzi.AspNetCore.Mvc;

/// <summary>
/// 管理类API控制器基类
/// 提供统一的管理类API授权和Swagger分组
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "admin")]
[ApiAuthorize(PermissionName = "Admin.Manage")]
[StableApi(Since = "0.1.0")]
public abstract class ApiAdminControllerBase : ApiControllerBase
{
    /// <summary>
    /// 初始化管理类API控制器基类
    /// </summary>
    /// <param name="serviceProvider">服务提供者（可选）</param>
    protected ApiAdminControllerBase(IServiceProvider? serviceProvider = null)
        : base(serviceProvider)
    {
    }
}