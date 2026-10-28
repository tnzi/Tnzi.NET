
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
    /// 可排序属性缓存（ApplyOrderBy 的白名单）：EF 模型里已映射的标量属性。
    /// </summary>
    /// <remarks>
    /// 按 EF 实体类型而不是 CLR 类型缓存：同一 CLR 类型在不同模型里映射的列可以不同（多租户关闭时 <c>TenantId</c> 被忽略）。
    /// </remarks>
    private static readonly ConcurrentDictionary<IEntityType, FrozenDictionary<string, PropertyInfo>> _sortablePropertyCache = new();

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
    /// <remarks>
    /// 只用于带 <c>[FileField]</c> 的实体按条件硬删时分批加载（见 <see cref="EFCoreOptions.BatchSize"/>）。
    /// </remarks>
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

        // 白名单取自 EF 模型：只有已映射的标量属性能翻译成 ORDER BY。
        // ★ 不能取 CLR 公开属性：DomainEvents、导航、[NotMapped] 计算属性都在里面，命中后 EF 执行时翻译失败，
        //   一个客户端传来的 orderBy 就变成 500，而不是按本方法的意图被跳过。
        var sortableProperties = GetSortableProperties();

        // 解析 orderBy 字符串并应用排序
        var parts = orderBy.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        IOrderedQueryable<TEntity>? orderedQuery = null;

        foreach (var part in parts)
        {
            var tokens = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var propertyName = tokens[0];
            var descending = tokens.Length > 1 && tokens[1].Equals("DESC", StringComparison.OrdinalIgnoreCase);

            // 白名单验证：跳过不存在或未映射的属性，防止注入
            if (!sortableProperties.TryGetValue(propertyName, out var propertyInfo))
            {
                _logger?.LogWarning("ApplyOrderBy: Property '{PropertyName}' is not a valid property of {EntityType}, skipping",
                    propertyName, typeof(TEntity).Name);
                continue;
            }

            var parameter = Expression.Parameter(typeof(TEntity), "e");
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

    private FrozenDictionary<string, PropertyInfo> GetSortableProperties()
    {
        var entityType = DbContext.Model.FindEntityType(typeof(TEntity));
        if (entityType == null)
        {
            return FrozenDictionary<string, PropertyInfo>.Empty;
        }

        return _sortablePropertyCache.GetOrAdd(entityType, static type =>
        {
            var properties = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in type.GetProperties())
            {
                // 影子属性与只映射到字段的属性没有可供表达式访问的 CLR 属性
                if (property.PropertyInfo is { } clrProperty && !property.IsShadowProperty())
                {
                    properties.TryAdd(property.Name, clrProperty);
                }
            }

            return properties.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        });
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
            query = query.Where(predicate);
        return await query.AnyAsync(cancellationToken);
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

        // 自动添加租户前缀，确保多租户缓存隔离
        var tenantId = _serviceProvider?.GetService<ICurrentTenant>()?.Id;
        var tenantAwareKey = CacheKeys.WithTenant(cacheKey, tenantId);

        if (_cache != null)
        {
            var cached = await _cache.GetAsync<List<TEntity>>(tenantAwareKey, cancellationToken);
            if (cached != null)
            {
                return cached;
            }
        }

        var list = await ToListAsync(predicate, cancellationToken);

        if (_cache != null)
        {
            await _cache.SetAsync(tenantAwareKey, list, expiration, cancellationToken);
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

        // Auto-apply dynamic filter from PagedQuery.Filter
        if (query.Filter is { HasFilters: true })
        {
            var filterExpression = FilterExpressionBuilder.Build<TEntity>(query.Filter);
            source = source.Where(filterExpression);
        }

        source = ApplyOrderBy(source, query.OrderBy);
        return await source.CreateAsync(query.PageIndex, query.PageSize, cancellationToken);
    }

    public virtual async Task<IPagedList<TEntity>> GetPagedListAsync(Expression<Func<TEntity, bool>> predicate, PagedQuery query, CancellationToken cancellationToken = default)
    {
        Check.NotNull(predicate);
        Check.NotNull(query);
        // 分页查询通常是只读的，使用AsNoTracking提升性能
        var source = GetQueryableAsNoTracking().Where(predicate);

        // Auto-apply dynamic filter from PagedQuery.Filter
        if (query.Filter is { HasFilters: true })
        {
            var filterExpression = FilterExpressionBuilder.Build<TEntity>(query.Filter);
            source = source.Where(filterExpression);
        }

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

        // Auto-apply dynamic filter from PagedQuery.Filter（与另外两个分页重载保持一致，
        // 此前投影重载静默忽略 query.Filter）
        if (query.Filter is { HasFilters: true })
        {
            var filterExpression = FilterExpressionBuilder.Build<TEntity>(query.Filter);
            source = source.Where(filterExpression);
        }

        source = ApplyOrderBy(source, query.OrderBy);
        return await source.Select(selector).CreateAsync(query.PageIndex, query.PageSize, cancellationToken);
    }

    // Dynamic filter methods (FilterGroup integration)

    /// <summary>
    /// Get a filtered paged list using dynamic FilterGroup.
    /// </summary>
    public virtual async Task<IPagedList<TEntity>> GetFilteredPagedListAsync(
        FilterGroup filter, PagedQuery query,
        Expression<Func<TEntity, bool>>? additionalPredicate = null,
        CancellationToken cancellationToken = default)
    {
        Check.NotNull(filter);
        Check.NotNull(query);

        var source = GetQueryableAsNoTracking();

        if (filter.HasFilters)
        {
            var filterExpression = FilterExpressionBuilder.Build<TEntity>(filter);
            source = source.Where(filterExpression);
        }

        if (additionalPredicate != null)
            source = source.Where(additionalPredicate);

        source = ApplyOrderBy(source, query.OrderBy);
        return await source.CreateAsync(query.PageIndex, query.PageSize, cancellationToken);
    }

    /// <summary>
    /// Get a filtered list using dynamic FilterGroup (non-paged).
    /// </summary>
    public virtual async Task<List<TEntity>> GetFilteredListAsync(
        FilterGroup filter, CancellationToken cancellationToken = default)
    {
        Check.NotNull(filter);

        var source = GetQueryableAsNoTracking();

        if (filter.HasFilters)
        {
            var filterExpression = FilterExpressionBuilder.Build<TEntity>(filter);
            source = source.Where(filterExpression);
        }

        return await source.ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Count entities matching a dynamic FilterGroup.
    /// </summary>
    public virtual async Task<int> CountAsync(
        FilterGroup filter, CancellationToken cancellationToken = default)
    {
        Check.NotNull(filter);

        var source = GetQueryableAsNoTracking();

        if (filter.HasFilters)
        {
            var filterExpression = FilterExpressionBuilder.Build<TEntity>(filter);
            source = source.Where(filterExpression);
        }

        return await source.CountAsync(cancellationToken);
    }

    // 聚合方法实现

    public virtual async Task<decimal> SumAsync(Expression<Func<TEntity, decimal>> selector, Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default)
    {
        Check.NotNull(selector);
        var query = GetQueryableAsNoTracking();
        if (predicate != null)
            query = query.Where(predicate);
        return await query.SumAsync(selector, cancellationToken);
    }

    public virtual async Task<TResult> MinAsync<TResult>(Expression<Func<TEntity, TResult>> selector, Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default)
    {
        Check.NotNull(selector);
        var query = GetQueryableAsNoTracking();
        if (predicate != null)
            query = query.Where(predicate);
        return await query.MinAsync(selector, cancellationToken);
    }

    public virtual async Task<TResult> MaxAsync<TResult>(Expression<Func<TEntity, TResult>> selector, Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default)
    {
        Check.NotNull(selector);
        var query = GetQueryableAsNoTracking();
        if (predicate != null)
            query = query.Where(predicate);
        return await query.MaxAsync(selector, cancellationToken);
    }

    public virtual async Task<decimal> AverageAsync(Expression<Func<TEntity, decimal>> selector, Expression<Func<TEntity, bool>>? predicate = null, CancellationToken cancellationToken = default)
    {
        Check.NotNull(selector);
        var query = GetQueryableAsNoTracking();
        if (predicate != null)
            query = query.Where(predicate);
        return await query.AverageAsync(selector, cancellationToken);
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

    /// <summary>
    /// Explicit flush: forces pending changes through SaveChangesAsync regardless of
    /// the transaction-deferred policy. Use sparingly when subsequent operations within
    /// the same UnitOfWork need to observe writes (e.g. sequence-number computation).
    /// </summary>
    /// <remarks>
    /// 事务已启用（UnitOfWork/UnitOfWorkManager）时必须经由 UnitOfWork 保存：
    /// 物理数据库事务是延迟到 UnitOfWork 第一次 SaveChanges 才开启的，
    /// 若直接调用 DbContext.SaveChangesAsync，首次 flush 会在自动提交模式下落库，
    /// 脱离事务保护（后续回滚无法撤销），并使同事务内的裸 SQL 操作（如 ExecuteUpdate）
    /// 无法加入事务。
    /// </remarks>
    public virtual async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (_unitOfWorkManager != null && _unitOfWorkManager.IsEnabledTransaction)
        {
            var unitOfWork = _unitOfWorkManager.GetUnitOfWork<TDbContext>();
            if (unitOfWork != null)
            {
                return await unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }

        if (_unitOfWork != null && _unitOfWork.IsEnabledTransaction)
        {
            return await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return await DbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// 确保当前工作单元的物理数据库事务已开启（幂等）。
    /// 事务内执行 ExecuteUpdate/ExecuteDelete/Raw SQL 前必须调用，使裸 SQL 加入事务。
    /// </summary>
    public virtual async Task EnsureTransactionStartedAsync(CancellationToken cancellationToken = default)
    {
        if (_unitOfWorkManager != null && _unitOfWorkManager.IsEnabledTransaction)
        {
            var unitOfWork = _unitOfWorkManager.GetUnitOfWork<TDbContext>();
            if (unitOfWork != null)
            {
                await unitOfWork.EnsureTransactionStartedAsync(cancellationToken);
                return;
            }
        }

        if (_unitOfWork != null && _unitOfWork.IsEnabledTransaction)
        {
            await _unitOfWork.EnsureTransactionStartedAsync(cancellationToken);
        }
    }

    public virtual async Task InsertManyAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        Check.NotNull(entities);

        // 一次 AddRange、一次保存。按块逐次 AddRange 与一次 AddRange 得到的跟踪状态完全相同，
        // 发往数据库的命令分批由 provider 的 MaxBatchSize 决定，仓储里分块不省内存也不减往返。
        await DbSet.AddRangeAsync(entities, cancellationToken);

        // 批量操作：延迟保存（在方法结束时统一保存）
        if (ShouldSaveImmediately())
        {
            await DbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public virtual async Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        Check.NotNull(entity);

        var entry = DbContext.Entry(entity);
        var reattached = false;
        if (entry.State == EntityState.Detached)
        {
            // 同一主键已被本 DbContext 跟踪时 Attach 会抛 identity conflict，
            // 合并到已跟踪的那一条（缘由见 TrackedDuplicateResolver）。
            var tracked = TrackedDuplicateResolver.Find(DbContext, entity);
            if (TrackedDuplicateResolver.CanMergeInto(tracked))
            {
                TrackedDuplicateResolver.Merge(tracked!, entity);

                if (ShouldSaveImmediately())
                {
                    await DbContext.SaveChangesAsync(cancellationToken);
                }
                return;
            }

            DbSet.Attach(entity);
            reattached = true;
        }

        // 尚未持久化的新实体（Added）必须保持 Added：
        // 强行改为 Modified 会让 INSERT 变成 UPDATE（影响 0 行），在事务延迟保存模式下
        // 该行从未插入，依赖它的子表在事务提交 flush 时触发外键违反
        if (entry.State != EntityState.Added)
        {
            entry.State = EntityState.Modified;
        }

        // 脱离跟踪附加进来的实例原值即新值，文件字段的原值要从库里回读，引用增删才算得出来
        if (reattached)
        {
            await FileFieldOriginalValues.RestoreAsync(entry, cancellationToken);
        }

        // 智能保存：如果应该立即保存，则保存更改
        if (ShouldSaveImmediately())
        {
            await DbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public virtual async Task UpdateManyAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        Check.NotNull(entities);

        var entityList = entities as ICollection<TEntity> ?? entities.ToList();

        // 尚未持久化的新实体（Added）必须保持 Added，不能被 UpdateRange 降级为 Modified（见 UpdateAsync）
        var toUpdate = new List<TEntity>(entityList.Count);
        var reattached = new List<TEntity>();
        foreach (var item in entityList)
        {
            var state = DbContext.Entry(item).State;
            if (state == EntityState.Added)
            {
                continue;
            }

            // 与 UpdateAsync 同理：同主键已被跟踪时 UpdateRange 会抛 identity conflict，
            // 合并到已跟踪的那一条，不再进批。
            if (state == EntityState.Detached)
            {
                var tracked = TrackedDuplicateResolver.Find(DbContext, item);
                if (TrackedDuplicateResolver.CanMergeInto(tracked))
                {
                    TrackedDuplicateResolver.Merge(tracked!, item);
                    continue;
                }

                reattached.Add(item);
            }

            toUpdate.Add(item);
        }

        if (toUpdate.Count > 0)
        {
            DbSet.UpdateRange(toUpdate);
        }

        // 与 UpdateAsync 同理：脱离跟踪附加进来的实例，文件字段原值从库里回读
        foreach (var item in reattached)
        {
            await FileFieldOriginalValues.RestoreAsync(DbContext.Entry(item), cancellationToken);
        }

        // 批量操作：延迟保存（在方法结束时统一保存）
        if (ShouldSaveImmediately())
        {
            await DbContext.SaveChangesAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// 只改变更跟踪状态，<b>不产生任何 SQL</b>：把实体置为 <see cref="EntityState.Detached"/>，
    /// 于是它不再参与本 <c>DbContext</c> 后续的任何 <c>SaveChanges</c>。
    /// <para>
    /// ★ 刻意<b>不</b>复用 <see cref="DeleteAsync(TEntity, CancellationToken)"/>：那是业务删除 ——
    /// 对 <c>Added</c> 实体它同样不插入，但无事务时会立即 <c>SaveChanges</c>，把本作用域里别的挂起变更一并提交；
    /// 错误恢复路径（catch 里撤销一条插入失败的实体）不该有这种副作用。
    /// </para>
    /// </remarks>
    public virtual void Discard(TEntity entity)
    {
        Check.NotNull(entity);

        var entry = DbContext.Entry(entity);
        if (entry.State != EntityState.Detached)
            entry.State = EntityState.Detached;
    }

    /// <inheritdoc />
    /// <remarks>
    /// 软删实体与 <c>DbContext.Remove</c> 写出同样的行：<c>IsDeleted</c>、删除人、删除时间、修改审计与新并发戳，
    /// 由 <c>SaveChanges</c> 的审计管线按「未删 → 已删」的跃迁写入（见 <see cref="MarkSoftDeletedAsync"/>）。
    /// 尚未落库的新实体（<c>Added</c>）不论软删硬删一律不插入。
    /// </remarks>
    public virtual async Task DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        Check.NotNull(entity);

        if (entity is ISoftDelete)
        {
            await MarkSoftDeletedAsync(entity, cancellationToken);
        }
        else
        {
            DbSet.Remove(ResolveTrackedInstance(entity));
        }

        // 智能保存：如果应该立即保存，则保存更改
        if (ShouldSaveImmediately())
        {
            await DbContext.SaveChangesAsync(cancellationToken);
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
            // 软删除：逐个走与 DeleteAsync 同一处理（跟踪状态、重复跟踪合并、Added 不插入）
            foreach (var entity in entityList)
            {
                await MarkSoftDeletedAsync(entity, cancellationToken);
            }
        }
        else
        {
            // 硬删除：直接移除（同主键已被跟踪时移除已跟踪的那一条）
            DbSet.RemoveRange(entityList.Select(ResolveTrackedInstance).ToList());
        }

        // 批量操作：延迟保存（在方法结束时统一保存）
        if (ShouldSaveImmediately())
        {
            await DbContext.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// 脱离跟踪的实例若与本 DbContext 已跟踪的条目同主键，返回已跟踪的那个实例（对它操作才不会撞 identity conflict）；
    /// 否则原样返回。
    /// </summary>
    private TEntity ResolveTrackedInstance(TEntity entity)
    {
        if (DbContext.Entry(entity).State != EntityState.Detached)
        {
            return entity;
        }

        return TrackedDuplicateResolver.Find(DbContext, entity)?.Entity ?? entity;
    }

    /// <summary>
    /// 把一个软删实体标记为已删，让 <c>SaveChanges</c> 的审计管线写出删除人与删除时间。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★ 顺序是关键：<b>先让条目被正确跟踪，再翻 <c>IsDeleted</c></b>。审计管线的修改分支按原值 false → 现值 true
    /// 的跃迁判定「刚刚被软删」才写删除人；仓储读方法默认 <c>AsNoTracking</c>，若先翻标志再附加，
    /// 原值快照就已经是 true，删除人与删除时间一列不写。
    /// </para>
    /// <para>
    /// 刻意不走 <c>DbSet.Remove</c> + <c>Deleted</c> 分支：<c>Deleted</c> 状态会触发 EF 对已跟踪依赖项的级联
    /// （硬删子行、把可选外键置空），而软删的主行仍在库里，级联是错的。
    /// </para>
    /// <para>
    /// 同主键已被跟踪时合并到已跟踪的那一条（与 <c>UpdateAsync</c> 同一处理）；尚未落库的新实体（<c>Added</c>）
    /// 直接移出跟踪而不插入：它从未存在过，插一行带删除标记的记录只是凭空造出垃圾数据。
    /// </para>
    /// </remarks>
    private async Task MarkSoftDeletedAsync(TEntity entity, CancellationToken cancellationToken)
    {
        var target = entity;
        var entry = DbContext.Entry(entity);
        var reattached = false;

        if (entry.State == EntityState.Detached)
        {
            var tracked = TrackedDuplicateResolver.Find(DbContext, entity);
            if (TrackedDuplicateResolver.CanMergeInto(tracked))
            {
                TrackedDuplicateResolver.Merge(tracked!, entity);
                target = tracked!.Entity;
                entry = tracked;
            }
            else
            {
                // 按实例当前（未删）的值附加：原值快照就是「删除之前」
                DbSet.Attach(entity);
                reattached = true;
            }
        }

        ((ISoftDelete)entity).IsDeleted = true;

        if (entry.State == EntityState.Added)
        {
            DbSet.Remove(target);
            return;
        }

        ((ISoftDelete)target).IsDeleted = true;
        entry.State = EntityState.Modified;

        if (reattached)
        {
            await FileFieldOriginalValues.RestoreAsync(entry, cancellationToken);
        }
    }

    /// <summary>
    /// 执行 ExecuteUpdate/ExecuteDelete 等裸 SQL 前，若事务已启用则幂等地确保物理事务已开启。
    /// 框架事务延迟物理开启（首次 UoW SaveChanges 才 BEGIN）；当删除是请求内首个写操作时，
    /// 裸 SQL 会在自动提交模式下落库，脱离 UoW 事务、无法被回滚撤销。此处提前 BEGIN 使其加入事务。
    /// </summary>
    private async Task EnsureBulkSqlJoinsTransactionAsync(CancellationToken cancellationToken)
    {
        if (IsTransactionEnabled())
        {
            await EnsureTransactionStartedAsync(cancellationToken);
        }
    }

    /// <summary>
    /// 执行软删除：通过 ExecuteUpdateAsync 在数据库层批量设置审计字段。
    /// SetProperty 链在 <see cref="SoftDeleteSetters"/> 里按接口组合拼装，与 <c>BatchSoftDeleteAsync</c> 共用。
    /// </summary>
    protected virtual async Task ExecuteSoftDeleteAsync(IQueryable<TEntity> query, CancellationToken cancellationToken)
    {
        // 事务已启用时先确保物理事务开启，使下面的 ExecuteUpdate 裸 SQL 加入 UoW 事务（可回滚）
        await EnsureBulkSqlJoinsTransactionAsync(cancellationToken);

        var currentUser = _serviceProvider?.GetService<ICurrentUser>();
        var userId = currentUser?.Id;
        var tp = _serviceProvider?.GetService<TimeProvider>() ?? TimeProvider.System;
        var now = tp.GetUtcNow().UtcDateTime;

        await query.ExecuteUpdateAsync(s => SoftDeleteSetters.Apply(s, userId, now), cancellationToken);
    }

    public virtual async Task DeleteAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        Check.NotNull(predicate);

        // 软删除优化：使用 ExecuteUpdateAsync 直接在数据库层批量更新
        // 注意：软删除不涉及物理文件删除，因此可以安全使用优化路径
        if (typeof(ISoftDelete).IsAssignableFrom(typeof(TEntity)))
        {
            await ExecuteSoftDeleteAsync(DbSet.Where(predicate), cancellationToken);
        }
        else
        {
            // 硬删除安全性检查：如果实体包含 [FileField]，必须加载到内存删除以触发文件清理
            if (Internal.TypeHelper.HasFileFields(typeof(TEntity)))
            {
                await RemoveThroughChangeTrackerAsync(predicate, cancellationToken);
            }
            else
            {
                // 事务已启用时先确保物理事务开启，使 ExecuteDelete 裸 SQL 加入 UoW 事务（可回滚）
                await EnsureBulkSqlJoinsTransactionAsync(cancellationToken);
                // 无文件字段，安全使用 ExecuteDeleteAsync 优化
                await DbSet.Where(predicate).ExecuteDeleteAsync(cancellationToken);
            }
        }
    }

    /// <summary>
    /// 经变更跟踪硬删匹配的实体：带 <c>[FileField]</c> 的实体要这样删，文件引用才清理得到。
    /// </summary>
    /// <remarks>
    /// <para>
    /// ★★★ 分批的前提是<b>每批删完真的离开了数据库</b>，下一轮同一条查询才取得到下一批。此前只在「立即保存」时保存：
    /// 事务开着或关掉自动保存时行一直在库里，跟踪查询按主键身份解析回到同一批已标记 Deleted 的实例，循环永不结束。
    /// 现在分三种情形：
    /// </para>
    /// <list type="bullet">
    /// <item>立即保存：每批直接保存。</item>
    /// <item>事务开着（自动保存未关）：每批经仓储的 <see cref="SaveChangesAsync"/> 保存。它路由到工作单元、在调用方的事务里落库，
    /// 回滚照样撤销；与同一方法里 ExecuteDelete / ExecuteUpdate 的分支一样在事务内即时写库，内存按批有界。</item>
    /// <item>其余（关掉自动保存，或子类让立即保存不成立而又没有事务）：仓储不替调用方保存，一次取出全部匹配实体标记删除。
    /// 分批在这里省不了内存：标记删除的实体在调用方保存之前一直留在跟踪器里。</item>
    /// </list>
    /// <para>
    /// 用 <c>DbSet.RemoveRange</c> 而非 <c>DeleteManyAsync</c>：避免子类覆写的 <c>DeleteManyAsync</c> 再调回
    /// <c>DeleteAsync(predicate)</c> 无限递归。
    /// </para>
    /// </remarks>
    private async Task RemoveThroughChangeTrackerAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken)
    {
        var saveDirectly = ShouldSaveImmediately();
        var flushThroughUnitOfWork = !saveDirectly && _autoSaveChanges && IsTransactionEnabled();

        if (!saveDirectly && !flushThroughUnitOfWork)
        {
            DbSet.RemoveRange(await DbSet.Where(predicate).ToListAsync(cancellationToken));
            return;
        }

        var batchSize = GetBatchSize();
        while (true)
        {
            var batch = await DbSet.Where(predicate).Take(batchSize).ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                return;
            }

            // 兜底：本批全是已标记删除的实例 = 上一批保存之后这些行仍在库里（例如某个保存拦截器改写了删除），
            // 再转一圈也不会有进展。
            if (batch.TrueForAll(e => DbContext.Entry(e).State == EntityState.Deleted))
            {
                throw new InvalidOperationException(
                    $"Deleting {typeof(TEntity).Name} by predicate made no progress: rows removed in the previous batch are still returned by the query.");
            }

            DbSet.RemoveRange(batch);

            if (saveDirectly)
            {
                await DbContext.SaveChangesAsync(cancellationToken);
            }
            else
            {
                await SaveChangesAsync(cancellationToken);
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

    /// <inheritdoc />
    /// <remarks>
    /// 与 <c>SaveChanges</c> 同一套规则（<see cref="IdGenerationHelper.GenerateId"/>）：
    /// 注册了 <see cref="IEntityIdGenerator"/> 交给它，否则 Guid 按 provider 选 Sequential GUID 的排列、long 走 Snowflake。
    /// </remarks>
    public TKey NewId()
    {
        var id = IdGenerationHelper.GenerateId(DbContext, typeof(TEntity), typeof(TKey));
        return id is TKey key
            ? key
            : throw new NotSupportedException($"No id generator is available for key type {typeof(TKey).Name} of {typeof(TEntity).Name}.");
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
            // 数据库端批量 SQL 不经变更跟踪器，只交给它「脱离跟踪、且本上下文里没有同主键实例」的那些。
            // 已被跟踪的（含新增未落库的）走变更跟踪器那条路径，否则内存与库各说各话：
            // 新增的实体带着删除标记被插进库，已跟踪的实例带着旧并发戳在下一次保存撞冲突，
            // 调用方挂在它们身上的修改被丢掉。
            var viaTracker = new List<TEntity>();
            var viaSql = new List<TEntity>();
            foreach (var entity in entityList)
            {
                var tracked = DbContext.Entry(entity).State != EntityState.Detached
                    || TrackedDuplicateResolver.Find(DbContext, entity) != null;
                (tracked ? viaTracker : viaSql).Add(entity);
            }

            if (viaTracker.Count > 0)
            {
                await base.DeleteManyAsync(viaTracker, cancellationToken);
            }

            if (viaSql.Count > 0)
            {
                var ids = viaSql.Select(e => e.Id).ToList();
                await DeleteAsync(e => ids.Contains(e.Id), cancellationToken);

                // 同步调用方手里这些（未被跟踪的）实例的内存状态
                foreach (var entity in viaSql)
                {
                    if (entity is ISoftDelete sd) sd.IsDeleted = true;
                }
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