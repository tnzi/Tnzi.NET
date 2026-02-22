namespace Tnzi.Identity.Controllers.Admin;

/// <summary>
/// 角色管理控制器基类
/// 提供角色CRUD、权限分配等API端点，所有方法支持重写
/// </summary>
public abstract class RoleAdminControllerBase : ApiAdminControllerBase
{
    protected readonly IRoleService RoleService;

    /// <summary>
    /// 初始化角色管理控制器基类
    /// </summary>
    /// <param name="roleService">角色服务</param>
    protected RoleAdminControllerBase(IRoleService roleService)
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
    public virtual async Task<ApiResult> Delete(Guid id)
    {
        var result = await RoleService.DeleteAsync(id);
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

}
