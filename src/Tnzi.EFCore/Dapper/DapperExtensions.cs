
namespace Tnzi.EFCore.Dapper;

/// <summary>
/// DbContext Dapper 扩展方法
/// </summary>
public static class DapperExtensions
{
    /// <summary>
    /// 获取绑定到这个 DbContext 实例的 Dapper 服务（用于执行原始 SQL 和批量操作）
    /// </summary>
    /// <remarks>
    /// 恒绑定到传入的实例：容器里登记的 <see cref="IDapperService"/> 只对应首个 DbContext 类型，
    /// 多 DbContext 场景下拿它会把语句路由到另一个上下文。应用容器经
    /// <c>DbContextServiceResolver</c> 取（EF 内部容器看不到应用服务，此前 <c>IConfiguration</c>
    /// 与工作单元管理器在这条路径上一律取不到）。
    /// </remarks>
    /// <param name="dbContext">数据库上下文</param>
    /// <returns>Dapper 服务</returns>
    public static IDapperService GetDapper(this DbContext dbContext)
    {
        Check.NotNull(dbContext);

        var serviceProvider = DbContextServiceResolver.GetServiceProvider(dbContext);
        var configuration = serviceProvider?.GetService<IConfiguration>();
        var unitOfWorkManager = serviceProvider?.GetService<IUnitOfWorkManager>();
        var unitOfWork = serviceProvider?.GetService<IUnitOfWork>();
        var databaseProvider = Providers.DapperDatabaseProviderFactory.CreateFromDbContext(dbContext, configuration);

        return new DapperService(dbContext, databaseProvider, unitOfWorkManager, unitOfWork);
    }

    /// <summary>
    /// 执行原始 SQL 查询
    /// </summary>
    public static async Task<IEnumerable<T>> QueryAsync<T>(
        this DbContext dbContext,
        string sql,
        object? param = null,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.GetDapper().QueryAsync<T>(sql, param, cancellationToken);
    }

    /// <summary>
    /// 执行原始 SQL 查询并返回第一个结果或默认值
    /// </summary>
    public static async Task<T?> QueryFirstOrDefaultAsync<T>(
        this DbContext dbContext,
        string sql,
        object? param = null,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.GetDapper().QueryFirstOrDefaultAsync<T>(sql, param, cancellationToken);
    }

    /// <summary>
    /// 执行原始 SQL 查询并返回单个结果或默认值
    /// </summary>
    public static async Task<T?> QuerySingleOrDefaultAsync<T>(
        this DbContext dbContext,
        string sql,
        object? param = null,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.GetDapper().QuerySingleOrDefaultAsync<T>(sql, param, cancellationToken);
    }

    /// <summary>
    /// 执行原始 SQL 命令
    /// </summary>
    public static async Task<int> ExecuteAsync(
        this DbContext dbContext,
        string sql,
        object? param = null,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.GetDapper().ExecuteAsync(sql, param, cancellationToken);
    }

    /// <summary>
    /// 执行原始 SQL 查询并返回标量值
    /// </summary>
    public static async Task<T?> ExecuteScalarAsync<T>(
        this DbContext dbContext,
        string sql,
        object? param = null,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.GetDapper().ExecuteScalarAsync<T>(sql, param, cancellationToken);
    }

    /// <summary>
    /// 批量插入
    /// </summary>
    public static async Task<int> BulkInsertAsync<T>(
        this DbContext dbContext,
        IEnumerable<T> entities,
        string? tableName = null,
        CancellationToken cancellationToken = default) where T : class
    {
        return await dbContext.GetDapper().BulkInsertAsync<T>(entities, tableName, cancellationToken);
    }

    /// <summary>
    /// 批量更新
    /// </summary>
    public static async Task<int> BulkUpdateAsync<T>(
        this DbContext dbContext,
        IEnumerable<T> entities,
        string? tableName = null,
        string? keyColumn = null,
        CancellationToken cancellationToken = default) where T : class
    {
        return await dbContext.GetDapper().BulkUpdateAsync<T>(entities, tableName, keyColumn, cancellationToken);
    }

    /// <summary>
    /// 批量删除
    /// </summary>
    public static async Task<int> BulkDeleteAsync<T>(
        this DbContext dbContext,
        IEnumerable<object> keys,
        string? tableName = null,
        string? keyColumn = null,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.GetDapper().BulkDeleteAsync<T>(keys, tableName, keyColumn, cancellationToken);
    }

    /// <summary>
    /// 执行存储过程
    /// </summary>
    public static async Task<IEnumerable<T>> ExecuteStoredProcedureAsync<T>(
        this DbContext dbContext,
        string procedureName,
        object? param = null,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.GetDapper().ExecuteStoredProcedureAsync<T>(procedureName, param, cancellationToken);
    }
}

/// <summary>
/// Dapper 执行器注册扩展方法
/// </summary>
public static class DapperExecutorRegistrationExtensions
{
    /// <summary>
    /// 为特定实体类型注册 IDapperExecutor
    /// </summary>
    public static IServiceCollection RegisterDapperExecutor<TEntity, TKey>(this IServiceCollection services)
        where TEntity : class, Tnzi.Domain.Entities.IEntity<TKey>
    {
        services.AddScoped<IDapperExecutor<TEntity, TKey>>(sp =>
        {
            var entityManager = sp.GetRequiredService<IEntityManager>();
            var configuration = sp.GetService<IConfiguration>();
            return new DapperExecutor<TEntity, TKey>(sp, entityManager, configuration);
        });

        return services;
    }
}