namespace Tnzi.EFCore.Interceptors;

/// <summary>
/// PostgreSQL 的文本类型存不了 U+0000：发往 PostgreSQL 的命令里，字符串参数与命令文本中的 U+0000 在执行前换成 U+FFFD。
/// </summary>
/// <remarks>
/// <para>
/// PostgreSQL 的 <c>text</c> / <c>varchar</c> 拒收 NUL（SQLSTATE 22021 <c>invalid byte sequence for encoding "UTF8": 0x00</c>），
/// 而 .NET 字符串、SQL Server、MySQL 与 SQLite 都装得下它。一段外来文本里带一个 NUL 十分平常（读二进制文件、
/// 进程的原始输出、从 PDF 抽出的文字、用户粘贴的内容），于是整次写入失败，调用方只拿到
/// 「An error occurred while saving the entity changes」。这是数据库表示能力的限制，不是哪个模块的数据错了，
/// 所以在数据层一处收口，而不是让每个写外来文本的模块各自记得清洗。
/// </para>
/// <para>
/// 换成 U+FFFD（Unicode 的「无法表示的字符」）而不是删掉：读的人看得出那里原本有个字符，字符数不变，
/// 按字符计的列长上限照旧成立。查询参数一样替换，于是按存进去的原值查得到它（不替换的话那条查询本身就会失败）。
/// </para>
/// <para>
/// 覆盖面是经 EF 发出的每一条命令：<c>SaveChanges</c>、<c>ExecuteUpdate</c> / <c>ExecuteDelete</c>、带参数的原生 SQL 与查询。
/// 不经 EF 的写入（Dapper、直接用 <c>NpgsqlConnection</c>、二进制 COPY）不在其内。
/// 其它数据库原样放行；只有命令里确实有 NUL 时才去判断数据库种类，平常每条命令的代价是一次向量化的字符查找。
/// </para>
/// </remarks>
internal sealed class PostgreSqlNulCharacterInterceptor : DbCommandInterceptor
{
    /// <summary>无状态，所有上下文共用一个。</summary>
    public static readonly PostgreSqlNulCharacterInterceptor Instance = new();

    /// <summary>EF 给 PostgreSQL 提供程序的名字（<c>DatabaseFacade.ProviderName</c>）。</summary>
    internal const string NpgsqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";

    /// <summary>替换 U+0000 的字符。</summary>
    internal const char Replacement = '�';

    private PostgreSqlNulCharacterInterceptor()
    {
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Sanitize(command, eventData);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Sanitize(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Sanitize(command, eventData);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Sanitize(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Sanitize(command, eventData);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Sanitize(command, eventData);
        return ValueTask.FromResult(result);
    }

    private static void Sanitize(DbCommand command, CommandEventData eventData)
    {
        if (!ContainsNul(command) || !IsPostgreSql(command, eventData))
        {
            return;
        }

        var replaced = ReplaceNul(command);
        if (replaced.Count > 0)
        {
            ResolveLogger(eventData)?.LogDebug(
                "Replaced U+0000 with U+FFFD in {Places} of a PostgreSQL command: PostgreSQL text cannot store U+0000",
                string.Join(", ", replaced));
        }
    }

    /// <summary>命令文本或任一字符串参数里有没有 U+0000。</summary>
    internal static bool ContainsNul(DbCommand command)
    {
        if (command.CommandText.Contains('\0'))
        {
            return true;
        }

        foreach (DbParameter parameter in command.Parameters)
        {
            if (parameter.Value switch
                {
                    string text => text.Contains('\0'),
                    string?[] texts => texts.Any(t => t is not null && t.Contains('\0')),
                    List<string?> texts => texts.Any(t => t is not null && t.Contains('\0')),
                    _ => false
                })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 把命令文本与字符串参数（含 PostgreSQL 文本数组列的 <c>string[]</c> / <c>List&lt;string&gt;</c>）里的 U+0000 换成 U+FFFD。
    /// </summary>
    /// <returns>改过的地方（参数名，命令文本记作 <c>CommandText</c>），供日志用。</returns>
    /// <remarks>
    /// 集合参数换成新的集合实例，不改调用方手里的那一个（它可能就是被跟踪实体的属性值）。
    /// </remarks>
    internal static IReadOnlyList<string> ReplaceNul(DbCommand command)
    {
        var replaced = new List<string>();

        if (command.CommandText.Contains('\0'))
        {
            command.CommandText = command.CommandText.Replace('\0', Replacement);
            replaced.Add("CommandText");
        }

        foreach (DbParameter parameter in command.Parameters)
        {
            object? sanitized = parameter.Value switch
            {
                string text when text.Contains('\0') => text.Replace('\0', Replacement),
                string?[] texts when texts.Any(HasNul) => texts.Select(ReplaceIn).ToArray(),
                List<string?> texts when texts.Any(HasNul) => texts.Select(ReplaceIn).ToList(),
                _ => null
            };

            if (sanitized is not null)
            {
                parameter.Value = sanitized;
                replaced.Add(parameter.ParameterName);
            }
        }

        return replaced;
    }

    private static bool HasNul(string? text) => text is not null && text.Contains('\0');

    private static string? ReplaceIn(string? text) => text?.Replace('\0', Replacement);

    /// <summary>
    /// 命令发往 PostgreSQL 吗。以上下文的提供程序为准（包装过的命令，例如性能剖析器的，类型认不出来）；
    /// 没有上下文时看命令类型所在的命名空间。按名字判断，本包不引用 Npgsql。
    /// </summary>
    private static bool IsPostgreSql(DbCommand command, CommandEventData eventData)
    {
        if (eventData.Context is { } context)
        {
            return string.Equals(context.Database.ProviderName, NpgsqlProviderName, StringComparison.Ordinal);
        }

        return command.GetType().Namespace?.StartsWith("Npgsql", StringComparison.Ordinal) == true;
    }

    private static ILogger? ResolveLogger(CommandEventData eventData)
        => eventData.Context?.GetService<ILoggerFactory>()?.CreateLogger<PostgreSqlNulCharacterInterceptor>();
}
