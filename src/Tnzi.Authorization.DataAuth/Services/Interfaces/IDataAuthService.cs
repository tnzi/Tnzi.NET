namespace Tnzi.Authorization.DataAuth.Services;

/// <summary>
/// 数据授权服务接口
/// </summary>
public interface IDataAuthService
{
    #region 数据过滤

    /// <summary>
    /// 获取实体的数据权限过滤条件
    /// </summary>
    /// <typeparam name="TEntity">实体类型</typeparam>
    /// <param name="userId">用户ID</param>
    /// <param name="operation">操作类型</param>
    /// <returns>过滤条件表达式；<c>null</c> = 不过滤。角色按 OR 合并，用户只要持有一个对该实体、该操作
    /// <see cref="EntityRole.Filter"/> 留空的角色就整体不过滤，其它角色再窄也收不回去</returns>
    Task<System.Linq.Expressions.Expression<Func<TEntity, bool>>?> GetDataFilterAsync<TEntity>(Guid userId, DataAuthOperation operation)
        where TEntity : class;

    /// <summary>
    /// 检查用户是否有指定实体的数据权限
    /// </summary>
    /// <typeparam name="TEntity">实体类型</typeparam>
    /// <param name="userId">用户ID</param>
    /// <param name="entityId">实体ID</param>
    /// <param name="operation">操作类型</param>
    /// <returns>是否有权限。只支持 Guid 主键的实体（入参就是 Guid）：非 Guid 主键、没有 Id 属性、仓储未注册一律
    /// <c>false</c> 并记 Error，不抛</returns>
    Task<bool> CheckDataPermissionAsync<TEntity>(Guid userId, Guid entityId, DataAuthOperation operation)
        where TEntity : class;

    /// <summary>
    /// 通过实体类型名称检查数据权限（非泛型版本，用于 Admin API）：解析到 CLR 类型后与
    /// <see cref="CheckDataPermissionAsync{TEntity}"/> 同一判定 —— 那一行是否落在用户的过滤范围内。
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <param name="entityTypeName">实体类型名称（CLR 全名）</param>
    /// <param name="entityId">实体ID</param>
    /// <param name="operation">操作类型</param>
    /// <returns>该行是否在用户的数据范围内；类型未登记 404，登记了但本部署解析不到 CLR 类型 501，
    /// 实体主键不是 Guid 也 501（入参是 Guid，这次判定做不了）</returns>
    Task<Result<bool>> CheckDataPermissionByTypeNameAsync(Guid userId, string entityTypeName, Guid entityId, DataAuthOperation operation);

    #endregion

    #region EntityInfo 管理

    /// <summary>
    /// 获取实体信息
    /// </summary>
    /// <param name="entityTypeName">实体类型名称</param>
    /// <returns>实体信息</returns>
    Task<Result<EntityInfo>> GetEntityInfoAsync(string entityTypeName);

    /// <summary>
    /// 根据ID获取实体信息
    /// </summary>
    /// <param name="id">实体信息ID</param>
    /// <returns>实体信息</returns>
    Task<Result<EntityInfo>> GetEntityInfoByIdAsync(Guid id);

    /// <summary>
    /// 获取所有实体信息
    /// </summary>
    /// <returns>实体信息列表</returns>
    Task<Result<IEnumerable<EntityInfo>>> GetAllEntityInfosAsync();

    /// <summary>
    /// 创建实体信息
    /// </summary>
    /// <param name="request">实体信息</param>
    /// <returns>创建的实体信息</returns>
    Task<Result<EntityInfo>> CreateEntityInfoAsync(CreateEntityInfoRequest request);

    /// <summary>
    /// 更新实体信息
    /// </summary>
    /// <param name="id">实体信息ID</param>
    /// <param name="request">实体信息</param>
    /// <returns>更新后的实体信息</returns>
    Task<Result<EntityInfo>> UpdateEntityInfoAsync(Guid id, UpdateEntityInfoRequest request);

    /// <summary>
    /// 删除实体信息
    /// </summary>
    /// <param name="id">实体信息ID</param>
    Task<Result> DeleteEntityInfoAsync(Guid id);

    #endregion

    #region EntityRole 管理

    /// <summary>
    /// 获取用户的所有实体角色
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>实体角色集合</returns>
    Task<Result<IEnumerable<EntityRole>>> GetUserEntityRolesAsync(Guid userId);

    /// <summary>
    /// 根据ID获取实体角色
    /// </summary>
    /// <param name="id">实体角色ID</param>
    /// <returns>实体角色</returns>
    Task<Result<EntityRole>> GetEntityRoleByIdAsync(Guid id);

    /// <summary>
    /// 获取实体的所有角色配置
    /// </summary>
    /// <param name="entityInfoId">实体信息ID</param>
    /// <returns>实体角色列表</returns>
    Task<Result<IEnumerable<EntityRole>>> GetEntityRolesByEntityAsync(Guid entityInfoId);

    /// <summary>
    /// 获取角色的所有实体配置
    /// </summary>
    /// <param name="roleId">角色ID</param>
    /// <returns>实体角色列表</returns>
    Task<Result<IEnumerable<EntityRole>>> GetEntityRolesByRoleAsync(Guid roleId);

    /// <summary>
    /// 创建实体角色
    /// </summary>
    /// <param name="request">实体角色信息</param>
    /// <returns>创建的实体角色</returns>
    Task<Result<EntityRole>> CreateEntityRoleAsync(CreateEntityRoleRequest request);

    /// <summary>
    /// 更新实体角色
    /// </summary>
    /// <param name="id">实体角色ID</param>
    /// <param name="request">实体角色信息</param>
    /// <returns>更新后的实体角色</returns>
    Task<Result<EntityRole>> UpdateEntityRoleAsync(Guid id, UpdateEntityRoleRequest request);

    /// <summary>
    /// 删除实体角色
    /// </summary>
    /// <param name="id">实体角色ID</param>
    Task<Result> DeleteEntityRoleAsync(Guid id);

    /// <summary>
    /// 批量创建实体角色
    /// </summary>
    /// <param name="request">批量请求</param>
    /// <returns>创建的实体角色列表</returns>
    Task<Result<IEnumerable<EntityRole>>> BatchCreateEntityRolesAsync(BatchEntityRoleRequest request);

    #endregion
}

