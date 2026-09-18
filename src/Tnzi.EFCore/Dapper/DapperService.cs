using IDatabaseProvider = Tnzi.EFCore.Dapper.Providers.IDatabaseProvider;

namespace Tnzi.EFCore.Dapper;

/// <summary>
/// Dapper 服务实现
/// 使用 EF Core DbContext 的连接和事务
/// </summary>
/// <remarks>
/// 框架的物理事务是延迟开启的（首次 UoW SaveChanges 才 BEGIN），在那之前
/// <c>Database.CurrentTransaction</c> 恒为 null。写操作若只读它，会在「事务内第一次 flush 之前」
/// 以自动提交落库、回滚撤不掉。因此写入口先经 <see cref="IUnitOfWorkManager"/> 幂等地强开物理事务
/// （与仓储的 ExecuteUpdate/ExecuteDelete 路径同一惯例），再把事务交给 Dapper。
/// 与仓储守卫一样有第二个分支：没有管理器（或管理器没开事务）而直接注入的作用域 <see cref="IUnitOfWork"/>
/// 开了延迟事务（<c>EnableTransaction</c>，没有 <c>BeginTransactionAsync</c>）时，同样先加入它。
/// 两边都没开事务时保持自动提交 —— 那是正确语义，不是缺陷。
/// </remarks>
public class DapperService : IDapperService
{
    private readonly DbContext _dbContext;
    private readonly IDatabaseProvider _databaseProvider;
    private readonly IUnitOfWorkManager? _unitOfWorkManager;
    private readonly IUnitOfWork? _unitOfWork;

    public DapperService(
        DbContext dbContext,
        IDatabaseProvider databaseProvider,
        IUnitOfWorkManager? unitOfWorkManager = null,
        IUnitOfWork? unitOfWork = null)
    {
        _dbContext = Check.NotNull(dbContext);
        _databaseProvider = Check.NotNull(databaseProvider);
        _unitOfWorkManager = unitOfWorkManager;
        _unitOfWork = unitOfWork;
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

    /// <summary>
    /// 写操作前加入工作单元的延迟事务（幂等），返回语句应使用的事务。
    /// </summary>
    /// <remarks>
    /// 失败方向关闭：事务已启用却加入不了（这个 DbContext 实例不是工作单元管理的那个）时抛出，
    /// 而不是静默回落到自动提交 —— 后者正是本方法要消灭的形态。
    /// </remarks>
    private async Task<IDbTransaction?> JoinUnitOfWorkTransactionAsync(CancellationToken cancellationToken)
    {
        var dbContextType = _dbContext.GetType();

        if (_unitOfWorkManager is { IsEnabledTransaction: true })
        {
            var managedUnitOfWork = _unitOfWorkManager.GetUnitOfWork(dbContextType)
                ?? throw new InvalidOperationException(
                    $"A unit of work transaction is enabled but no unit of work could be created for DbContext '{dbContextType.Name}'. " +
                    "The Dapper statement would run outside the transaction, so it is refused.");

            await managedUnitOfWork.EnsureTransactionStartedAsync(cancellationToken);
        }
        else if (_unitOfWork is { IsEnabledTransaction: true })
        {
            // 仓储守卫的第二个分支（EfCoreRepository.EnsureTransactionStartedAsync）：
            // 作用域 IUnitOfWork 绑定的是主 DbContext，这里的上下文若是另一个实例，下面的空事务检查会拒绝。
            await _unitOfWork.EnsureTransactionStartedAsync(cancellationToken);
        }
        else
        {
            return GetTransaction();
        }

        return GetTransaction()
            ?? throw new InvalidOperationException(
                $"A unit of work transaction is enabled but DbContext '{dbContextType.Name}' has no current transaction after joining it. " +
                "This DbContext instance is not the one the unit of work manages (for example it was constructed manually); " +
                "resolve the DbContext from the request scope instead.");
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
        var transaction = await JoinUnitOfWorkTransactionAsync(cancellationToken);

        return await connection.ExecuteAsync(
            new CommandDefinition(sql, param, transaction, cancellationToken: cancellationToken));
    }

    public async Task<T?> ExecuteScalarAsync<T>(
        string sql,
        object? param = null,
        CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync();
        var transaction = await JoinUnitOfWorkTransactionAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<T>(
            new CommandDefinition(sql, param, transaction, cancellationToken: cancellationToken));
    }

    public async Task<int> BulkInsertAsync<T>(
        IEnumerable<T> entities,
        string? tableName = null,
        CancellationToken cancellationToken = default) where T : class
    {
        var connection = await GetConnectionAsync();
        var transaction = await JoinUnitOfWorkTransactionAsync(cancellationToken);

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
        var transaction = await JoinUnitOfWorkTransactionAsync(cancellationToken);

        return await DapperBulkOperations.BulkUpdateAsync(
            connection,
            _databaseProvider,
            _dbContext,
            entities,
            tableName,
            keyColumn,
            transaction,
            cancellationToken: cancellationToken);
    }

    public async Task<int> BulkDeleteAsync<T>(
        IEnumerable<object> keys,
        string? tableName = null,
        string? keyColumn = null,
        CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync();
        var transaction = await JoinUnitOfWorkTransactionAsync(cancellationToken);

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
    }

    public async Task<IEnumerable<T>> ExecuteStoredProcedureAsync<T>(
        string procedureName,
        object? param = null,
        CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync();
        var transaction = await JoinUnitOfWorkTransactionAsync(cancellationToken);

        return await connection.QueryAsync<T>(
            new CommandDefinition(
                procedureName,
                param,
                transaction,
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken));
    }
}
