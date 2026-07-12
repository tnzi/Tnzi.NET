
namespace Tnzi.System.Controllers.Admin;

/// <summary>
/// 菜单管理控制器
/// 提供菜单CRUD、获取菜单树、移动菜单等API端点，所有方法支持重写
/// </summary>
[DefaultController]
[Route("admin/menus")]
// 豁免面刻意收窄到仅 GetUserMenuTree（user/{userId}/tree）这一个自服务端点：它是前端 'merge'
// 菜单模式的自服务链路，任何已登录用户都要能调用它来构建自己的导航，只随基类的认证边界开放。
// 其余端点均为菜单管理读写操作，逐一叠加方法级 system.menu.view/create/update/delete，
// 不再随认证边界泛化开放。
public class DefaultMenuAdminController : ApiAdminControllerBase
{
    protected readonly IMenuService MenuService;
    protected readonly IFunctionAuthorizationService? FunctionAuthorization;

    /// <summary>
    /// 初始化菜单管理控制器
    /// </summary>
    public DefaultMenuAdminController(IMenuService menuService, IFunctionAuthorizationService? functionAuthorization = null)
    {
        MenuService = Check.NotNull(menuService);
        FunctionAuthorization = functionAuthorization;
    }

    /// <summary>
    /// 获取所有菜单
    /// </summary>
    [HttpGet]
    [ApiAuthorize(PermissionName = "system.menu.view")]
    public virtual async Task<ApiResult<IEnumerable<MenuDto>>> GetMenus()
    {
        var result = await MenuService.GetMenusAsync();
        return result.ToApiResult();
    }

    /// <summary>
    /// 根据ID获取菜单
    /// </summary>
    [HttpGet("{id:guid}")]
    [ApiAuthorize(PermissionName = "system.menu.view")]
    public virtual async Task<ApiResult<MenuDto>> GetById(Guid id)
    {
        var result = await MenuService.GetMenuByIdAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建菜单
    /// </summary>
    [HttpPost]
    [ApiAuthorize(PermissionName = "system.menu.create")]
    public virtual async Task<ApiResult<MenuDto>> Create([FromBody] CreateMenuDto input)
    {
        var result = await MenuService.CreateMenuAsync(input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新菜单
    /// </summary>
    [HttpPut("{id:guid}")]
    [ApiAuthorize(PermissionName = "system.menu.update")]
    public virtual async Task<ApiResult<MenuDto>> Update(Guid id, [FromBody] UpdateMenuDto input)
    {
        var result = await MenuService.UpdateMenuAsync(id, input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除菜单
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ApiAuthorize(PermissionName = "system.menu.delete")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await MenuService.DeleteMenuAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量删除菜单
    /// </summary>
    [HttpDelete("batch")]
    [ApiAuthorize(PermissionName = "system.menu.delete")]
    public virtual async Task<ApiResult> DeleteMenus([FromBody] IEnumerable<Guid> ids)
    {
        var result = await MenuService.DeleteMenusAsync(ids);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取用户的菜单树。自读(userId=当前用户)只需已认证(前端 'merge' 菜单
    /// 模式的登录链路);**跨用户读**会暴露他人的导航/权限拓扑,须额外持有
    /// system.menu.view,否则任意已登录用户都可探测他人可见面(IDOR)。
    /// Authorization 模块未加载时无权限体系,保持旧行为。
    /// </summary>
    [HttpGet("user/{userId}/tree")]
    public virtual async Task<ApiResult<IEnumerable<MenuTreeNode>>> GetUserMenuTree(Guid userId)
    {
        var currentUserId = CurrentUser?.Id;
        if (FunctionAuthorization != null
            && userId != currentUserId
            && !(currentUserId is Guid readerId
                 && await FunctionAuthorization.CheckPermissionAsync(readerId, "system.menu.view")))
        {
            return Result.Failure<IEnumerable<MenuTreeNode>>(
                "You may only read your own menu tree.", 403, ErrorCodes.FORBIDDEN).ToApiResult();
        }

        var result = await MenuService.GetUserMenuTreeAsync(userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量更新菜单排序
    /// </summary>
    [HttpPut("batch/orders")]
    [ApiAuthorize(PermissionName = "system.menu.update")]
    public virtual async Task<ApiResult> UpdateMenuOrders([FromBody] IEnumerable<MenuOrderDto> menuOrders)
    {
        var result = await MenuService.UpdateMenuOrdersAsync(menuOrders);
        return result.ToApiResult();
    }

    /// <summary>
    /// 移动菜单到新的父级
    /// </summary>
    [HttpPut("{id:guid}/move")]
    [ApiAuthorize(PermissionName = "system.menu.update")]
    public virtual async Task<ApiResult> MoveMenu(Guid id, [FromQuery] Guid? newParentId = null)
    {
        var result = await MenuService.MoveMenuAsync(id, newParentId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量 seed 菜单（按 MenuKey upsert，已存在跳过以保护运营修改）。用于首次启用
    /// 'merge' 菜单源时把前端路由派生菜单镜像成一组可编辑的 Sys_Menu 行。
    /// </summary>
    [HttpPost("seed")]
    [ApiAuthorize(PermissionName = "system.menu.create")]
    public virtual async Task<ApiResult<MenuSeedResultDto>> Seed([FromBody] IEnumerable<CreateMenuDto> menus)
    {
        var result = await MenuService.SeedMenusAsync(menus);
        return result.ToApiResult();
    }
}
