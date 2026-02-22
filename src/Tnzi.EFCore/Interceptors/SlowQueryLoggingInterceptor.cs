
namespace Tnzi.EFCore.Interceptors;

/// <summary>
/// 慢查询日志拦截器
/// 记录执行时间超过阈值的 SQL 查询
/// </summary>
public class SlowQueryLoggingInterceptor : DbCommandInterceptor
{
    private readonly ILogger<SlowQueryLoggingInterceptor> _logger;
    private readonly EFCoreOptions _options;

    public SlowQueryLoggingInterceptor(
        ILogger<SlowQueryLoggingInterceptor> logger,
        IOptions<EFCoreOptions> options)
    {
        _logger = Check.NotNull(logger);
        _options = Check.NotNull(options).Value;
    }

    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result)
    {
        LogSlowQuery(command, eventData);
        return base.ReaderExecuted(command, eventData, result);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        LogSlowQuery(command, eventData);
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override int NonQueryExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result)
    {
        LogSlowQuery(command, eventData);
        return base.NonQueryExecuted(command, eventData, result);
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        LogSlowQuery(command, eventData);
        return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override object? ScalarExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result)
    {
        LogSlowQuery(command, eventData);
        return base.ScalarExecuted(command, eventData, result);
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default)
    {
        LogSlowQuery(command, eventData);
        return base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
    }

    private void LogSlowQuery(DbCommand command, CommandExecutedEventData eventData)
    {
        if (!_options.EnableSlowQueryLogging)
            return;

        var elapsedMs = eventData.Duration.TotalMilliseconds;
        if (elapsedMs < _options.SlowQueryThresholdMs)
            return;

        var commandText = command.CommandText;
        var parameters = _options.LogQueryParameters 
            ? FormatParameters(command.Parameters) 
            : "[Parameters hidden]";

        _logger.LogWarning(
            "Slow query detected ({ElapsedMs:F2}ms > {ThresholdMs}ms):\n" +
            "SQL: {CommandText}\n" +
            "Parameters: {Parameters}",
            elapsedMs,
            _options.SlowQueryThresholdMs,
            commandText,
            parameters);
    }

    private static string FormatParameters(DbParameterCollection parameters)
    {
        if (parameters.Count == 0)
            return "(none)";

        var parts = new List<string>();
        foreach (DbParameter param in parameters)
        {
            var value = param.Value == DBNull.Value ? "NULL" : param.Value?.ToString() ?? "NULL";
            parts.Add($"{param.ParameterName}={value}");
        }
        return string.Join(", ", parts);
    }
}