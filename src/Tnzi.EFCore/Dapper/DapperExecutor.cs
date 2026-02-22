
namespace Tnzi.EFCore.Dapper;

/// <summary>
/// Dapper 执行器实现（自动定位 DbContext）
/// 始终根据实体类型创建对应 DbContext 的 DapperService，确保多 DbContext 路由正确
/// </summary>
public class DapperExecutor<TEntity, TKey> : IDapperExecutor<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
{
    private readonly IDapperService _dapperService;

    public DapperExecutor(
        IServiceProvider serviceProvider,
        IEntityManager entityManager,
        IConfiguration? configuration = null)
    {
        Check.NotNull(serviceProvider);
        Check.NotNull(entityManager);

        // 通过实体类型定位 DbContext
        var dbContextType = entityManager.GetDbContextTypeForEntity(typeof(TEntity));
        var dbContext = (DbContext)serviceProvider.GetRequiredService(dbContextType);

        // 始终根据实体的 DbContext 创建 DapperService，确保多 DbContext 场景路由正确
        var databaseProvider = DapperDatabaseProviderFactory.CreateFromDbContext(dbContext, configuration);
        var logger = serviceProvider.GetRequiredService<ILogger<DapperService>>();

        _dapperService = new DapperService(dbContext, databaseProvider, logger);
    }

    public Task<IEnumerable<TResult>> QueryAsync<TResult>(
        string sql,
        object? param = null,
        CancellationToken cancellationToken = default)
    {
        return _dapperService.QueryAsync<TResult>(sql, param, cancellationToken);
    }

    public Task<TResult?> QueryFirstOrDefaultAsync<TResult>(
        string sql,
        object? param = null,
        CancellationToken cancellationToken = default)
    {
        return _dapperService.QueryFirstOrDefaultAsync<TResult>(sql, param, cancellationToken);
    }

    public Task<TResult?> QuerySingleOrDefaultAsync<TResult>(
        string sql,
        object? param = null,
        CancellationToken cancellationToken = default)
    {
        return _dapperService.QuerySingleOrDefaultAsync<TResult>(sql, param, cancellationToken);
    }

    public Task<int> ExecuteAsync(
        string sql,
        object? param = null,
        CancellationToken cancellationToken = default)
    {
        return _dapperService.ExecuteAsync(sql, param, cancellationToken);
    }

    public Task<T?> ExecuteScalarAsync<T>(
        string sql,
        object? param = null,
        CancellationToken cancellationToken = default)
    {
        return _dapperService.ExecuteScalarAsync<T>(sql, param, cancellationToken);
    }

    public Task<int> BulkInsertAsync<TBulk>(
        IEnumerable<TBulk> entities,
        string? tableName = null,
        CancellationToken cancellationToken = default) where TBulk : class
    {
        return _dapperService.BulkInsertAsync(entities, tableName, cancellationToken);
    }

    public Task<int> BulkUpdateAsync<TBulk>(
        IEnumerable<TBulk> entities,
        string? tableName = null,
        string? keyColumn = null,
        CancellationToken cancellationToken = default) where TBulk : class
    {
        return _dapperService.BulkUpdateAsync(entities, tableName, keyColumn, cancellationToken);
    }

    public Task<int> BulkDeleteAsync<TBulk>(
        IEnumerable<object> keys,
        string? tableName = null,
        string? keyColumn = null,
        CancellationToken cancellationToken = default) where TBulk : class
    {
        return _dapperService.BulkDeleteAsync<TBulk>(keys, tableName, keyColumn, cancellationToken);
    }

    public Task<IEnumerable<TResult>> ExecuteStoredProcedureAsync<TResult>(
        string procedureName,
        object? param = null,
        CancellationToken cancellationToken = default)
    {
        return _dapperService.ExecuteStoredProcedureAsync<TResult>(procedureName, param, cancellationToken);
    }
}
