
namespace Tnzi.EFCore.Dapper;

/// <summary>
/// DbContext Dapper 扩展方法
/// </summary>
public static class DapperExtensions
{
    /// <summary>
    /// 获取 Dapper 服务（用于执行原始 SQL 和批量操作）
    /// 优先从 DI 容器获取，如果不存在则创建新实例
    /// </summary>
    /// <param name="dbContext">数据库上下文</param>
    /// <returns>Dapper 服务</returns>
    public static IDapperService GetDapper(this DbContext dbContext)
    {
        var serviceProvider = dbContext.Database.GetService<IServiceProvider>();
        if (serviceProvider == null && dbContext is IInfrastructure<IServiceProvider> infra)
        {
            serviceProvider = infra.Instance;
        }
        if (serviceProvider == null)
        {
            throw new InvalidOperationException("Cannot get IServiceProvider from DbContext");
        }
        
        // 优先从 DI 容器获取 IDapperService（如果已注册）
        // 注意：IDapperService 是 scoped 的，每个 DbContext 类型对应一个实例
        var dapperService = serviceProvider.GetService<IDapperService>();
        if (dapperService != null)
        {
            return dapperService;
        }
        
        // 如果 DI 容器中没有，则创建新实例（向后兼容）
        var configuration = serviceProvider.GetService<IConfiguration>();
        var databaseProvider = Providers.DapperDatabaseProviderFactory.CreateFromDbContext(dbContext, configuration);
        var logger = serviceProvider.GetRequiredService<ILogger<DapperService>>();
        
        return new DapperService(dbContext, databaseProvider, logger);
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