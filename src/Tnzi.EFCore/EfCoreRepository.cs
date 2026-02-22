
namespace Tnzi.EFCore;

public class EFCoreRepository<TDbContext, TEntity> : IRepository<TEntity>
    where TDbContext : DbContext
    where TEntity : class, IEntity
{
    protected TDbContext DbContext { get; }
    protected DbSet<TEntity> DbSet { get; }
    private readonly bool _autoSaveChanges;
    private readonly EFCoreOptions? _options;
    private readonly IServiceProvider? _serviceProvider;
    private readonly ILogger<EFCoreRepository<TDbContext, TEntity>>? _logger;
    private readonly IPerformanceMonitorService? _performanceMonitor;
    private readonly ICache? _cache;
    private readonly IUnitOfWorkManager? _unitOfWorkManager;
    private readonly IUnitOfWork? _unitOfWork;

    /// <summary>
    /// 实体属性名白名单缓存（用于 ApplyOrderBy 安全验证）
    /// </summary>
    private static readonly ConcurrentDictionary<Type, HashSet<string>> _propertyNameCache = new();

    public EFCoreRepository(
        TDbContext dbContext, 
        IOptions<EFCoreOptions>? options = null,
        IServiceProvider? serviceProvider = null,
        ILogger<EFCoreRepository<TDbContext, TEntity>>? logger = null)
    {
        DbContext = Check.NotNull(dbContext);
        DbSet = dbContext.Set<TEntity>();
        _serviceProvider = serviceProvider;
        _logger = logger;
        _performanceMonitor = serviceProvider?.GetService<IPerformanceMonitorService>();
        _cache = serviceProvider?.GetService<ICache>();
        _unitOfWorkManager = serviceProvider?.GetService<IUnitOfWorkManager>();
        _unitOfWork = serviceProvider?.GetService<IUnitOfWork>();
        // 如果配置不存在，默认使用 true（向后兼容，与旧框架行为一致）
        _autoSaveChanges = options?.Value?.AutoSaveChanges ?? true;
        _options = options?.Value;
    }
    
    /// <summary>
    /// 检测是否应该立即保存更改
    /// 智能保存机制：如果已启用事务或在批量操作模式，则延迟保存
    /// </summary>
    protected virtual bool ShouldSaveImmediately()
    {
        if (!_autoSaveChanges)
        {
            return false; // 配置为不自动保存
        }
        
        // 检测是否已启用事务（通过 UnitOfWork 或 UnitOfWorkManager）
        bool isTransactionEnabled = IsTransactionEnabled();
        
        if (isTransactionEnabled)
        {
            return false; // 已启用事务，延迟保存（等待事务提交）
        }
        
        // 单个操作且无事务，立即保存
        return true;
    }
    
    /// <summary>
    /// 检测当前是否已启用事务
    /// 检测顺序：UnitOfWorkManager -> UnitOfWork -> DbContext.Database.CurrentTransaction
    /// </summary>
    protected virtual bool IsTransactionEnabled()
    {
        // 如果禁用事务检测，直接返回 false
        if (_options?.EnableTransactionDetection == false)
        {
            return false;
        }

        if (_serviceProvider == null)
        {
            return false;
        }

        try
        {
            // 第一优先级：检测 UnitOfWorkManager（支持多 DbContext）
            if (_unitOfWorkManager != null && _unitOfWorkManager.IsEnabledTransaction)
            {
                return true;
            }
            
            // 第二优先级：检测单个 UnitOfWork
            if (_unitOfWork != null && _unitOfWork.IsEnabledTransaction)
            {
                return true;
            }

            // 第三优先级：直接检测数据库事务（EF Core 层）
            // 这是降级检测，用于处理某些边缘情况
            var currentTransaction = DbContext.Database.CurrentTransaction;
            if (currentTransaction != null)
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error checking transaction status");
            // 忽略错误，返回 false
        }
        
        return false;
    }

    /// <summary>
    /// 获取实体类型的批次大小
    /// 优先使用 BatchSizes 字典中配置的值，否则使用全局 BatchSize
    /// </summary>
    /// <returns>批次大小</returns>
    protected virtual int GetBatchSize()
    {
        // 如果配置不存在，使用默认值 1000（向后兼容）
        if (_options == null)
        {
            return 1000;
        }

        var entityTypeName = typeof(TEntity).FullName;
        if (entityTypeName != null && _options.BatchSizes != null && _options.BatchSizes.TryGetValue(entityTypeName, out var batchSize))
        {
            return batchSize;
        }

        return _options.BatchSize;
    }

    public Type ElementType => GetQueryable().ElementType;
    public Expression Expression => GetQueryable().Expression;
    public IQueryProvider Provider => GetQueryable().Provider;

    /// <summary>
    /// 获取查询对象（带跟踪）
    /// </summary>
    protected virtual IQueryable<TEntity> GetQueryable()
    {
        return DbSet.AsQueryable();
    }

    /// <summary>
    /// 获取查询对象（不跟踪，用于只读查询以提升性能）
    /// </summary>
    protected virtual IQueryable<TEntity> GetQueryableAsNoTracking()
    {
        return DbSet.AsNoTracking().AsQueryable();
    }

    /// <summary>
    /// 获取 IQueryable 查询对象
    /// </summary>
    public virtual IQueryable<TEntity> AsQueryable(bool withTracking = false)
    {
        return withTracking ? GetQueryable() : GetQueryableAsNoTracking();
    }

    /// <summary>
    /// 对查询应用排序。如果 orderBy 为空，按主键默认排序以确保分页结果一致。
    /// 支持格式: "PropertyName [ASC|DESC], ..." 例如 "Name DESC, Id ASC"
    /// </summary>
    protected virtual IQueryable<TEntity> ApplyOrderBy(IQueryable<TEntity> source, string? orderBy)
    {
        if (string.IsNullOrWhiteSpace(orderBy))
        {
            // 默认按主键排序，确保分页结果一致（避免 PostgreSQL 等数据库无隐式排序问题）
            return source.OrderBy(e => EF.Property<object>(e, "Id"));
        }

        // 获取或构建属性名白名单
        var allowedProperties = _propertyNameCache.GetOrAdd(typeof(TEntity), type =>
            new HashSet<string>(
                type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name),
                StringComparer.OrdinalIgnoreCase));

        // 解析 orderBy 字符串并应用排序
        var parts = orderBy.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        IOrderedQueryable<TEntity>? orderedQuery = null;

        foreach (var part in parts)
        {
            var tokens = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var propertyName = tokens[0];
            var descending = tokens.Length > 1 && tokens[1].Equals("DESC", StringComparison.OrdinalIgnoreCase);

            // 白名单验证：跳过不存在的属性，防止注入
            if (!allowedProperties.Contains(propertyName))
            {
                _logger?.LogWarning("ApplyOrderBy: Property '{PropertyName}' is not a valid property of {EntityType}, skipping",
                    propertyName, typeof(TEntity).Name);
                continue;
            }

            var parameter = Expression.Parameter(typeof(TEntity), "e");
            var propertyInfo = typeof(TEntity).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)!;
            var property = Expression.Property(parameter, propertyInfo);
            var lambda = Expression.Lambda<Func<TEntity, object>>(Expression.Convert(property, typeof(object)), parameter);

            if (orderedQuery == null)
            {
                orderedQuery = descending ? source.OrderByDescending(lambda) : source.OrderBy(lambda);
            }
            else
            {
                orderedQuery = descending ? orderedQuery.ThenByDescending(lambda) : orderedQuery.ThenBy(lambda);
            }
        }

        // 如果所有属性都被跳过，回退到默认排序
        return orderedQuery ?? source.OrderBy(e => EF.Property<object>(e, "Id"));
    }

    public IEnumerator<TEntity> GetEnumerator()
    {
        return GetQueryable().GetEnumerator();
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public virtual async Task<TEntity?> FindAsync(params object[] keys)
    {
        Check.NotNullOrEmpty(keys);

        return await DbSet.FindAsync(keys);
    }

    public virtual async Task<TEntity?> FindAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        Check.NotNull(predicate);
        // 使用 AsNoTracking 提升性能，因为 Find 通常是只读查询
        return await GetQueryableAsNoTracking().FirstOrDefaultAsync(predicate, cancellationToken);
    }


    public virtual async Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default)
    {
        var query = GetQueryableAsNoTracking();
        if (predicate != null)
        {
            query = query.Where(predicate);
        }
        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    public virtual async Task<TEntity?> SingleOrDefaultAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default)
    {
        var query = GetQueryableAsNoTracking();
        if (predicate != null)
        {
            query = query.Where(predicate);
        }
        return await query.SingleOrDefaultAsync(cancellationToken);
    }

    public virtual async Task<bool> AnyAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default)
    {
        var query = GetQueryableAsNoTracking();
        if (predicate != null)
        {
            query = query.Where(predicate);
        }
        return await query.AnyAsync(cancellationToken);
    }

    public virtual async Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        Check.NotNull(predicate);
        return await AnyAsync(predicate, cancellationToken);
    }

    public virtual async Task<TEntity> GetRequiredAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        Check.NotNull(predicate);
        var entity = await FirstOrDefaultAsync(predicate, cancellationToken);
        if (entity == null)
        {
            throw new EntityNotFoundException<TEntity>();
        }
        return entity;
    }

    public virtual async Task<int> CountAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default)
    {
        var query = GetQueryableAsNoTracking();
        if (predicate != null)
        {
            query = query.Where(predicate);
        }
        return await query.CountAsync(cancellationToken);
    }

    public virtual async Task<long> LongCountAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default)
    {
        var query = GetQueryableAsNoTracking();
        if (predicate != null)
        {
            query = query.Where(predicate);
        }
        return await query.LongCountAsync(cancellationToken);
    }

    public virtual async Task<List<TEntity>> ToListAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default)
    {
        var query = GetQueryableAsNoTracking();
        if (predicate != null)
        {
            query = query.Where(predicate);
        }
        
        var stopwatch = Stopwatch.StartNew();
        var result = await query.ToListAsync(cancellationToken);
        stopwatch.Stop();
        
        _performanceMonitor?.RecordDbQuery($"EFCoreRepository<{typeof(TEntity).Name}>.ToListAsync", stopwatch.Elapsed);
        
        return result;
    }

    public virtual async Task<List<TEntity>> ToCachedListAsync(
        Expression<Func<TEntity, bool>> predicate,
        string cacheKey,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(predicate);
        Check.NotNullOrWhiteSpace(cacheKey);

        if (_cache != null)
        {
            var cached = await _cache.GetAsync<List<TEntity>>(cacheKey, cancellationToken);
            if (cached != null)
            {
                return cached;
            }
        }

        var list = await ToListAsync(predicate, cancellationToken);

        if (_cache != null)
        {
            await _cache.SetAsync(cacheKey, list, expiration, cancellationToken);
        }

        return list;
    }

    public virtual async Task<TEntity[]> ToArrayAsync(Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default)
    {
        var query = GetQueryableAsNoTracking();
        if (predicate != null)
        {
            query = query.Where(predicate);
        }
        return await query.ToArrayAsync(cancellationToken);
    }

    public virtual async Task<List<TResult>> SelectAsync<TResult>(Expression<Func<TEntity, TResult>> selector, Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default)
    {
        Check.NotNull(selector);
        var query = GetQueryableAsNoTracking();
        if (predicate != null)
        {
            query = query.Where(predicate);
        }
        return await query.Select(selector).ToListAsync(cancellationToken);
    }

    public virtual async Task<IPagedList<TEntity>> GetPagedListAsync(PagedQuery query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(query);
        // 分页查询通常是只读的，使用AsNoTracking提升性能
        var source = GetQueryableAsNoTracking();
        source = ApplyOrderBy(source, query.OrderBy);
        return await source.CreateAsync(query.PageIndex, query.PageSize, cancellationToken);
    }

    public virtual async Task<IPagedList<TEntity>> GetPagedListAsync(Expression<Func<TEntity, bool>> predicate, PagedQuery query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(predicate);
        Check.NotNull(query);
        // 分页查询通常是只读的，使用AsNoTracking提升性能
        var source = GetQueryableAsNoTracking().Where(predicate);
        source = ApplyOrderBy(source, query.OrderBy);
        return await source.CreateAsync(query.PageIndex, query.PageSize, cancellationToken);
    }

    public virtual async Task<IPagedList<TResult>> GetPagedListAsync<TResult>(Expression<Func<TEntity, TResult>> selector, PagedQuery query, Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default)
    {
        Check.NotNull(selector);
        Check.NotNull(query);
        var source = GetQueryableAsNoTracking();
        if (predicate != null)
        {
            source = source.Where(predicate);
        }
        source = ApplyOrderBy(source, query.OrderBy);
        return await source.Select(selector).CreateAsync(query.PageIndex, query.PageSize, cancellationToken);
    }

    public virtual async Task InsertAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        Check.NotNull(entity);
        
        await DbSet.AddAsync(entity, cancellationToken);
        
        // 智能保存：如果应该立即保存，则保存更改
        if (ShouldSaveImmediately())
        {
            await DbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public virtual async Task InsertManyAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        Check.NotNull(entities);

        // 优化批量插入：如果实体数量较少，直接使用AddRangeAsync
        // 如果数量较大，可以考虑分批处理以减少内存占用
        var entityList = entities as ICollection<TEntity> ?? entities.ToList();
        var batchSize = GetBatchSize();

        // 对于大量实体（超过批次大小），分批处理
        if (entityList.Count > batchSize)
        {
            var batches = entityList.Chunk(batchSize);

            foreach (var batch in batches)
            {
                await DbSet.AddRangeAsync(batch, cancellationToken);
            }
        }
        else
        {
            await DbSet.AddRangeAsync(entityList, cancellationToken);
        }
        
        // 批量操作：延迟保存（在方法结束时统一保存）
        if (ShouldSaveImmediately())
        {
            await DbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public virtual async Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        Check.NotNull(entity);

        if (DbContext.Entry(entity).State == EntityState.Detached)
        {
            DbSet.Attach(entity);
        }
        DbContext.Entry(entity).State = EntityState.Modified;
        
        // 智能保存：如果应该立即保存，则保存更改
        if (ShouldSaveImmediately())
        {
            await DbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public virtual async Task UpdateManyAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        Check.NotNull(entities);

        // 优化批量更新：对于大量实体，分批处理
        var entityList = entities as ICollection<TEntity> ?? entities.ToList();
        var batchSize = GetBatchSize();

        // 对于大量实体（超过批次大小），分批处理
        if (entityList.Count > batchSize)
        {
            var batches = entityList.Chunk(batchSize);

            foreach (var batch in batches)
            {
                DbSet.UpdateRange(batch);
            }
        }
        else
        {
            DbSet.UpdateRange(entityList);
        }
        
        // 批量操作：延迟保存（在方法结束时统一保存）
        if (ShouldSaveImmediately())
        {
            await DbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public virtual async Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        Check.NotNull(entity);

        if (entity is ISoftDelete softDelete)
        {
            softDelete.IsDeleted = true;
            await UpdateAsync(entity, cancellationToken);
            // UpdateAsync 已经会保存，这里不需要再次保存
        }
        else
        {
            DbSet.Remove(entity);
            
            // 智能保存：如果应该立即保存，则保存更改
            if (ShouldSaveImmediately())
            {
                await DbContext.SaveChangesAsync(cancellationToken);
            }
        }
    }

    public virtual async Task DeleteManyAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        Check.NotNull(entities);

        var entityList = entities as ICollection<TEntity> ?? entities.ToList();

        // 检查是否有软删除
        var hasSoftDelete = typeof(ISoftDelete).IsAssignableFrom(typeof(TEntity));

        if (hasSoftDelete)
        {
            // 软删除：批量更新IsDeleted标志
            var softDeleteEntities = entityList.Cast<ISoftDelete>().ToList();
            foreach (var entity in softDeleteEntities)
            {
                entity.IsDeleted = true;
            }

            // 批量更新
            var entitiesToUpdate = entityList.ToList();
            var batchSize = GetBatchSize();
            if (entitiesToUpdate.Count > batchSize)
            {
                var batches = entitiesToUpdate.Chunk(batchSize);
                foreach (var batch in batches)
                {
                    DbSet.UpdateRange(batch);
                }
            }
            else
            {
                DbSet.UpdateRange(entitiesToUpdate);
            }
        }
        else
        {
            // 硬删除：直接移除
            var batchSize = GetBatchSize();
            if (entityList.Count > batchSize)
            {
                var batches = entityList.Chunk(batchSize);
                foreach (var batch in batches)
                {
                    DbSet.RemoveRange(batch);
                }
            }
            else
            {
                DbSet.RemoveRange(entityList);
            }
        }
        
        // 批量操作：延迟保存（在方法结束时统一保存）
        if (ShouldSaveImmediately())
        {
            await DbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public virtual async Task DeleteAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        Check.NotNull(predicate);
        
        // 软删除优化：使用 ExecuteUpdateAsync 直接在数据库层批量更新
        // 注意：软删除不涉及物理文件删除，因此可以安全使用优化路径
        if (typeof(ISoftDelete).IsAssignableFrom(typeof(TEntity)))
        {
            var currentUser = _serviceProvider?.GetService<ICurrentUser>();
            var userId = currentUser?.Id;
            var now = DateTime.UtcNow;

            var hasDeleter = typeof(IHasDeleter).IsAssignableFrom(typeof(TEntity));
            var hasModTime = typeof(IHasModificationTime).IsAssignableFrom(typeof(TEntity));
            var hasModifier = typeof(IHasModifier).IsAssignableFrom(typeof(TEntity));

            if (hasDeleter && hasModTime && hasModifier)
            {
                await DbSet.Where(predicate).ExecuteUpdateAsync(s => s
                    .SetProperty(e => ((ISoftDelete)e).IsDeleted, true)
                    .SetProperty(e => ((IHasDeleter)e).DeleterId, userId)
                    .SetProperty(e => ((IHasDeleter)e).DeletionTime, now)
                    .SetProperty(e => ((IHasModificationTime)e).LastModificationTime, (DateTime?)now)
                    .SetProperty(e => ((IHasModifier)e).LastModifierId, userId)
                , cancellationToken);
            }
            else if (hasDeleter)
            {
                await DbSet.Where(predicate).ExecuteUpdateAsync(s => s
                    .SetProperty(e => ((ISoftDelete)e).IsDeleted, true)
                    .SetProperty(e => ((IHasDeleter)e).DeleterId, userId)
                    .SetProperty(e => ((IHasDeleter)e).DeletionTime, now)
                , cancellationToken);
            }
            else if (hasModTime && hasModifier)
            {
                await DbSet.Where(predicate).ExecuteUpdateAsync(s => s
                    .SetProperty(e => ((ISoftDelete)e).IsDeleted, true)
                    .SetProperty(e => ((IHasModificationTime)e).LastModificationTime, (DateTime?)now)
                    .SetProperty(e => ((IHasModifier)e).LastModifierId, userId)
                , cancellationToken);
            }
            else
            {
                await DbSet.Where(predicate).ExecuteUpdateAsync(s => s
                    .SetProperty(e => ((ISoftDelete)e).IsDeleted, true)
                , cancellationToken);
            }
        }
        else
        {
            // 硬删除安全性检查：如果实体包含 [FileField]，必须加载到内存删除以触发文件清理
            // 分批加载和删除，避免一次性加载所有匹配实体到内存导致 OOM
            if (Internal.TypeHelper.HasFileFields(typeof(TEntity)))
            {
                var batchSize = GetBatchSize();
                while (true)
                {
                    var batch = await DbSet.Where(predicate)
                        .Take(batchSize)
                        .ToListAsync(cancellationToken)
                        ;

                    if (batch.Count == 0)
                        break;

                    // 直接使用 DbSet.RemoveRange 而非调用 DeleteManyAsync，
                    // 避免子类 override 的 DeleteManyAsync 再次调用 DeleteAsync(predicate) 导致无限递归
                    DbSet.RemoveRange(batch);

                    if (ShouldSaveImmediately())
                    {
                        await DbContext.SaveChangesAsync(cancellationToken);
                    }
                }
            }
            else
            {
                // 无文件字段，安全使用 ExecuteDeleteAsync 优化
                await DbSet.Where(predicate).ExecuteDeleteAsync(cancellationToken);
            }
        }
    }
}

public class EFCoreRepository<TDbContext, TEntity, TKey> : EFCoreRepository<TDbContext, TEntity>, IRepository<TEntity, TKey>
    where TDbContext : DbContext
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    public EFCoreRepository(
        TDbContext dbContext, 
        IOptions<EFCoreOptions>? options = null,
        IServiceProvider? serviceProvider = null,
        ILogger<EFCoreRepository<TDbContext, TEntity>>? logger = null) 
        : base(dbContext, options, serviceProvider, logger)
    {
    }

    /// <summary>
    /// 验证 Id 是否有效（非 null 且非默认值）
    /// </summary>
    /// <param name="id">要验证的 Id</param>
    /// <returns>如果 Id 有效返回 true</returns>
    protected static bool IsValidId(TKey id)
    {
        if (id == null) return false;
        // 对于值类型，检查是否是默认值
        if (id is ValueType && id.Equals(default(TKey))) return false;
        return true;
    }

    /// <summary>
    /// 验证 Id，如果无效则抛出异常
    /// </summary>
    protected static void ThrowIfInvalidId(TKey id, string paramName = "id")
    {
        if (!IsValidId(id))
            throw new ArgumentException("Id cannot be null or default value.", paramName);
    }

    public virtual async Task<TEntity?> GetAsync(TKey id, CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidId(id);
        var entity = await FindAsync(new object[] { id! });
        return entity;
    }

    public virtual async Task<TEntity> GetRequiredAsync(TKey id, CancellationToken cancellationToken = default)
    {
        var entity = await GetAsync(id, cancellationToken);
        if (entity == null)
        {
            throw new EntityNotFoundException<TEntity>(id);
        }
        return entity;
    }

    public virtual async Task<List<TEntity>> GetListAsync(IEnumerable<TKey> ids, CancellationToken cancellationToken = default)
    {
        Check.NotNull(ids);
        var idList = ids.ToList();
        if (idList.Count == 0)
        {
            return new List<TEntity>();
        }
        return await GetQueryableAsNoTracking()
            .Where(e => idList.Contains(e.Id!))
            .ToListAsync(cancellationToken)
            ;
    }

    public virtual async Task UpdateAsync(TKey id, Action<TEntity> updateAction, CancellationToken cancellationToken = default)
    {
        Check.NotNull(updateAction);
        var entity = await GetRequiredAsync(id, cancellationToken);
        updateAction(entity);
        await UpdateAsync(entity, cancellationToken);
    }

    public override async Task DeleteManyAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        Check.NotNull(entities);
        
        var entityList = entities as ICollection<TEntity> ?? entities.ToList();
        if (entityList.Count == 0) return;

        // 对于大批量操作，利用基础 DeleteAsync (ExecuteUpdate/ExecuteDelete) 进行优化
        // 阈值设为 20，平衡反射/表达式构建开销与 SQL 性能
        // 注意：当实体有 [FileField] 且非软删除时，不走优化路径，
        // 因为 DeleteAsync(predicate) 会再次调用 DeleteManyAsync，导致无限递归
        var hasFileFields = !typeof(ISoftDelete).IsAssignableFrom(typeof(TEntity))
            && Internal.TypeHelper.HasFileFields(typeof(TEntity));
        if (entityList.Count > 20 && !hasFileFields)
        {
            var ids = entityList.Select(e => e.Id).ToList();
            await DeleteAsync(e => ids.Contains(e.Id), cancellationToken);

            // 同步内存状态（以防实体被追踪并在后续被使用）
            foreach (var entity in entityList)
            {
                if (entity is ISoftDelete sd) sd.IsDeleted = true;
            }
        }
        else
        {
            await base.DeleteManyAsync(entityList, cancellationToken);
        }
    }

    public virtual async Task DeleteAsync(TKey id, CancellationToken cancellationToken = default)
    {
        ThrowIfInvalidId(id);
        // 直接使用谓词删除，触发 ExecuteUpdate/ExecuteDelete 优化
        await DeleteAsync(e => e.Id!.Equals(id), cancellationToken);
    }

    public virtual async Task<TEntity?> GetWithDetailsAsync(TKey id, CancellationToken cancellationToken = default, params Expression<Func<TEntity, object>>[] includes)
    {
        ThrowIfInvalidId(id);
        var query = GetQueryable();
        foreach (var include in includes)
        {
            query = query.Include(include);
        }
        return await query.FirstOrDefaultAsync(e => e.Id!.Equals(id), cancellationToken);
    }

    public virtual async Task<bool> ExistsAsync(TKey id, CancellationToken cancellationToken = default)
    {
        if (!IsValidId(id))
            return false;

        return await GetQueryableAsNoTracking().AnyAsync(e => e.Id!.Equals(id), cancellationToken);
    }

    public virtual async Task<Dictionary<TKey, TEntity>> ToDictionaryAsync(CancellationToken cancellationToken = default)
    {
        var list = await GetQueryableAsNoTracking().ToListAsync(cancellationToken);
        return list.ToDictionary(e => e.Id!);
    }

    public virtual async Task<Dictionary<TKey, TEntity>> ToDictionaryAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        Check.NotNull(predicate);
        var list = await GetQueryableAsNoTracking().Where(predicate).ToListAsync(cancellationToken);
        return list.ToDictionary(e => e.Id!);
    }
}