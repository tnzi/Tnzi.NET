

namespace Tnzi.Authorization.DataAuth.Services;

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
    private readonly IEntityManager? _entityManager;

    /// <summary>
    /// 数据过滤缓存前缀
    /// </summary>
    public const string DataFilterCachePrefix = "DataFilter:";

    /// <summary>
    /// <see cref="EntityInfo"/> 写路径的版本键。<b>不带租户段</b>：EntityInfo 是全局表（无 <c>IMultiTenant</c>），
    /// 一次改动（尤其是 <see cref="EntityInfo.IsDataAuthEnabled"/> 这个总开关）影响所有租户，
    /// 只 bump 调用者租户段的版本会让其它租户的内存缓存最长 15 分钟继续用旧表达式。
    /// <see cref="EntityRole"/> 是按租户的表，它的写路径仍只 bump 调用者租户段（<see cref="ClearAllDataFilterCacheAsync"/>）。
    /// </summary>
    public const string EntityInfoVersionCacheKey = DataFilterCachePrefix + "EntityInfoVersion";

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
        IMemoryCache? memoryCache = null,
        IEntityManager? entityManager = null)
        : base(serviceProvider)
    {
        _entityInfoRepository = Check.NotNull(entityInfoRepository);
        _entityRoleRepository = Check.NotNull(entityRoleRepository);
        _userRoleService = userRoleService;
        _cache = cache;
        _memoryCache = memoryCache;
        _entityManager = entityManager;
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

        // 获取租户标识用于缓存隔离
        var tenantSegment = CurrentUser?.TenantId?.ToString("N") ?? "global";

        // 获取全局版本和用户版本，实现秒级缓存失效；EntityInfo 版本跨租户（见 EntityInfoVersionCacheKey）
        var globalVersion = _cache != null ? await _cache.GetAsync<int>($"{DataFilterCachePrefix}{tenantSegment}:GlobalVersion") : 0;
        var userVersion = _cache != null ? await _cache.GetAsync<int>($"{DataFilterCachePrefix}{tenantSegment}:UserVersion:{userId}") : 0;
        var entityInfoVersion = _cache != null ? await _cache.GetAsync<int>(EntityInfoVersionCacheKey) : 0;

        // 生成版本化缓存键（含租户隔离）
        var cacheKey = $"{DataFilterCachePrefix}{tenantSegment}:{globalVersion}:{userVersion}:{entityInfoVersion}:{userId}:{entityTypeName}:{(int)operation}";

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

        // 留空 = 这个角色对该实体、该操作不设限（合法、且是唯一的「不限制」写法）。
        // ★ 角色按 OR 合并，一个不设限的角色就是 OR 里的 true，别的角色再窄（或者坏掉、deny-all）
        // 也收不回去 —— 所以命中一条就整体不过滤。此前留空是 continue（「不贡献任何行」），
        // 持有不设限角色的用户反而被第二个角色限住；第二个角色的 Filter 坏了更是直接零行。
        // 方向是 OR 的既有语义，不是放宽：deny-on-fail 说的是坏角色不能给出多于配置的行，
        // 而不是把别的角色合法给出的行收走。
        if (entityRoles.Any(er => string.IsNullOrWhiteSpace(er.Filter)))
            return null;

        // 解析每个角色的Filter JSON并构建表达式
        var expressions = new List<Expression<Func<TEntity, bool>>>();

        foreach (var entityRole in entityRoles)
        {

            // ⚠️ 安全策略 (deny-on-fail): 一条写下了却读不出规则的 Filter —— 非法 JSON、
            // 或者解析成功但一条规则都没有（键名拼错就是这个形状）—— 这个角色对该实体的
            // 数据权限规则无效。我们*不能*跳过这条规则继续 OR 合并，否则该 role 的
            // "有限可见"会退化为"完全无过滤" → 用户拿到比配置更多的访问权限，且零症状。
            // 改为加入一个永远 false 的表达式，与其他 role 的过滤条件 OR 合并后效果
            // 等价于"这条 role 不贡献任何可见行"。配错的锅 admin 自己背，比静默扩大权限好。
            // ★ 解析走 FilterGroupJson（与列表 API 同一方言），不走会吞异常的 FromJsonString：
            // 后者对非法 JSON 返回 null、对 camelCase 返回零条规则，两条路都曾在这里被读成「跳过」。
            if (!FilterGroupJson.TryParse(entityRole.Filter, out var filterGroup, out var parseError))
            {
                Logger.LogWarning(
                    "Data filter for EntityRole {EntityRoleId} (role {RoleId}) cannot be parsed: {Error}. " +
                    "Treating as deny-all for safety; admin must fix the filter via the EntityRole admin UI. Filter: {Filter}",
                    entityRole.Id, entityRole.RoleId, parseError, entityRole.Filter);
                expressions.Add(DenyAll<TEntity>());
                continue;
            }

            if (!filterGroup!.HasFilters)
            {
                Logger.LogWarning(
                    "Data filter for EntityRole {EntityRoleId} (role {RoleId}) parsed but contains no rules. " +
                    "Treating as deny-all for safety; leave Filter blank for no restriction. Filter: {Filter}",
                    entityRole.Id, entityRole.RoleId, entityRole.Filter);
                expressions.Add(DenyAll<TEntity>());
                continue;
            }

            try
            {
                // 使用 FilterExpressionBuilder 构建表达式
                var expression = FilterExpressionBuilder.Build<TEntity>(filterGroup);
                expressions.Add(expression);
            }
            catch (Exception ex)
            {
                // 同上：属性名错 / 类型不匹配等表达式构建失败 → deny-on-fail。
                Logger.LogWarning(ex,
                    "Failed to build data filter for EntityRole {EntityRoleId} (role {RoleId}). " +
                    "Treating as deny-all for safety; admin must fix the filter via the EntityRole admin UI. Filter: {Filter}",
                    entityRole.Id, entityRole.RoleId, entityRole.Filter);
                expressions.Add(DenyAll<TEntity>());
            }
        }

        // 到这里每条角色都非空，且每条都贡献了一个表达式（合法过滤器或 deny-all），列表不可能为空；
        // 不再留「空列表 ⇒ null（不过滤）」的兜底 —— 那是一条方向为放开的静默路径。
        // 合并所有角色的过滤条件（使用OR连接）
        var result = CombineExpressionsWithOr(expressions);

        // 缓存表达式。共享 IMemoryCache 一旦被设了 SizeLimit（Caching:MemorySizeLimit），
        // 不带 Size 的写入会抛 InvalidOperationException —— 这里没有 try/catch，命中规则的每一次
        // 行级过滤都会变成 500。写共享缓存一律带 Size。
        _memoryCache?.Set(cacheKey, result, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = DataFilterCacheExpiration,
            Size = 1
        });

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
            var tenantSegment = CurrentUser?.TenantId?.ToString("N") ?? "global";
            var key = $"{DataFilterCachePrefix}{tenantSegment}:UserVersion:{userId}";
            var version = await _cache.GetAsync<int>(key);
            await _cache.SetAsync(key, version + 1, TimeSpan.FromDays(7));
        }
    }

    /// <summary>
    /// 清除所有数据过滤缓存（调用者所在租户段 —— EntityRole 写路径用）
    /// </summary>
    public async Task ClearAllDataFilterCacheAsync()
    {
        if (_cache != null)
        {
            var tenantSegment = CurrentUser?.TenantId?.ToString("N") ?? "global";
            var key = $"{DataFilterCachePrefix}{tenantSegment}:GlobalVersion";
            var version = await _cache.GetAsync<int>(key);
            await _cache.SetAsync(key, version + 1, TimeSpan.FromDays(7));
        }
    }

    /// <summary>
    /// 作废<b>所有租户</b>的数据过滤缓存 —— EntityInfo 写路径用（全局表，见 <see cref="EntityInfoVersionCacheKey"/>）。
    /// </summary>
    private async Task InvalidateEntityInfoCacheAsync()
    {
        if (_cache != null)
        {
            var version = await _cache.GetAsync<int>(EntityInfoVersionCacheKey);
            await _cache.SetAsync(EntityInfoVersionCacheKey, version + 1, TimeSpan.FromDays(7));
        }
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
            LogError("CheckDataPermissionAsync cannot check {EntityTypeName}: it has no Id property.", typeof(TEntity).FullName);
            return false;
        }

        // 入参是 Guid，非 Guid 主键（雪花 long / int / string）拼不出 Id == entityId：
        // Expression.Equal 会同步抛 InvalidOperationException，从 async 方法里出来就是一个 faulted task、端点 500。
        // 与「没有 Id 属性」「仓储未注册」同一方向：拒绝并记 Error，而不是抛。
        if (idProperty.PropertyType != typeof(Guid))
        {
            LogError(
                "CheckDataPermissionAsync cannot check {EntityTypeName}: its Id is {KeyType}, and the check only supports Guid-keyed entities.",
                typeof(TEntity).FullName, idProperty.PropertyType.Name);
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
    /// 通过实体类型名称检查数据权限（非泛型版本，用于 Admin API）。
    /// 把 <see cref="EntityInfo.TypeName"/> 解析成 CLR 类型后走与
    /// <see cref="CheckDataPermissionAsync{TEntity}"/> 逐字相同的判定：按用户的过滤范围 + Id 查一次 <c>AnyAsync</c>。
    /// </summary>
    /// <remarks>
    /// 此前除「未登记 404」外每条路径都 <c>Ok(true)</c>，包括「确实有命中的过滤规则」那条（当时的注释：不知道
    /// CLR 类型就执行不了表达式过滤）—— 一个恒真的授权判定 API。现在解析不到类型答 <b>501</b>：
    /// 登记存在但本部署没加载那个实体，这次判定做不了；不能答 true（放开且零症状），也不该答 404（登记明明在）。
    /// </remarks>
    public async Task<Result<bool>> CheckDataPermissionByTypeNameAsync(Guid userId, string entityTypeName, Guid entityId, DataAuthOperation operation)
    {
        // 查询 EntityInfo 确认实体类型已注册
        var entityInfoResult = await GetEntityInfoAsync(entityTypeName);
        if (!entityInfoResult.Succeeded || entityInfoResult.Data == null)
        {
            return Fail<bool>($"Entity type '{entityTypeName}' is not registered for data authorization", 404, ErrorCodes.RESOURCE_NOT_FOUND);
        }

        var entityType = ResolveEntityType(entityInfoResult.Data.TypeName);
        if (entityType == null || !entityType.IsClass)
        {
            return Fail<bool>(
                $"Entity type '{entityTypeName}' is registered but does not resolve to a loaded entity class on this deployment, so the data permission check cannot be performed.",
                501);
        }

        // 判定接受的是 Guid entityId；非 Guid 主键（雪花 long 是框架一等选项）的登记这次判定做不了。
        // 在这里、而不是等泛型方法按过滤范围拒绝：无规则命中时泛型方法答 true，管理员会把「true」读成
        // 「检查有效」，再加一条规则就变成拒绝 —— 对同一个实体类型，端点的回答必须是一致的 501。
        var keyType = entityType.GetProperty("Id")?.PropertyType;
        if (keyType != typeof(Guid))
        {
            return Fail<bool>(
                $"Entity type '{entityTypeName}' is keyed by '{keyType?.Name ?? "no Id property"}'; the data permission check accepts a Guid entityId and only supports Guid-keyed entities.",
                501);
        }

        var check = typeof(DataAuthService)
            .GetMethod(nameof(CheckDataPermissionAsync))!
            .MakeGenericMethod(entityType);
        var task = (Task<bool>)check.Invoke(this, [userId, entityId, operation])!;
        return Ok(await task);
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

        // A registration nothing can resolve is a configuration error, not a
        // row to keep: the filter path looks rows up by typeof(T).FullName, so
        // an assembly-qualified name would never be found (whole entity
        // unfiltered) and a name from a module this deployment does not load
        // can never have its filters validated.
        if (ResolveEntityType(request.TypeName) == null)
        {
            return Fail<EntityInfo>(
                $"EntityInfo.TypeName '{request.TypeName}' does not resolve to a loaded entity type; use the CLR full name (namespace + type, no assembly) of an entity this deployment loads.",
                400, ErrorCodes.VALIDATION_ERROR);
        }

        var entityInfo = request.MapTo<EntityInfo>();

        await _entityInfoRepository.InsertAsync(entityInfo);
        await InvalidateEntityInfoCacheAsync();
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

        // 如果启用状态改变，清除缓存 —— 全局表，必须作废所有租户段
        if (oldEnabled != request.IsDataAuthEnabled)
        {
            await InvalidateEntityInfoCacheAsync();
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

        // 清除缓存 —— 全局表，必须作废所有租户段
        await InvalidateEntityInfoCacheAsync();
        LogInformation("EntityInfo deleted: {TypeName}, Name: {Name}", entityInfo.TypeName, entityInfo.Name);
        return Ok("EntityInfo deleted successfully");
    }

    /// <summary>
    /// Save-time validation for an EntityRole's <c>Filter</c> JSON.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Null/empty filter is legal (= no row restriction for this role +
    /// operation). When supplied, we (1) deserialize as <see cref="FilterGroup"/>,
    /// (2) resolve <see cref="EntityInfo.TypeName"/> to a CLR type, and
    /// (3) trial-build the expression to surface bad property names /
    /// type mismatches as 400 errors *at save time*, not silent skips
    /// at query time.
    /// </para>
    /// <para>
    /// Type resolution goes through <see cref="ResolveEntityType"/> (EF-registered
    /// entity types first, then every loaded assembly, by CLR full name). A
    /// <see cref="EntityInfo"/> whose type cannot be resolved is a configuration
    /// error and the save is rejected: accepting it would store a filter this
    /// deployment can never check, which is deny-all at query time with only a
    /// warning to tell it from a typo.
    /// </para>
    /// </remarks>
    private Result ValidateFilter(string? filterJson, EntityInfo entityInfo)
    {
        // Empty filter = "no row restriction". Legitimate, don't reject.
        if (string.IsNullOrWhiteSpace(filterJson)) return Ok();

        // Reject over-long JSON here rather than letting the column do it: a
        // lenient MySQL truncates silently, and the truncated half is deny-all
        // at query time with nothing but a warning to tell it from a config error.
        if (filterJson.Length > EntityRole.FilterMaxLength)
        {
            return Fail($"Filter JSON exceeds {EntityRole.FilterMaxLength} characters.");
        }

        // Same dialect and the same three verdicts as the query side
        // (GetDataFilterAsync): malformed and "parsed but no rules" are both
        // rejected here, because at query time both are deny-all. Accepting
        // them would store a rule that silently shows the role zero rows.
        if (!FilterGroupJson.TryParse(filterJson, out var filterGroup, out var parseError))
        {
            return Fail(parseError ?? "Filter JSON is malformed.");
        }
        if (!filterGroup!.HasFilters)
        {
            return Fail("Filter JSON contains no rules; omit the Filter field for no restriction.");
        }

        // Resolve the target entity type by the same name the filter path
        // queries by (typeof(T).FullName). Type.GetType(bareName) used to sit
        // here: it only probes the calling assembly and CoreLib, so every
        // consumer entity resolved to null and the save was waved through.
        var entityType = ResolveEntityType(entityInfo.TypeName);
        if (entityType == null)
        {
            return Fail(
                $"EntityInfo.TypeName '{entityInfo.TypeName}' does not resolve to a loaded entity type, so the filter cannot be validated. Register the entity by its CLR full name in a deployment that loads it.");
        }

        // Reflectively call FilterExpressionBuilder.Build<entityType>(filterGroup).
        try
        {
            var buildMethod = typeof(FilterExpressionBuilder)
                .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .FirstOrDefault(m => m.Name == nameof(FilterExpressionBuilder.Build)
                    && m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType == typeof(FilterGroup));
            if (buildMethod == null)
            {
                return Fail("Internal: FilterExpressionBuilder.Build<T>(FilterGroup) not found.");
            }
            var generic = buildMethod.MakeGenericMethod(entityType);
            generic.Invoke(null, new object?[] { filterGroup });
            return Ok();
        }
        catch (System.Reflection.TargetInvocationException ex)
        {
            // Reflection wraps the real exception - unwrap for a useful message.
            var inner = ex.InnerException ?? ex;
            return Fail($"Filter expression failed to build against {entityType.FullName}: {inner.Message}");
        }
        catch (Exception ex)
        {
            return Fail($"Filter validation failed: {ex.Message}");
        }
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

        // Resolve EntityInfo to validate the filter against the target type.
        // EntityInfoId is not editable so we use the existing entityRole's
        // entity reference (avoids a second lookup in the validation path).
        var entityInfo = await _entityInfoRepository.FindAsync(entityRole.EntityInfoId);
        if (entityInfo == null)
        {
            // Shouldn't happen - entity row exists but its EntityInfo is
            // missing → corrupted state. Surface as 500 not 400.
            return Fail<EntityRole>(
                $"Owning EntityInfo {entityRole.EntityInfoId} not found.",
                500, ErrorCodes.INTERNAL_SERVER_ERROR);
        }

        var filterValidation = ValidateFilter(request.Filter, entityInfo);
        if (!filterValidation.Succeeded)
        {
            return Fail<EntityRole>(filterValidation.Message ?? "Invalid filter expression",
                400, ErrorCodes.VALIDATION_ERROR);
        }

        request.MapTo(entityRole);

        await _entityRoleRepository.UpdateAsync(entityRole);

        // 清除缓存
        await ClearAllDataFilterCacheAsync();

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
        await ClearAllDataFilterCacheAsync();
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

        // Fail-fast on a bad Filter so the admin sees the error in the form,
        // not silently at query time. Without this an invalid filter would
        // be silently dropped on every query → user gets MORE access than
        // intended (violates deny-by-default).
        var filterValidation = ValidateFilter(request.Filter, entityInfo);
        if (!filterValidation.Succeeded)
        {
            return Fail<EntityRole>(filterValidation.Message ?? "Invalid filter expression",
                400, ErrorCodes.VALIDATION_ERROR);
        }

        var entityRole = request.MapTo<EntityRole>();
        entityRole.IsEnabled = true;

        await _entityRoleRepository.InsertAsync(entityRole);

        // 清除缓存
        await ClearAllDataFilterCacheAsync();

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

        // Same save-time gate as CreateEntityRoleAsync: one shared Filter for
        // every role, so validate once before anything is inserted. Without
        // this the batch endpoint was the bypass for the single-create 400.
        var filterValidation = ValidateFilter(request.Filter, entityInfo);
        if (!filterValidation.Succeeded)
        {
            return Fail<IEnumerable<EntityRole>>(filterValidation.Message ?? "Invalid filter expression",
                400, ErrorCodes.VALIDATION_ERROR);
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
            await ClearAllDataFilterCacheAsync();
        }

        LogInformation("Batch created {Count} EntityRoles for EntityInfoId: {EntityInfoId}", rolesToCreate.Count, request.EntityInfoId);
        return Ok((IEnumerable<EntityRole>)rolesToCreate, $"Batch created {rolesToCreate.Count} EntityRoles");
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// 把 <see cref="EntityInfo.TypeName"/>（CLR 全名：命名空间 + 类型名，<b>不带程序集</b>，与
    /// <c>typeof(T).FullName</c> 逐字相同 —— 过滤路径就按它等值查表）解析成 CLR 类型。
    /// </summary>
    /// <remarks>
    /// 先问 EF 登记过的实体类型集合（最准，只认真的实体），再退到已加载程序集逐个按全名找。
    /// ★ 不能用 <c>Type.GetType(裸全名)</c>：它只探测调用方程序集与 CoreLib，对任何住在别的程序集里的
    /// 实体恒为 null；而反过来存 AssemblyQualifiedName 会让按 FullName 的等值查询永不命中、整个实体不过滤。
    /// 两种格式互斥，这里钉死裸全名。
    /// </remarks>
    private Type? ResolveEntityType(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        var registered = _entityManager?.GetAllEntityTypes()
            .FirstOrDefault(t => string.Equals(t.FullName, typeName, StringComparison.Ordinal));
        if (registered != null)
        {
            return registered;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic)
            {
                continue;
            }

            var type = assembly.GetType(typeName, throwOnError: false);
            if (type != null)
            {
                return type;
            }
        }

        return null;
    }

    /// <summary>deny-on-fail 用的恒 false 表达式：与其它角色的条件 OR 合并后，这条规则不贡献任何可见行。</summary>
    private static Expression<Func<TEntity, bool>> DenyAll<TEntity>() where TEntity : class
    {
        var param = Expression.Parameter(typeof(TEntity), "e");
        return Expression.Lambda<Func<TEntity, bool>>(Expression.Constant(false), param);
    }

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