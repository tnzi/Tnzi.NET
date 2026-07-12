
namespace Tnzi.AspNetCore.Mvc;

/// <summary>
/// 管理类API控制器基类
/// 提供统一的管理类API授权和Swagger分组。
/// 基类只要求"已认证"(裸 <c>[ApiAuthorize]</c>),不再压 <c>Admin.Manage</c> 码:
/// 每个 admin 控制器以类级模块 <c>.view</c> 码 + 写端点方法级操作码承担真实门禁
/// (AND 语义),"能否进后台"由用户是否持有任何具体授权自然决定。这样进门/落脚点
/// 不是权限矩阵里可被清空的行——把某角色权限清零后,其成员登录只会得到一个空壳
/// 后台(菜单只剩公共项、所有业务 API 403),而不是连权限自查(access-profile)
/// 都被锁死的死循环。
/// </summary>
[ApiController]
[ApiExplorerSettings(GroupName = "admin")]
[ApiAuthorize]
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