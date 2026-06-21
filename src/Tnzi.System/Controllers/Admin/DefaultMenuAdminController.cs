
namespace Tnzi.System.Controllers.Admin;

/// <summary>
/// 菜单管理控制器
/// 提供菜单CRUD、获取菜单树、移动菜单等API端点，所有方法支持重写
/// </summary>
[DefaultController]
[Route("admin/menus")]
public class DefaultMenuAdminController : ApiAdminControllerBase
{
    protected readonly IMenuService MenuService;

    /// <summary>
    /// 初始化菜单管理控制器
    /// </summary>
    public DefaultMenuAdminController(IMenuService menuService)
    {
        MenuService = Check.NotNull(menuService);
    }

    /// <summary>
    /// 获取所有菜单
    /// </summary>
    [HttpGet]
    public virtual async Task<ApiResult<IEnumerable<MenuDto>>> GetMenus()
    {
        var result = await MenuService.GetMenusAsync();
        return result.ToApiResult();
    }

    /// <summary>
    /// 根据ID获取菜单
    /// </summary>
    [HttpGet("{id:guid}")]
    public virtual async Task<ApiResult<MenuDto>> GetById(Guid id)
    {
        var result = await MenuService.GetMenuByIdAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建菜单
    /// </summary>
    [HttpPost]
    public virtual async Task<ApiResult<MenuDto>> Create([FromBody] CreateMenuDto input)
    {
        var result = await MenuService.CreateMenuAsync(input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新菜单
    /// </summary>
    [HttpPut("{id:guid}")]
    public virtual async Task<ApiResult<MenuDto>> Update(Guid id, [FromBody] UpdateMenuDto input)
    {
        var result = await MenuService.UpdateMenuAsync(id, input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除菜单
    /// </summary>
    [HttpDelete("{id:guid}")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await MenuService.DeleteMenuAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量删除菜单
    /// </summary>
    [HttpDelete("batch")]
    public virtual async Task<ApiResult> DeleteMenus([FromBody] IEnumerable<Guid> ids)
    {
        var result = await MenuService.DeleteMenusAsync(ids);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取用户的菜单树
    /// </summary>
    [HttpGet("user/{userId}/tree")]
    public virtual async Task<ApiResult<IEnumerable<MenuTreeNode>>> GetUserMenuTree(Guid userId)
    {
        var result = await MenuService.GetUserMenuTreeAsync(userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量更新菜单排序
    /// </summary>
    [HttpPut("batch/orders")]
    public virtual async Task<ApiResult> UpdateMenuOrders([FromBody] IEnumerable<MenuOrderDto> menuOrders)
    {
        var result = await MenuService.UpdateMenuOrdersAsync(menuOrders);
        return result.ToApiResult();
    }

    /// <summary>
    /// 移动菜单到新的父级
    /// </summary>
    [HttpPut("{id:guid}/move")]
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
    public virtual async Task<ApiResult<MenuSeedResultDto>> Seed([FromBody] IEnumerable<CreateMenuDto> menus)
    {
        var result = await MenuService.SeedMenusAsync(menus);
        return result.ToApiResult();
    }
}
