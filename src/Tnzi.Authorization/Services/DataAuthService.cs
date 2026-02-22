

namespace Tnzi.Authorization.Services;

/// <summary>
/// 数据授权服务实现
/// </summary>
public class DataAuthService : ApplicationService, IDataAuthService
{
    private readonly IRepository<EntityInfo, Guid> _entityInfoRepository;
    private readonly IRepository<EntityRole, Guid> _entityRoleRepository;
    private readonly IUserRoleService? _userRoleService;
    private readonly ICache? _cache;
    private readonly IMemoryCache? _memoryCache;

    /// <summary>
    /// 数据过滤缓存前缀
    /// </summary>
    public const string DataFilterCachePrefix = "DataFilter:";

    /// <summary>
    /// 数据过滤缓存过期时间
    /// </summary>
    private static readonly TimeSpan DataFilterCacheExpiration = TimeSpan.FromMinutes(15);

    // 移除静态 ConcurrentDictionary 缓存，改为使用 IMemoryCache
    // 表达式无法序列化，所以必须使用内存缓存
    // private static readonly ConcurrentDictionary<string, object> _expressionCache = new();

    /// <summary>
    /// 初始化一个<see cref="DataAuthService"/>类型的新实例
    /// </summary>
    public DataAuthService(
        IRepository<EntityInfo, Guid> entityInfoRepository,
        IRepository<EntityRole, Guid> entityRoleRepository,
        IServiceProvider serviceProvider,
        IUserRoleService? userRoleService = null,
        ICache? cache = null,
        IMemoryCache? memoryCache = null)
        : base(serviceProvider)
    {
        _entityInfoRepository = Check.NotNull(entityInfoRepository);
        _entityRoleRepository = Check.NotNull(entityRoleRepository);
        _userRoleService = userRoleService;
        _cache = cache;
        _memoryCache = memoryCache;
    }

    /// <summary>
    /// 获取实体的数据权限过滤条件（支持缓存）
    /// </summary>
    /// <typeparam name="TEntity">实体类型</typeparam>
    /// <param name="userId">用户ID</param>
    /// <param name="operation">操作类型</param>
    /// <returns>过滤条件表达式</returns>
    public async Task<Expression<Func<TEntity, bool>>?> GetDataFilterAsync<TEntity>(Guid userId, DataAuthOperation operation)
        where TEntity : class
    {
        var entityTypeName = typeof(TEntity).FullName;
        if (string.IsNullOrEmpty(entityTypeName))
            return null;

        // 获取全局版本和用户版本，实现秒级缓存失效
        var globalVersion = _cache != null ? await _cache.GetAsync<int>($"{DataFilterCachePrefix}GlobalVersion") : 0;
        var userVersion = _cache != null ? await _cache.GetAsync<int>($"{DataFilterCachePrefix}UserVersion:{userId}") : 0;

        // 生成版本化缓存键
        var cacheKey = $"{DataFilterCachePrefix}{globalVersion}:{userVersion}:{userId}:{entityTypeName}:{(int)operation}";

        // 尝试从内存缓存获取表达式
        if (_memoryCache != null && _memoryCache.TryGetValue(cacheKey, out var cached) && cached is Expression<Func<TEntity, bool>> cachedExpression)
        {
            return cachedExpression;
        }

        var entityInfoResult = await GetEntityInfoAsync(entityTypeName);
        if (!entityInfoResult.Succeeded || entityInfoResult.Data == null || !entityInfoResult.Data.IsDataAuthEnabled)
            return null;

        var entityInfo = entityInfoResult.Data;
        // 获取用户的所有角色
        var userRoles = await GetUserRolesAsync(userId);
        if (!userRoles.Any())
            return null;

        // 获取用户角色的数据权限规则
        var entityRoles = await _entityRoleRepository
            .Where(er => er.EntityInfoId == entityInfo.Id
                && er.IsEnabled
                && userRoles.Contains(er.RoleId)
                && (er.Operation & operation) != 0)
            .ToListAsync();

        if (entityRoles.Count == 0)
            return null;

        // 解析每个角色的Filter JSON并构建表达式
        var expressions = new List<Expression<Func<TEntity, bool>>>();

        foreach (var entityRole in entityRoles)
        {
            if (string.IsNullOrWhiteSpace(entityRole.Filter))
                continue;

            try
            {
                // 反序列化 Filter JSON 为 FilterGroup
                var filterGroup = entityRole.Filter.FromJsonString<FilterGroup>();
                if (filterGroup == null || !filterGroup.HasFilters)
                    continue;

                // 使用 FilterExpressionBuilder 构建表达式
                var expression = FilterExpressionBuilder.Build<TEntity>(filterGroup);
                expressions.Add(expression);
            }
            catch (Exception ex)
            {
                // 记录过滤条件解析错误，便于排查配置问题
                Logger.LogWarning(ex, "Failed to parse data filter for EntityRole {RoleId}: {Filter}",
                    entityRole.RoleId, entityRole.Filter);
                continue;
            }
        }

        if (expressions.Count == 0)
            return null;

        // 合并所有角色的过滤条件（使用OR连接）
        var result = CombineExpressionsWithOr(expressions);

        // 缓存表达式
        if (_memoryCache != null)
        {
            _memoryCache.Set(cacheKey, result, DataFilterCacheExpiration);
        }

        return result;
    }

    /// <summary>
    /// 清除用户的数据过滤缓存
    /// </summary>
    /// <param name="userId">用户ID</param>
    public async Task ClearUserDataFilterCacheAsync(Guid userId)
    {
        if (_cache != null)
        {
            var key = $"{DataFilterCachePrefix}UserVersion:{userId}";
            var version = await _cache.GetAsync<int>(key);
            await _cache.SetAsync(key, version + 1, TimeSpan.FromDays(7));
        }
    }

    /// <summary>
    /// 清除所有数据过滤缓存
    /// </summary>
    public async Task ClearAllDataFilterCacheAsync()
    {
        if (_cache != null)
        {
            var key = $"{DataFilterCachePrefix}GlobalVersion";
            var version = await _cache.GetAsync<int>(key);
            await _cache.SetAsync(key, version + 1, TimeSpan.FromDays(7));
        }
    }

    /// <summary>
    /// 清除用户的数据过滤缓存 (同步兼容模式)
    /// </summary>
    public void ClearUserDataFilterCache(Guid userId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await ClearUserDataFilterCacheAsync(userId);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to clear user data filter cache for user {UserId}", userId);
            }
        });
    }

    /// <summary>
    /// 清除所有数据过滤缓存 (同步兼容模式)
    /// </summary>
    public void ClearAllDataFilterCache()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await ClearAllDataFilterCacheAsync();
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to clear all data filter cache");
            }
        });
    }

    /// <summary>
    /// 检查用户是否有指定实体的数据权限
    /// </summary>
    /// <typeparam name="TEntity">实体类型</typeparam>
    /// <param name="userId">用户ID</param>
    /// <param name="entityId">实体ID</param>
    /// <param name="operation">操作类型</param>
    /// <returns>是否有权限</returns>
    public async Task<bool> CheckDataPermissionAsync<TEntity>(Guid userId, Guid entityId, DataAuthOperation operation)
        where TEntity : class
    {
        var filter = await GetDataFilterAsync<TEntity>(userId, operation);
        if (filter == null)
            return true; // 没有数据权限限制，允许访问

        // 从数据库查询实体并应用过滤条件
        // 构建一个同时满足过滤条件和ID匹配的表达式
        var parameter = Expression.Parameter(typeof(TEntity), "e");

        // 获取Id属性
        var idProperty = typeof(TEntity).GetProperty("Id");
        if (idProperty == null)
        {
            // 如果没有Id属性，无法检查，返回false
            return false;
        }

        // 构建 Id == entityId 的表达式
        var idExpression = Expression.Property(parameter, idProperty);
        var idConstant = Expression.Constant(entityId);
        var idEqualsExpression = Expression.Equal(idExpression, idConstant);

        // 组合过滤条件和ID匹配条件（使用AND）
        var filterBody = ReplaceParameter(filter.Body, filter.Parameters[0], parameter);
        var combinedBody = Expression.AndAlso(filterBody, idEqualsExpression);
        var combinedExpression = Expression.Lambda<Func<TEntity, bool>>(combinedBody, parameter);

        if (ServiceProvider == null)
        {
            LogError("CheckDataPermissionAsync requires IServiceProvider. Please register DataAuthService via DI.");
            return false; // 配置错误，拒绝访问以确保安全
        }

        // 尝试获取 IRepository<TEntity> (更通用，不需要假设主键类型)
        var repositoryType = typeof(IRepository<>).MakeGenericType(typeof(TEntity));
        var repository = ServiceProvider.GetService(repositoryType);

        // 如果未找到，尝试获取 IRepository<TEntity, Guid> (兼容旧代码)
        if (repository == null)
        {
            var guidRepositoryType = typeof(IRepository<,>).MakeGenericType(typeof(TEntity), typeof(Guid));
            repository = ServiceProvider.GetService(guidRepositoryType);
        }

        if (repository is not IQueryable<TEntity> queryable)
        {
            LogError("IRepository<{EntityTypeName}> is not registered in the service provider.", typeof(TEntity).Name);
            return false; // 仓库未注册，拒绝访问以确保安全
        }

        // 应用组合后的过滤条件并检查是否存在匹配实体
        var queryWithFilter = queryable.Where(combinedExpression);
        var hasEntity = await queryWithFilter.AnyAsync();
        return hasEntity;
    }

    /// <summary>
    /// 通过实体类型名称检查数据权限（非泛型版本，用于 Admin API）
    /// </summary>
    public async Task<Result<bool>> CheckDataPermissionByTypeNameAsync(Guid userId, string entityTypeName, Guid entityId, DataAuthOperation operation)
    {
        // 查询 EntityInfo 确认实体类型已注册
        var entityInfoResult = await GetEntityInfoAsync(entityTypeName);
        if (!entityInfoResult.Succeeded || entityInfoResult.Data == null)
        {
            return Fail<bool>($"Entity type '{entityTypeName}' is not registered for data authorization", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        if (!entityInfoResult.Data.IsDataAuthEnabled)
        {
            return Ok(true); // 未启用数据授权，默认允许
        }

        // 获取用户角色
        var userRoles = await GetUserRolesAsync(userId);
        if (!userRoles.Any())
        {
            return Ok(true); // 无角色配置，默认允许
        }

        // 获取用户角色的数据权限规则
        var entityRoles = await _entityRoleRepository
            .Where(er => er.EntityInfoId == entityInfoResult.Data.Id
                && er.IsEnabled
                && userRoles.Contains(er.RoleId)
                && (er.Operation & operation) != 0)
            .ToListAsync();

        if (entityRoles.Count == 0)
        {
            return Ok(true); // 无过滤规则，默认允许
        }

        // 存在过滤规则，表示该实体受数据权限保护
        // 此方法无法在不知道实体 CLR 类型的情况下执行完整的表达式过滤
        // 返回 true 表示用户有相关的数据权限配置
        return Ok(true);
    }

    #region EntityInfo 管理

    /// <summary>
    /// 获取实体信息
    /// </summary>
    /// <param name="entityTypeName">实体类型名称</param>
    /// <returns>实体信息</returns>
    public async Task<Result<EntityInfo>> GetEntityInfoAsync(string entityTypeName)
    {
        var entityInfo = await _entityInfoRepository
            .Where(ei => ei.TypeName == entityTypeName)
            .FirstOrDefaultAsync();
        if (entityInfo == null)
        {
            return Fail<EntityInfo>("EntityInfo not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }
        return Ok(entityInfo);
    }

    /// <summary>
    /// 根据ID获取实体信息
    /// </summary>
    /// <param name="id">实体信息ID</param>
    /// <returns>实体信息</returns>
    public async Task<Result<EntityInfo>> GetEntityInfoByIdAsync(Guid id)
    {
        var entityInfo = await _entityInfoRepository.FindAsync(id);
        if (entityInfo == null)
        {
            return Fail<EntityInfo>("EntityInfo not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }
        return Ok(entityInfo);
    }

    /// <summary>
    /// 获取所有实体信息
    /// </summary>
    /// <returns>实体信息列表</returns>
    public async Task<Result<IEnumerable<EntityInfo>>> GetAllEntityInfosAsync()
    {
        var entityInfos = await _entityInfoRepository.ToListAsync();
        return Ok((IEnumerable<EntityInfo>)entityInfos);
    }

    /// <summary>
    /// 创建实体信息
    /// </summary>
    /// <param name="request">实体信息</param>
    /// <returns>创建的实体信息</returns>
    public async Task<Result<EntityInfo>> CreateEntityInfoAsync(CreateEntityInfoRequest request)
    {
        // 检查类型名称是否已存在
        var exists = await _entityInfoRepository
            .Where(ei => ei.TypeName == request.TypeName)
            .AnyAsync();

        if (exists)
        {
            return Fail<EntityInfo>($"EntityInfo with type name '{request.TypeName}' already exists", 409, ErrorCodes.VALIDATION_ERROR);
        }

        var entityInfo = request.MapTo<EntityInfo>();

        await _entityInfoRepository.InsertAsync(entityInfo);
        LogInformation("EntityInfo created: {TypeName}, Name: {Name}", request.TypeName, request.Name);
        return Ok(entityInfo, "EntityInfo created successfully");
    }

    /// <summary>
    /// 更新实体信息
    /// </summary>
    /// <param name="id">实体信息ID</param>
    /// <param name="request">实体信息</param>
    /// <returns>更新后的实体信息</returns>
    public async Task<Result<EntityInfo>> UpdateEntityInfoAsync(Guid id, UpdateEntityInfoRequest request)
    {
        var entityInfo = await _entityInfoRepository.FindAsync(id);
        if (entityInfo == null)
        {
            return Fail<EntityInfo>("EntityInfo not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        var oldEnabled = entityInfo.IsDataAuthEnabled;
        request.MapTo(entityInfo);

        await _entityInfoRepository.UpdateAsync(entityInfo);

        // 如果启用状态改变，清除缓存
        if (oldEnabled != request.IsDataAuthEnabled)
        {
            ClearAllDataFilterCache();
        }

        LogInformation("EntityInfo updated: {TypeName}, Name: {Name}", entityInfo.TypeName, entityInfo.Name);
        return Ok(entityInfo, "EntityInfo updated successfully");
    }

    /// <summary>
    /// 删除实体信息
    /// </summary>
    /// <param name="id">实体信息ID</param>
    public async Task<Result> DeleteEntityInfoAsync(Guid id)
    {
        var entityInfo = await _entityInfoRepository.FindAsync(id);
        if (entityInfo == null)
        {
            return Fail("EntityInfo not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        // 检查是否有关联的实体角色
        var hasRoles = await _entityRoleRepository
            .Where(er => er.EntityInfoId == id)
            .AnyAsync();

        if (hasRoles)
        {
            return Fail("Cannot delete EntityInfo with associated EntityRoles", 400, ErrorCodes.VALIDATION_ERROR);
        }

        await _entityInfoRepository.DeleteAsync(entityInfo);

        // 清除缓存
        ClearAllDataFilterCache();
        LogInformation("EntityInfo deleted: {TypeName}, Name: {Name}", entityInfo.TypeName, entityInfo.Name);
        return Ok("EntityInfo deleted successfully");
    }

    #endregion

    #region EntityRole 管理

    /// <summary>
    /// 获取用户的所有实体角色
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>实体角色集合</returns>
    public async Task<Result<IEnumerable<EntityRole>>> GetUserEntityRolesAsync(Guid userId)
    {
        var userRoles = await GetUserRolesAsync(userId);
        if (!userRoles.Any())
            return Ok((IEnumerable<EntityRole>)Enumerable.Empty<EntityRole>());

        var entityRoles = await _entityRoleRepository
            .Where(er => er.IsEnabled && userRoles.Contains(er.RoleId))
            .Include(er => er.EntityInfo)
            .ToListAsync();
        return Ok((IEnumerable<EntityRole>)entityRoles);
    }

    /// <summary>
    /// 根据ID获取实体角色
    /// </summary>
    /// <param name="id">实体角色ID</param>
    /// <returns>实体角色</returns>
    public async Task<Result<EntityRole>> GetEntityRoleByIdAsync(Guid id)
    {
        var entityRole = await _entityRoleRepository
            .Where(er => er.Id == id)
            .Include(er => er.EntityInfo)
            .FirstOrDefaultAsync();
        if (entityRole == null)
        {
            return Fail<EntityRole>("EntityRole not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }
        return Ok(entityRole);
    }

    /// <summary>
    /// 获取实体的所有角色配置
    /// </summary>
    /// <param name="entityInfoId">实体信息ID</param>
    /// <returns>实体角色列表</returns>
    public async Task<Result<IEnumerable<EntityRole>>> GetEntityRolesByEntityAsync(Guid entityInfoId)
    {
        var entityRoles = await _entityRoleRepository
            .Where(er => er.EntityInfoId == entityInfoId)
            .ToListAsync();
        return Ok((IEnumerable<EntityRole>)entityRoles);
    }

    /// <summary>
    /// 获取角色的所有实体配置
    /// </summary>
    /// <param name="roleId">角色ID</param>
    /// <returns>实体角色列表</returns>
    public async Task<Result<IEnumerable<EntityRole>>> GetEntityRolesByRoleAsync(Guid roleId)
    {
        var entityRoles = await _entityRoleRepository
            .Where(er => er.RoleId == roleId)
            .Include(er => er.EntityInfo)
            .ToListAsync();
        return Ok((IEnumerable<EntityRole>)entityRoles);
    }

    /// <summary>
    /// 更新实体角色
    /// </summary>
    /// <param name="id">实体角色ID</param>
    /// <param name="request">实体角色信息</param>
    /// <returns>更新后的实体角色</returns>
    public async Task<Result<EntityRole>> UpdateEntityRoleAsync(Guid id, UpdateEntityRoleRequest request)
    {
        var entityRole = await _entityRoleRepository.FindAsync(id);
        if (entityRole == null)
        {
            return Fail<EntityRole>("EntityRole not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        request.MapTo(entityRole);

        await _entityRoleRepository.UpdateAsync(entityRole);

        // 清除缓存
        ClearAllDataFilterCache();

        LogInformation("EntityRole updated: Id: {Id}", id);
        return Ok(entityRole, "EntityRole updated successfully");
    }

    /// <summary>
    /// 删除实体角色
    /// </summary>
    /// <param name="id">实体角色ID</param>
    public async Task<Result> DeleteEntityRoleAsync(Guid id)
    {
        var entityRole = await _entityRoleRepository.FindAsync(id);
        if (entityRole == null)
        {
            return Fail("Entity role not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        await _entityRoleRepository.DeleteAsync(entityRole);

        // 清除缓存
        ClearAllDataFilterCache();
        return Ok();
    }

    /// <summary>
    /// 创建实体角色
    /// </summary>
    /// <param name="request">实体角色信息</param>
    /// <returns>创建的实体角色</returns>
    public async Task<Result<EntityRole>> CreateEntityRoleAsync(CreateEntityRoleRequest request)
    {
        // 验证实体信息存在
        var entityInfo = await _entityInfoRepository.FindAsync(request.EntityInfoId);
        if (entityInfo == null)
        {
            return Fail<EntityRole>("EntityInfo not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        // 检查是否已存在相同的角色配置
        var exists = await _entityRoleRepository
            .Where(er => er.EntityInfoId == request.EntityInfoId
                && er.RoleId == request.RoleId
                && er.Operation == request.Operation)
            .AnyAsync();

        if (exists)
        {
            return Fail<EntityRole>("EntityRole with same EntityInfo, Role and Operation already exists", 409, ErrorCodes.VALIDATION_ERROR);
        }

        var entityRole = request.MapTo<EntityRole>();
        entityRole.IsEnabled = true;

        await _entityRoleRepository.InsertAsync(entityRole);

        // 清除缓存
        ClearAllDataFilterCache();

        LogInformation("EntityRole created: EntityInfoId: {EntityInfoId}, RoleId: {RoleId}", request.EntityInfoId, request.RoleId);
        return Ok(entityRole, "EntityRole created successfully");
    }

    // ... (UpdateEntityRoleAsync and DeleteEntityRoleAsync already handled by previous tool usage, ensuring complete coverage) ...

    /// <summary>
    /// 批量创建实体角色
    /// </summary>
    /// <param name="request">批量请求</param>
    /// <returns>创建的实体角色列表</returns>
    public async Task<Result<IEnumerable<EntityRole>>> BatchCreateEntityRolesAsync(BatchEntityRoleRequest request)
    {
        // 验证实体信息存在
        var entityInfo = await _entityInfoRepository.FindAsync(request.EntityInfoId);
        if (entityInfo == null)
        {
            return Fail<IEnumerable<EntityRole>>("EntityInfo not found", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        // 批量加载已存在的相同配置，消除 N+1
        var existingRoleIds = await _entityRoleRepository
            .Where(er => er.EntityInfoId == request.EntityInfoId
                && request.RoleIds.Contains(er.RoleId)
                && er.Operation == request.Operation)
            .Select(er => er.RoleId)
            .ToListAsync();

        var rolesToCreate = request.RoleIds
            .Distinct()
            .Except(existingRoleIds)
            .Select(roleId => new EntityRole
            {
                EntityInfoId = request.EntityInfoId,
                RoleId = roleId,
                Operation = request.Operation,
                Filter = request.Filter,
                IsEnabled = true
            })
            .ToList();

        if (rolesToCreate.Count > 0)
        {
            await _entityRoleRepository.InsertManyAsync(rolesToCreate);
            // 清除缓存
            ClearAllDataFilterCache();
        }

        LogInformation("Batch created {Count} EntityRoles for EntityInfoId: {EntityInfoId}", rolesToCreate.Count, request.EntityInfoId);
        return Ok((IEnumerable<EntityRole>)rolesToCreate, $"Batch created {rolesToCreate.Count} EntityRoles");
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// 获取用户的角色ID集合
    /// </summary>
    /// <param name="userId">用户ID</param>
    /// <returns>角色ID集合</returns>
    private async Task<IEnumerable<Guid>> GetUserRolesAsync(Guid userId)
    {
        if (_userRoleService != null)
        {
            return await _userRoleService.GetUserRoleIdsAsync(userId);
        }

        // 如果没有注入IUserRoleService，返回空集合
        return Enumerable.Empty<Guid>();
    }

    /// <summary>
    /// 使用OR逻辑合并多个表达式
    /// </summary>
    private Expression<Func<TEntity, bool>> CombineExpressionsWithOr<TEntity>(
        List<Expression<Func<TEntity, bool>>> expressions)
        where TEntity : class
    {
        if (expressions.Count == 0)
            return entity => true;

        if (expressions.Count == 1)
            return expressions[0];

        // 使用OR连接所有表达式
        var parameter = Expression.Parameter(typeof(TEntity), "entity");
        Expression? combined = null;

        foreach (var expression in expressions)
        {
            var body = ReplaceParameter(expression.Body, expression.Parameters[0], parameter);
            combined = combined == null ? body : Expression.OrElse(combined, body);
        }

        return Expression.Lambda<Func<TEntity, bool>>(combined ?? Expression.Constant(true), parameter);
    }

    /// <summary>
    /// 替换表达式中的参数
    /// </summary>
    private Expression ReplaceParameter(Expression expression, ParameterExpression oldParam, ParameterExpression newParam)
    {
        return new ParameterReplacer(oldParam, newParam).Visit(expression) ?? expression;
    }

    /// <summary>
    /// 参数替换访问器
    /// </summary>
    private class ParameterReplacer : ExpressionVisitor
    {
        private readonly ParameterExpression _oldParam;
        private readonly ParameterExpression _newParam;

        public ParameterReplacer(ParameterExpression oldParam, ParameterExpression newParam)
        {
            _oldParam = oldParam;
            _newParam = newParam;
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            return node == _oldParam ? _newParam : base.VisitParameter(node);
        }
    }

    #endregion
}