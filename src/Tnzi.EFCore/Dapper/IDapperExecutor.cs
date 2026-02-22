
namespace Tnzi.EFCore.Dapper;

/// <summary>
/// Dapper 执行器接口（通过实体类型自动定位 DbContext）
/// 用于多 DbContext 场景，自动根据实体类型找到对应的 DbContext
/// </summary>
/// <typeparam name="TEntity">实体类型</typeparam>
/// <typeparam name="TKey">主键类型</typeparam>
public interface IDapperExecutor<TEntity, TKey> 
    where TEntity : class, IEntity<TKey>
{
    /// <summary>
    /// 执行查询并返回结果集
    /// </summary>
    Task<IEnumerable<TResult>> QueryAsync<TResult>(
        string sql, 
        object? param = null, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行查询并返回第一个结果或默认值
    /// </summary>
    Task<TResult?> QueryFirstOrDefaultAsync<TResult>(
        string sql, 
        object? param = null, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行查询并返回单个结果或默认值
    /// </summary>
    Task<TResult?> QuerySingleOrDefaultAsync<TResult>(
        string sql, 
        object? param = null, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行非查询 SQL（INSERT、UPDATE、DELETE）
    /// </summary>
    Task<int> ExecuteAsync(
        string sql, 
        object? param = null, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行查询并返回标量值
    /// </summary>
    Task<T?> ExecuteScalarAsync<T>(
        string sql, 
        object? param = null, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量插入
    /// </summary>
    Task<int> BulkInsertAsync<TBulk>(
        IEnumerable<TBulk> entities, 
        string? tableName = null,
        CancellationToken cancellationToken = default) where TBulk : class;

    /// <summary>
    /// 批量更新
    /// </summary>
    Task<int> BulkUpdateAsync<TBulk>(
        IEnumerable<TBulk> entities,
        string? tableName = null,
        string? keyColumn = null,
        CancellationToken cancellationToken = default) where TBulk : class;

    /// <summary>
    /// 批量删除
    /// </summary>
    Task<int> BulkDeleteAsync<TBulk>(
        IEnumerable<object> keys,
        string? tableName = null,
        string? keyColumn = null,
        CancellationToken cancellationToken = default) where TBulk : class;

    /// <summary>
    /// 执行存储过程
    /// </summary>
    Task<IEnumerable<TResult>> ExecuteStoredProcedureAsync<TResult>(
        string procedureName,
        object? param = null,
        CancellationToken cancellationToken = default);
}