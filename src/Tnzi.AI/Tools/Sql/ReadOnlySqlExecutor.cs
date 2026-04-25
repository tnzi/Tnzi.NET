using System.Data;
using System.Data.Common;
using Microsoft.Extensions.Options;

namespace Tnzi.AI.Tools.Sql;

/// <summary>
/// Default <see cref="IReadOnlySqlExecutor"/> implementation.
/// Validates SQL via <see cref="ISqlValidator"/>, applies the mandatory
/// <see cref="IReadOnlySqlPermissionCheck"/>, executes within a read-committed
/// transaction that is always rolled back, and enforces row count / timeout / byte budget.
/// </summary>
public sealed class ReadOnlySqlExecutor : IReadOnlySqlExecutor
{
    private readonly ISqlValidator _validator;
    private readonly Func<string?, DbConnection> _connectionFactory;
    private readonly ILogger<ReadOnlySqlExecutor> _logger;
    private readonly IReadOnlySqlPermissionCheck _permissionCheck;
    private readonly SqlToolOptions _toolOptions;

    public ReadOnlySqlExecutor(
        ISqlValidator validator,
        Func<string?, DbConnection> connectionFactory,
        IReadOnlySqlPermissionCheck permissionCheck,
        IOptions<SqlToolOptions> toolOptions,
        ILogger<ReadOnlySqlExecutor> logger)
    {
        _validator = Check.NotNull(validator);
        _connectionFactory = Check.NotNull(connectionFactory);
        _permissionCheck = Check.NotNull(permissionCheck);
        _toolOptions = Check.NotNull(toolOptions).Value;
        _logger = Check.NotNull(logger);
    }

    public async Task<QueryResult> ExecuteAsync(
        string sql,
        ReadOnlySqlExecutionOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        options ??= new ReadOnlySqlExecutionOptions();
        var dialect = options.Dialect ?? _toolOptions.DefaultDialect;
        var maxRows = ClampRows(options.MaxRows ?? _toolOptions.DefaultMaxRows);
        var timeoutSeconds = ClampTimeout(options.TimeoutSeconds ?? _toolOptions.DefaultTimeoutSeconds);

        if (_toolOptions.AllowedConnectionNames.Length > 0)
        {
            var name = options.ConnectionName ?? string.Empty;
            if (!_toolOptions.AllowedConnectionNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException(
                    $"Connection '{name}' is not in SqlToolOptions.AllowedConnectionNames.");
        }

        // Layer 1: SQL validation
        var validation = _validator.Validate(sql, dialect);
        if (!validation.IsValid)
            throw new InvalidOperationException($"SQL validation failed: {validation.ErrorMessage}");

        // Layer 2: permission check (mandatory; default impl is DenyAllSqlPermissionCheck)
        var permission = await _permissionCheck.CheckAsync(sql, ct);
        if (!permission.Allowed)
            throw new UnauthorizedAccessException(
                $"SQL execution denied: {permission.DenialReason ?? "access denied"}");

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
            cmd.CommandTimeout = timeoutSeconds;

            await using var reader = await cmd.ExecuteReaderAsync(ct);

            var columns = new List<QueryColumn>(reader.FieldCount);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var name = reader.GetName(i);
                var typeName = reader.GetFieldType(i).Name;
                columns.Add(new QueryColumn(name, typeName));
            }

            var rows = new List<IReadOnlyList<object?>>();
            var truncated = false;
            long approxBytes = 0L;
            var byteBudget = _toolOptions.MaxResultBytes;

            while (await reader.ReadAsync(ct))
            {
                if (rows.Count >= maxRows)
                {
                    truncated = true;
                    break;
                }

                var row = new object?[reader.FieldCount];
                long rowBytes = 0L;
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    if (reader.IsDBNull(i))
                    {
                        row[i] = null;
                        continue;
                    }
                    var v = reader.GetValue(i);
                    row[i] = v;
                    rowBytes += EstimateValueBytes(v);
                }

                if (approxBytes + rowBytes > byteBudget)
                {
                    truncated = true;
                    break;
                }
                approxBytes += rowBytes;
                rows.Add(row);
            }

            sw.Stop();
            _logger.LogDebug("SQL executed in {ElapsedMs}ms, {RowCount} rows, ~{Bytes}B, truncated={Truncated}",
                sw.ElapsedMilliseconds, rows.Count, approxBytes, truncated);

            return new QueryResult(columns, rows, truncated, sw.ElapsedMilliseconds, sql);
        }
        finally
        {
            if (tx is not null)
            {
                await tx.RollbackAsync(CancellationToken.None);
                await tx.DisposeAsync();
            }

            await conn.DisposeAsync();
        }
    }

    private int ClampRows(int requested)
    {
        if (requested <= 0) return _toolOptions.DefaultMaxRows;
        return Math.Min(requested, _toolOptions.MaxAllowedMaxRows);
    }

    private int ClampTimeout(int requested)
    {
        if (requested <= 0) return _toolOptions.DefaultTimeoutSeconds;
        return Math.Min(requested, _toolOptions.MaxAllowedTimeoutSeconds);
    }

    private static long EstimateValueBytes(object value) => value switch
    {
        string s => 2L * s.Length + 24,
        byte[] b => b.LongLength + 24,
        Guid => 16,
        DateTime => 8,
        DateTimeOffset => 12,
        TimeSpan => 8,
        bool => 1,
        long or ulong or double => 8,
        int or uint or float => 4,
        short or ushort => 2,
        byte or sbyte => 1,
        decimal => 16,
        _ => 32
    };
}
