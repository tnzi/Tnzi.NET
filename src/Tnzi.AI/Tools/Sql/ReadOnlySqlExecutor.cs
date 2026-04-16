using System.Data;
using System.Data.Common;
using Tnzi.DependencyInjection;

namespace Tnzi.AI.Tools.Sql;

/// <summary>
/// Default <see cref="IReadOnlySqlExecutor"/> implementation.
/// Validates SQL, optionally checks permissions, executes within a read-committed
/// transaction that is always rolled back, and enforces row limits and timeouts.
/// </summary>
public sealed class ReadOnlySqlExecutor : IReadOnlySqlExecutor, IScopedDependency
{
    private readonly ISqlValidator _validator;
    private readonly Func<string?, DbConnection> _connectionFactory;
    private readonly ILogger<ReadOnlySqlExecutor> _logger;
    private readonly IReadOnlySqlPermissionCheck? _permissionCheck;

    public ReadOnlySqlExecutor(
        ISqlValidator validator,
        Func<string?, DbConnection> connectionFactory,
        ILogger<ReadOnlySqlExecutor> logger,
        IReadOnlySqlPermissionCheck? permissionCheck = null)
    {
        _validator = Check.NotNull(validator);
        _connectionFactory = Check.NotNull(connectionFactory);
        _logger = Check.NotNull(logger);
        _permissionCheck = permissionCheck;
    }

    public async Task<QueryResult> ExecuteAsync(
        string sql,
        ReadOnlySqlExecutionOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        options ??= new ReadOnlySqlExecutionOptions();

        // Layer 1: SQL validation
        var validation = _validator.Validate(sql);
        if (!validation.IsValid)
            throw new InvalidOperationException($"SQL validation failed: {validation.ErrorMessage}");

        // Layer 2: permission check
        if (_permissionCheck is not null)
        {
            var permission = await _permissionCheck.CheckAsync(sql, ct);
            if (!permission.Allowed)
                throw new UnauthorizedAccessException(
                    $"SQL execution denied: {permission.DenialReason ?? "access denied"}");
        }

        var conn = _connectionFactory(options.ConnectionName);
        DbTransaction? tx = null;
        var sw = Stopwatch.StartNew();

        try
        {
            if (conn.State != ConnectionState.Open)
                await conn.OpenAsync(ct);

            // Begin read-committed transaction; always rolled back for read-only enforcement
            tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.Transaction = tx;
            cmd.CommandTimeout = options.TimeoutSeconds;

            await using var reader = await cmd.ExecuteReaderAsync(ct);

            // Build column list
            var columns = new List<QueryColumn>(reader.FieldCount);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var name = reader.GetName(i);
                var typeName = reader.GetFieldType(i).Name;
                columns.Add(new QueryColumn(name, typeName));
            }

            // Read rows up to MaxRows
            var rows = new List<IReadOnlyList<object?>>();
            var truncated = false;

            while (await reader.ReadAsync(ct))
            {
                if (rows.Count >= options.MaxRows)
                {
                    truncated = true;
                    break;
                }

                var row = new object?[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++)
                    row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);

                rows.Add(row);
            }

            sw.Stop();
            _logger.LogDebug("SQL executed in {ElapsedMs}ms, {RowCount} rows, truncated={Truncated}",
                sw.ElapsedMilliseconds, rows.Count, truncated);

            return new QueryResult(columns, rows, truncated, sw.ElapsedMilliseconds, sql);
        }
        finally
        {
            // Always rollback — enforces read-only semantics even if provider ignores isolation level
            if (tx is not null)
            {
                await tx.RollbackAsync(CancellationToken.None);
                await tx.DisposeAsync();
            }

            await conn.DisposeAsync();
        }
    }
}
