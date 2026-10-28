namespace Tnzi.Storage.Services;

/// <summary>
/// 默认的孤立引用验证器实现。
///
/// 通过 <see cref="IEntityManager"/> 把引用记录上的实体类型名（FileReference.EntityType，如 "User"）
/// 解析为已注册的 CLR 实体类型，再定位其所属 DbContext，按主键查询该实体是否仍物理存在
/// （软删除的记录算存在：它可以被恢复，它的引用行在软删除时是刻意保留的）。
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

            // 3. 按主键查询是否存在，不受全局查询过滤器约束：软删的实体仍算「存在」（可能被恢复，保守不删）。
            //    ★ 不能用 FindAsync：它查库时套用全局查询过滤器，软删的行查不到，会被判成孤立、文件随后被物理删除。
            //    租户过滤器一并忽略：Id 是全局唯一的 Guid，任一租户里有这一行就是这个实体。
            var keyName = ResolveGuidKeyName(dbContext, clrType);
            if (keyName == null)
            {
                _logger.LogDebug(
                    "Orphan reference validator found no single Guid key on '{EntityType}', treating as existing (conservative)",
                    entityType);
                return true;
            }

            var exists = (Task<bool>)ExistsIgnoringQueryFiltersMethod.MakeGenericMethod(clrType)
                .Invoke(null, [dbContext, keyName, entityId, cancellationToken])!;
            return await exists;
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

    private static readonly MethodInfo ExistsIgnoringQueryFiltersMethod = typeof(EntityManagerOrphanReferenceValidator)
        .GetMethod(nameof(ExistsIgnoringQueryFiltersAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>主键是单列 Guid 时返回其属性名（<c>[FileField]</c> 只支持 Guid 主键的实体）；否则 null。</summary>
    private static string? ResolveGuidKeyName(DbContext dbContext, Type clrType)
    {
        var key = dbContext.Model.FindEntityType(clrType)?.FindPrimaryKey();
        if (key is not { Properties.Count: 1 })
        {
            return null;
        }

        var property = key.Properties[0];
        return property.ClrType == typeof(Guid) ? property.Name : null;
    }

    private static Task<bool> ExistsIgnoringQueryFiltersAsync<TEntity>(
        DbContext dbContext, string keyName, Guid entityId, CancellationToken cancellationToken)
        where TEntity : class
        => dbContext.Set<TEntity>()
            .IgnoreQueryFilters()
            .AnyAsync(e => EF.Property<Guid>(e, keyName) == entityId, cancellationToken);

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
