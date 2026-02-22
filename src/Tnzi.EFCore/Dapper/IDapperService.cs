namespace Tnzi.EFCore.Dapper;

/// <summary>
/// Dapper 服务接口 - 用于执行原始 SQL 和批量操作
/// 作为 EF Core 的补充，用于复杂查询和批量操作场景
/// </summary>
public interface IDapperService
{
    /// <summary>
    /// 执行查询并返回结果集
    /// </summary>
    Task<IEnumerable<T>> QueryAsync<T>(
        string sql, 
        object? param = null, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行查询并返回第一个结果或默认值
    /// </summary>
    Task<T?> QueryFirstOrDefaultAsync<T>(
        string sql, 
        object? param = null, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行查询并返回单个结果或默认值
    /// </summary>
    Task<T?> QuerySingleOrDefaultAsync<T>(
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
    /// 批量插入（使用数据库特定的批量插入语法）
    /// </summary>
    Task<int> BulkInsertAsync<T>(
        IEnumerable<T> entities, 
        string? tableName = null,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// 批量更新
    /// </summary>
    Task<int> BulkUpdateAsync<T>(
        IEnumerable<T> entities,
        string? tableName = null,
        string? keyColumn = null,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// 批量删除（使用 IN 子句）
    /// </summary>
    Task<int> BulkDeleteAsync<T>(
        IEnumerable<object> keys,
        string? tableName = null,
        string? keyColumn = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行存储过程
    /// </summary>
    Task<IEnumerable<T>> ExecuteStoredProcedureAsync<T>(
        string procedureName,
        object? param = null,
        CancellationToken cancellationToken = default);
}

