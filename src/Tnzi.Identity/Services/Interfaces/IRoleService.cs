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
    /// 检查角色是否存在
    /// </summary>
    Task<Result<bool>> ExistsAsync(string name);
}
