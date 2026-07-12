namespace Tnzi.Identity.Controllers.Admin;

/// <summary>
/// 角色管理控制器
/// 提供角色CRUD、权限分配等API端点，所有方法支持重写
/// </summary>
[DefaultController]
[Route("admin/roles")]
[ApiAuthorize(PermissionName = "role.view")]
public class DefaultRoleAdminController : ApiAdminControllerBase
{
    protected readonly IRoleService RoleService;

    /// <summary>
    /// 初始化角色管理控制器
    /// </summary>
    /// <param name="roleService">角色服务</param>
    public DefaultRoleAdminController(IRoleService roleService)
    {
        RoleService = Check.NotNull(roleService);
    }

    /// <summary>
    /// 获取所有角色
    /// </summary>
    /// <returns>角色列表</returns>
    [HttpGet]
    public virtual async Task<ApiResult<IEnumerable<RoleDto>>> GetRoles()
    {
        var result = await RoleService.GetAllAsync();
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取角色分页列表
    /// </summary>
    /// <param name="query">查询参数</param>
    /// <returns>角色分页列表</returns>
    [HttpGet("paged")]
    public virtual async Task<ApiResult<IPagedList<RoleDto>>> GetPagedList([FromQuery] RoleListQueryDto query)
    {
        var result = await RoleService.GetPagedListAsync(query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 根据ID获取角色详情（包含用户计数）
    /// </summary>
    /// <param name="id">角色ID</param>
    /// <returns>角色详情</returns>
    [HttpGet("{id}/detail")]
    public virtual async Task<ApiResult<RoleDetailDto>> GetDetail(Guid id)
    {
        var result = await RoleService.GetDetailAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 根据ID获取角色
    /// </summary>
    /// <param name="id">角色ID</param>
    /// <returns>角色信息</returns>
    [HttpGet("{id}")]
    public virtual async Task<ApiResult<RoleDto>> GetById(Guid id)
    {
        var result = await RoleService.GetByIdAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 根据名称获取角色
    /// </summary>
    /// <param name="name">角色名称</param>
    /// <returns>角色信息</returns>
    [HttpGet("by-name/{name}")]
    public virtual async Task<ApiResult<RoleDto>> GetByName(string name)
    {
        var result = await RoleService.GetByNameAsync(name);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建角色
    /// </summary>
    /// <param name="input">角色信息</param>
    /// <returns>创建的角色</returns>
    [HttpPost]
    [ApiAuthorize(PermissionName = "role.create")]
    public virtual async Task<ApiResult<RoleDto>> Create([FromBody] CreateRoleDto input)
    {
        var result = await RoleService.CreateAsync(input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新角色
    /// </summary>
    /// <param name="id">角色ID</param>
    /// <param name="input">角色信息</param>
    /// <returns>更新后的角色</returns>
    [HttpPut("{id}")]
    [ApiAuthorize(PermissionName = "role.update")]
    public virtual async Task<ApiResult<RoleDto>> Update(Guid id, [FromBody] UpdateRoleDto input)
    {
        var result = await RoleService.UpdateAsync(id, input);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除角色
    /// </summary>
    /// <param name="id">角色ID</param>
    /// <returns>操作结果</returns>
    [HttpDelete("{id}")]
    [ApiAuthorize(PermissionName = "role.delete")]
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await RoleService.DeleteAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量删除角色
    /// </summary>
    /// <param name="ids">角色ID列表</param>
    /// <returns>操作结果</returns>
    [HttpDelete("batch")]
    [ApiAuthorize(PermissionName = "role.delete")]
    public virtual async Task<ApiResult> DeleteMany([FromBody] IEnumerable<Guid> ids)
    {
        var result = await RoleService.DeleteManyAsync(ids);
        return result.ToApiResult();
    }

    /// <summary>
    /// 检查角色是否存在
    /// </summary>
    /// <param name="name">角色名称</param>
    /// <returns>是否存在</returns>
    [HttpGet("exists/{name}")]
    public virtual async Task<ApiResult<bool>> RoleExists(string name)
    {
        var result = await RoleService.ExistsAsync(name);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取角色下的用户分页列表
    /// </summary>
    /// <param name="id">角色ID</param>
    /// <param name="query">分页参数</param>
    /// <returns>用户分页列表</returns>
    [HttpGet("{id}/users")]
    public virtual async Task<ApiResult<IPagedList<UserListItemDto>>> GetUsersInRole(Guid id, [FromQuery] PagedQueryDto query)
    {
        var result = await RoleService.GetUsersInRoleAsync(id, query);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取角色下的用户数量
    /// </summary>
    /// <param name="id">角色ID</param>
    /// <returns>用户数量</returns>
    [HttpGet("{id}/user-count")]
    public virtual async Task<ApiResult<int>> GetUserCount(Guid id)
    {
        var result = await RoleService.GetUserCountAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取所有默认角色
    /// </summary>
    /// <returns>默认角色列表</returns>
    [HttpGet("defaults")]
    public virtual async Task<ApiResult<IEnumerable<RoleDto>>> GetDefaultRoles()
    {
        var result = await RoleService.GetDefaultRolesAsync();
        return result.ToApiResult();
    }

    /// <summary>
    /// 设置角色为默认角色
    /// </summary>
    /// <param name="id">角色ID</param>
    /// <param name="isDefault">是否设为默认</param>
    /// <returns>操作结果</returns>
    [HttpPut("{id}/default")]
    [ApiAuthorize(PermissionName = "role.update")]
    public virtual async Task<ApiResult> SetDefault(Guid id, [FromQuery] bool isDefault)
    {
        var result = await RoleService.SetDefaultAsync(id, isDefault);
        return result.ToApiResult();
    }
}
