namespace Tnzi.Authorization.Controllers.Admin;

/// <summary>
/// 数据授权控制器基类
/// 提供数据权限检查、获取数据过滤条件等API端点，所有方法支持重写
/// </summary>
[Route("admin/data-authorization")]
public abstract class DataAuthAdminControllerBase : ApiAdminControllerBase
{
    protected readonly IDataAuthService DataAuthService;

    /// <summary>
    /// 初始化数据授权控制器基类
    /// </summary>
    /// <param name="dataAuthService">数据授权服务</param>
    protected DataAuthAdminControllerBase(IDataAuthService dataAuthService)
    {
        DataAuthService = Check.NotNull(dataAuthService);
    }

    #region 权限检查

    /// <summary>
    /// 检查用户是否有指定实体的数据权限
    /// </summary>
    /// <param name="request">检查权限请求</param>
    /// <returns>是否有权限</returns>
    [HttpPost("check")]
    public virtual async Task<ApiResult<bool>> CheckDataPermission([FromBody] CheckDataPermissionRequest request)
    {
        var result = await DataAuthService.CheckDataPermissionByTypeNameAsync(
            request.UserId, request.EntityTypeName, request.EntityId, request.Operation);
        return result.ToApiResult();
    }

    #endregion

    #region EntityInfo 管理

    /// <summary>
    /// 获取所有实体信息
    /// </summary>
    /// <returns>实体信息列表</returns>
    [HttpGet("entity-infos")]
    public virtual async Task<ApiResult<IEnumerable<EntityInfo>>> GetAllEntityInfos()
    {
        var result = await DataAuthService.GetAllEntityInfosAsync();
        return result.ToApiResult();
    }

    /// <summary>
    /// 根据类型名称获取实体信息
    /// </summary>
    /// <param name="entityTypeName">实体类型名称</param>
    /// <returns>实体信息</returns>
    [HttpGet("entity-info/{entityTypeName}")]
    public virtual async Task<ApiResult<EntityInfo>> GetEntityInfo(string entityTypeName)
    {
        var result = await DataAuthService.GetEntityInfoAsync(entityTypeName);
        return result.ToApiResult();
    }

    /// <summary>
    /// 根据ID获取实体信息
    /// </summary>
    /// <param name="id">实体信息ID</param>
    /// <returns>实体信息</returns>
    [HttpGet("entity-infos/{id:guid}")]
    public virtual async Task<ApiResult<EntityInfo>> GetEntityInfoById(Guid id)
    {
        var result = await DataAuthService.GetEntityInfoByIdAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建实体信息
    /// </summary>
    /// <param name="request">实体信息</param>
    /// <returns>创建的实体信息</returns>
    [HttpPost("entity-infos")]
    public virtual async Task<ApiResult<EntityInfo>> CreateEntityInfo([FromBody] CreateEntityInfoRequest request)
    {
        var result = await DataAuthService.CreateEntityInfoAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新实体信息
    /// </summary>
    /// <param name="id">实体信息ID</param>
    /// <param name="request">实体信息</param>
    /// <returns>更新后的实体信息</returns>
    [HttpPut("entity-infos/{id:guid}")]
    public virtual async Task<ApiResult<EntityInfo>> UpdateEntityInfo(Guid id, [FromBody] UpdateEntityInfoRequest request)
    {
        var result = await DataAuthService.UpdateEntityInfoAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除实体信息
    /// </summary>
    /// <param name="id">实体信息ID</param>
    /// <returns>操作结果</returns>
    [HttpDelete("entity-infos/{id:guid}")]
    public virtual async Task<ApiResult> DeleteEntityInfo(Guid id)
    {
        var result = await DataAuthService.DeleteEntityInfoAsync(id);
        return result.ToApiResult();
    }

    #endregion

    #region EntityRole 管理

    /// <summary>
    /// 获取用户的所有实体角色
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>实体角色集合</returns>
    [HttpGet("user/{userId:guid}/entity-roles")]
    public virtual async Task<ApiResult<IEnumerable<EntityRole>>> GetUserEntityRoles(Guid userId)
    {
        var result = await DataAuthService.GetUserEntityRolesAsync(userId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 根据ID获取实体角色
    /// </summary>
    /// <param name="id">实体角色ID</param>
    /// <returns>实体角色</returns>
    [HttpGet("entity-roles/{id:guid}")]
    public virtual async Task<ApiResult<EntityRole>> GetEntityRoleById(Guid id)
    {
        var result = await DataAuthService.GetEntityRoleByIdAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取实体的所有角色配置
    /// </summary>
    /// <param name="entityInfoId">实体信息ID</param>
    /// <returns>实体角色列表</returns>
    [HttpGet("entity-infos/{entityInfoId:guid}/roles")]
    public virtual async Task<ApiResult<IEnumerable<EntityRole>>> GetEntityRolesByEntity(Guid entityInfoId)
    {
        var result = await DataAuthService.GetEntityRolesByEntityAsync(entityInfoId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 获取角色的所有实体配置
    /// </summary>
    /// <param name="roleId">角色ID</param>
    /// <returns>实体角色列表</returns>
    [HttpGet("roles/{roleId:guid}/entity-roles")]
    public virtual async Task<ApiResult<IEnumerable<EntityRole>>> GetEntityRolesByRole(Guid roleId)
    {
        var result = await DataAuthService.GetEntityRolesByRoleAsync(roleId);
        return result.ToApiResult();
    }

    /// <summary>
    /// 创建实体角色
    /// </summary>
    /// <param name="request">实体角色信息</param>
    /// <returns>创建的实体角色</returns>
    [HttpPost("entity-roles")]
    public virtual async Task<ApiResult<EntityRole>> CreateEntityRole([FromBody] CreateEntityRoleRequest request)
    {
        var result = await DataAuthService.CreateEntityRoleAsync(request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 更新实体角色
    /// </summary>
    /// <param name="id">实体角色ID</param>
    /// <param name="request">实体角色信息</param>
    /// <returns>更新后的实体角色</returns>
    [HttpPut("entity-roles/{id:guid}")]
    public virtual async Task<ApiResult<EntityRole>> UpdateEntityRole(Guid id, [FromBody] UpdateEntityRoleRequest request)
    {
        var result = await DataAuthService.UpdateEntityRoleAsync(id, request);
        return result.ToApiResult();
    }

    /// <summary>
    /// 删除实体角色
    /// </summary>
    /// <param name="id">实体角色ID</param>
    /// <returns>操作结果</returns>
    [HttpDelete("entity-roles/{id:guid}")]
    public virtual async Task<ApiResult> DeleteEntityRole(Guid id)
    {
        var result = await DataAuthService.DeleteEntityRoleAsync(id);
        return result.ToApiResult();
    }

    /// <summary>
    /// 批量创建实体角色
    /// </summary>
    /// <param name="request">批量请求</param>
    /// <returns>创建的实体角色列表</returns>
    [HttpPost("entity-roles/batch")]
    public virtual async Task<ApiResult<IEnumerable<EntityRole>>> BatchCreateEntityRoles([FromBody] BatchEntityRoleRequest request)
    {
        var result = await DataAuthService.BatchCreateEntityRolesAsync(request);
        return result.ToApiResult();
    }

    #endregion
}

/// <summary>
/// 检查数据权限请求
/// </summary>
public class CheckDataPermissionRequest
{
    /// <summary>
    /// 用户ID
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 实体类型名称（完整类型名）
    /// </summary>
    public string EntityTypeName { get; set; } = string.Empty;

    /// <summary>
    /// 实体ID
    /// </summary>
    public Guid EntityId { get; set; }

    /// <summary>
    /// 操作类型
    /// </summary>
    public DataAuthOperation Operation { get; set; }
}
