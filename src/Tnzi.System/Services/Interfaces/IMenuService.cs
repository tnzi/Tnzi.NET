namespace Tnzi.System.Services;

/// <summary>
/// 菜单服务接口
/// </summary>
public interface IMenuService
{
    /// <summary>
    /// 获取所有菜单
    /// </summary>
    Task<Result<IEnumerable<MenuDto>>> GetMenusAsync();

    /// <summary>
    /// 根据ID获取菜单
    /// </summary>
    Task<Result<MenuDto>> GetMenuByIdAsync(Guid id);

    /// <summary>
    /// 创建菜单
    /// </summary>
    Task<Result<MenuDto>> CreateMenuAsync(CreateMenuDto input);

    /// <summary>
    /// 更新菜单
    /// </summary>
    Task<Result<MenuDto>> UpdateMenuAsync(Guid id, UpdateMenuDto input);

    /// <summary>
    /// 删除菜单
    /// </summary>
    Task<Result> DeleteMenuAsync(Guid id);

    /// <summary>
    /// 批量删除菜单（包含子菜单检查）
    /// </summary>
    Task<Result> DeleteMenusAsync(IEnumerable<Guid> ids);

    /// <summary>
    /// 获取用户的菜单树
    /// </summary>
    Task<Result<IEnumerable<MenuTreeNode>>> GetUserMenuTreeAsync(Guid userId);

    /// <summary>
    /// 批量更新菜单排序
    /// </summary>
    Task<Result> UpdateMenuOrdersAsync(IEnumerable<MenuOrderDto> orders);

    /// <summary>
    /// 移动菜单到新的父级
    /// </summary>
    Task<Result> MoveMenuAsync(Guid id, Guid? newParentId);

    /// <summary>
    /// 批量 seed 菜单：按 <c>MenuKey</c> upsert（不存在则插入，已存在则跳过以保护
    /// 运营对该行做过的覆盖修改）。用于首次启用 'merge' 菜单源时,把前端路由派生
    /// 的菜单镜像成一组可编辑的 <c>Sys_Menu</c> 行,给运营一个起点。
    /// </summary>
    Task<Result<MenuSeedResultDto>> SeedMenusAsync(IEnumerable<CreateMenuDto> menus);
}
