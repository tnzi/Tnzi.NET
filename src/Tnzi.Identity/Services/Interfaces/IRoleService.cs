namespace Tnzi.Identity.Services;

/// <summary>
/// 角色管理服务接口
/// </summary>
public interface IRoleService
{
    /// <summary>
    /// 获取所有角色
    /// </summary>
    Task<Result<IEnumerable<RoleDto>>> GetAllAsync();

    /// <summary>
    /// 获取角色分页列表
    /// </summary>
    Task<Result<IPagedList<RoleDto>>> GetPagedListAsync(RoleListQueryDto query);

    /// <summary>
    /// 根据ID获取角色详情（包含用户计数）
    /// </summary>
    Task<Result<RoleDetailDto>> GetDetailAsync(Guid id);

    /// <summary>
    /// 根据ID获取角色
    /// </summary>
    Task<Result<RoleDto>> GetByIdAsync(Guid id);

    /// <summary>
    /// 根据名称获取角色
    /// </summary>
    Task<Result<RoleDto>> GetByNameAsync(string name);

    /// <summary>
    /// 创建角色
    /// </summary>
    Task<Result<RoleDto>> CreateAsync(CreateRoleDto input);

    /// <summary>
    /// 更新角色
    /// </summary>
    Task<Result<RoleDto>> UpdateAsync(Guid id, UpdateRoleDto input);

    /// <summary>
    /// 删除角色
    /// </summary>
    Task<Result> DeleteAsync(Guid id);

    /// <summary>
    /// 批量删除角色
    /// </summary>
    Task<Result> DeleteManyAsync(IEnumerable<Guid> ids);

    /// <summary>
    /// 检查角色是否存在
    /// </summary>
    Task<Result<bool>> ExistsAsync(string name);

    /// <summary>
    /// 获取角色下的用户分页列表
    /// </summary>
    Task<Result<IPagedList<UserListItemDto>>> GetUsersInRoleAsync(Guid roleId, PagedQueryDto query);

    /// <summary>
    /// 获取角色下的用户数量
    /// </summary>
    Task<Result<int>> GetUserCountAsync(Guid roleId);

    /// <summary>
    /// 获取所有默认角色
    /// </summary>
    Task<Result<IEnumerable<RoleDto>>> GetDefaultRolesAsync();

    /// <summary>
    /// 设置角色为默认角色
    /// </summary>
    Task<Result> SetDefaultAsync(Guid id, bool isDefault);
}
