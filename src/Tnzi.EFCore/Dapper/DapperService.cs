using IDatabaseProvider = Tnzi.EFCore.Dapper.Providers.IDatabaseProvider;

namespace Tnzi.EFCore.Dapper;

/// <summary>
/// Dapper 服务实现
/// 使用 EF Core DbContext 的连接和事务
/// </summary>
public class DapperService : IDapperService
{
    private readonly DbContext _dbContext;
    private readonly IDatabaseProvider _databaseProvider;
    private readonly ILogger<DapperService> _logger;

    public DapperService(
        DbContext dbContext,
        IDatabaseProvider databaseProvider,
        ILogger<DapperService> logger)
    {
        _dbContext = Check.NotNull(dbContext);
        _databaseProvider = Check.NotNull(databaseProvider);
        _logger = Check.NotNull(logger);
    }

    /// <summary>
    /// 获取数据库连接并确保已打开
    /// Dapper 需要连接处于打开状态才能执行 SQL
    /// </summary>
    private async Task<DbConnection> GetConnectionAsync()
    {
        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State == ConnectionState.Closed)
        {
            await connection.OpenAsync();
        }
        return connection;
    }

    /// <summary>
    /// 获取当前事务（如果有）
    /// </summary>
    private IDbTransaction? GetTransaction()
    {
        var transaction = _dbContext.Database.CurrentTransaction;
        return transaction?.GetDbTransaction();
    }

    public async Task<IEnumerable<T>> QueryAsync<T>(
        string sql,
        object? param = null,
        CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync();
        var transaction = GetTransaction();

        return await connection.QueryAsync<T>(
            new CommandDefinition(sql, param, transaction, cancellationToken: cancellationToken));
    }

    public async Task<T?> QueryFirstOrDefaultAsync<T>(
        string sql,
        object? param = null,
        CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync();
        var transaction = GetTransaction();

        return await connection.QueryFirstOrDefaultAsync<T>(
            new CommandDefinition(sql, param, transaction, cancellationToken: cancellationToken));
    }

    public async Task<T?> QuerySingleOrDefaultAsync<T>(
        string sql,
        object? param = null,
        CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync();
        var transaction = GetTransaction();

        return await connection.QuerySingleOrDefaultAsync<T>(
            new CommandDefinition(sql, param, transaction, cancellationToken: cancellationToken));
    }

    public async Task<int> ExecuteAsync(
        string sql,
        object? param = null,
        CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync();
        var transaction = GetTransaction();

        return await connection.ExecuteAsync(
            new CommandDefinition(sql, param, transaction, cancellationToken: cancellationToken));
    }

    public async Task<T?> ExecuteScalarAsync<T>(
        string sql,
        object? param = null,
        CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync();
        var transaction = GetTransaction();

        return await connection.ExecuteScalarAsync<T>(
            new CommandDefinition(sql, param, transaction, cancellationToken: cancellationToken));
    }

    public async Task<int> BulkInsertAsync<T>(
        IEnumerable<T> entities,
        string? tableName = null,
        CancellationToken cancellationToken = default) where T : class
    {
        var connection = await GetConnectionAsync();
        var transaction = GetTransaction();

        return await DapperBulkOperations.BulkInsertAsync(
            connection,
            _databaseProvider,
            _dbContext,
            entities,
            tableName,
            transaction,
            cancellationToken: cancellationToken);
    }

    public async Task<int> BulkUpdateAsync<T>(
        IEnumerable<T> entities,
        string? tableName = null,
        string? keyColumn = null,
        CancellationToken cancellationToken = default) where T : class
    {
        var connection = await GetConnectionAsync();
        var transaction = GetTransaction();

        #pragma warning disable CS0618 // 内部调用已标记为 Obsolete 的方法是预期行为
        return await DapperBulkOperations.BulkUpdateAsync(
            connection,
            _databaseProvider,
            _dbContext,
            entities,
            tableName,
            keyColumn,
            transaction,
            cancellationToken: cancellationToken);
        #pragma warning restore CS0618
    }

    public async Task<int> BulkDeleteAsync<T>(
        IEnumerable<object> keys,
        string? tableName = null,
        string? keyColumn = null,
        CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync();
        var transaction = GetTransaction();

        #pragma warning disable CS0618 // 内部调用已标记为 Obsolete 的方法是预期行为
        return await DapperBulkOperations.BulkDeleteAsync(
            connection,
            _databaseProvider,
            _dbContext,
            keys,
            typeof(T),
            tableName,
            keyColumn,
            transaction,
            cancellationToken: cancellationToken);
        #pragma warning restore CS0618
    }

    public async Task<IEnumerable<T>> ExecuteStoredProcedureAsync<T>(
        string procedureName,
        object? param = null,
        CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync();
        var transaction = GetTransaction();

        return await connection.QueryAsync<T>(
            new CommandDefinition(
                procedureName,
                param,
                transaction,
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken));
    }
}
