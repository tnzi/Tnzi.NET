namespace Tnzi.Storage.Services;

/// <summary>
/// 默认的孤立引用验证器实现。
///
/// 通过 <see cref="IEntityManager"/> 把引用记录上的实体类型名（FileReference.EntityType，如 "User"）
/// 解析为已注册的 CLR 实体类型，再定位其所属 DbContext，按主键查询该实体是否仍物理存在。
///
/// 保守策略（绝不误删）：当无法把名称解析为唯一实体类型、无法确定 DbContext、查询抛错或任何
/// 不确定情形时，一律返回 true（视为"实体仍存在"），从而避免误删仍被引用的文件。
/// 应用可注册自己的 <see cref="IOrphanReferenceValidator"/> 实现覆盖此默认行为（模块使用 TryAdd 注册）。
/// </summary>
public class EntityManagerOrphanReferenceValidator : IOrphanReferenceValidator
{
    private readonly IEntityManager _entityManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EntityManagerOrphanReferenceValidator> _logger;

    /// <summary>
    /// 初始化 <see cref="EntityManagerOrphanReferenceValidator"/>
    /// </summary>
    public EntityManagerOrphanReferenceValidator(
        IEntityManager entityManager,
        IServiceProvider serviceProvider,
        ILogger<EntityManagerOrphanReferenceValidator> logger)
    {
        _entityManager = Check.NotNull(entityManager);
        _serviceProvider = Check.NotNull(serviceProvider);
        _logger = Check.NotNull(logger);
    }

    /// <inheritdoc />
    public async Task<bool> IsEntityExistsAsync(string entityType, Guid entityId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(entityType))
        {
            // 无类型信息无法判断，保守视为存在
            return true;
        }

        try
        {
            // 1. 把类型名解析为已注册的 CLR 实体类型（按短名 / 全名匹配，区分大小写）
            var clrType = ResolveEntityType(entityType);
            if (clrType == null)
            {
                _logger.LogDebug(
                    "Orphan reference validator could not resolve entity type '{EntityType}', treating as existing (conservative)",
                    entityType);
                return true;
            }

            // 2. 定位该实体所属的 DbContext 类型并解析实例
            Type dbContextType;
            try
            {
                dbContextType = _entityManager.GetDbContextTypeForEntity(clrType);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "Orphan reference validator could not determine DbContext for entity type '{EntityType}', treating as existing (conservative)",
                    entityType);
                return true;
            }

            if (_serviceProvider.GetService(dbContextType) is not DbContext dbContext)
            {
                _logger.LogDebug(
                    "Orphan reference validator could not resolve DbContext '{DbContextType}', treating as existing (conservative)",
                    dbContextType.Name);
                return true;
            }

            // 3. 按主键查询是否存在（FindAsync 会先查本地缓存再查库；忽略软删除过滤器，
            //    软删除实体仍算"存在"=保守不删，符合"绝不误删"原则）
            var found = await dbContext.FindAsync(clrType, [entityId], cancellationToken);
            return found != null;
        }
        catch (Exception ex)
        {
            // 任何意外都保守返回 true，绝不误删
            _logger.LogWarning(ex,
                "Orphan reference validator failed for entity '{EntityType}/{EntityId}', treating as existing (conservative)",
                entityType, entityId);
            return true;
        }
    }

    /// <summary>
    /// 把实体类型名解析为唯一的已注册 CLR 实体类型。
    /// 优先精确匹配短名（Type.Name），再退化到全名（FullName）。
    /// 若多个类型同名（歧义），返回 null（保守）。
    /// </summary>
    private Type? ResolveEntityType(string entityType)
    {
        var allTypes = _entityManager.GetAllEntityTypes();
        if (allTypes.Length == 0)
        {
            return null;
        }

        // 精确短名匹配
        var byShortName = allTypes
            .Where(t => string.Equals(t.Name, entityType, StringComparison.Ordinal))
            .ToList();
        if (byShortName.Count == 1)
        {
            return byShortName[0];
        }
        if (byShortName.Count > 1)
        {
            // 同名歧义：无法安全判断属于哪个实体，保守放弃
            _logger.LogDebug(
                "Orphan reference validator found multiple entity types named '{EntityType}', cannot disambiguate",
                entityType);
            return null;
        }

        // 退化到全名匹配
        var byFullName = allTypes
            .Where(t => string.Equals(t.FullName, entityType, StringComparison.Ordinal))
            .ToList();
        return byFullName.Count == 1 ? byFullName[0] : null;
    }
}
