
namespace Tnzi.EFCore;

/// <summary>
/// 索引过滤条件工厂，提供不同数据库的 HasFilter SQL 表达式
/// </summary>
/// <remarks>
/// EF Core 的 HasFilter 接受原始 SQL 字符串，且无跨库统一语法（标识符引用、布尔字面量等因数据库而异）。
/// 此工厂提供按 DatabaseProvider 的过滤 SQL，避免在实体配置中硬编码数据库特定的 HasFilter。
/// <para>
/// 使用示例（推荐方式，自动检测数据库提供者）：
/// <code>
/// public class OrganizationConfiguration : EntityTypeConfigurationBase&lt;Organization, Guid&gt;
/// {
///     public override void Configure(EntityTypeBuilder&lt;Organization&gt; builder)
///     {
///         builder.HasIndex(o => o.Code)
///             .IsUnique()
///             .HasFilter(IndexFilterFactory.GetCodeNotNullAndIsDeletedFalse());
///     }
/// }
/// </code>
/// </para>
/// <para>
/// 使用示例（显式指定数据库提供者）：
/// <code>
/// builder.HasIndex(o => o.Code)
///     .IsUnique()
///     .HasFilter(IndexFilterFactory.GetCodeNotNullAndIsDeletedFalse(DatabaseProvider.PostgreSQL));
/// 
/// builder.HasIndex(t => new { t.Module, t.Category, t.TemplateName })
///     .IsUnique()
///     .HasFilter(IndexFilterFactory.GetIsDeletedFalse(DatabaseProvider.PostgreSQL));
/// </code>
/// </para>
/// <para>
/// 重要：请勿在实体配置中硬编码 HasFilter 的 SQL（如 &quot;IsDeleted&quot; = false），
/// 应始终使用此工厂来确保跨数据库兼容性。
/// </para>
/// </remarks>
public static class IndexFilterFactory
{
    private static readonly FrozenDictionary<DatabaseProvider, (string IsDeletedFalse, string CodeNotNullAndIsDeletedFalse)> _filters;

    static IndexFilterFactory()
    {
        _filters = new Dictionary<DatabaseProvider, (string, string)>
        {
            [DatabaseProvider.SqlServer] = (
                "[IsDeleted] = 0",
                "[Code] IS NOT NULL AND [IsDeleted] = 0"
            ),
            [DatabaseProvider.PostgreSQL] = (
                "\"IsDeleted\" = false",
                "\"Code\" IS NOT NULL AND \"IsDeleted\" = false"
            ),
            [DatabaseProvider.MySql] = (
                "`IsDeleted` = FALSE",
                "`Code` IS NOT NULL AND `IsDeleted` = FALSE"
            ),
            [DatabaseProvider.Sqlite] = (
                "\"IsDeleted\" = 0",
                "\"Code\" IS NOT NULL AND \"IsDeleted\" = 0"
            )
        }.ToFrozenDictionary();
    }

    /// <summary>
    /// 获取 "IsDeleted = false" 的过滤 SQL，用于唯一索引排除软删除行
    /// </summary>
    /// <param name="provider">数据库提供者类型</param>
    /// <returns>HasFilter 可用的 SQL 字符串</returns>
    /// <remarks>
    /// SQL Server: [IsDeleted] = 0
    /// PostgreSQL: "IsDeleted" = false
    /// MySQL: `IsDeleted` = FALSE
    /// SQLite: "IsDeleted" = 0
    /// </remarks>
    public static string GetIsDeletedFalse(DatabaseProvider provider)
    {
        if (!_filters.TryGetValue(provider, out var pair))
        {
            throw new NotSupportedException(
                $"Database provider {provider} is not supported. " +
                $"Supported providers: {string.Join(", ", _filters.Keys)}.");
        }

        return pair.IsDeletedFalse;
    }

    /// <summary>
    /// 获取 "IsDeleted = false" 的过滤 SQL，用于唯一索引排除软删除行（自动检测数据库提供者）
    /// </summary>
    /// <returns>HasFilter 可用的 SQL 字符串</returns>
    /// <remarks>
    /// 此方法自动从 EntityTypeConfigurationBase 的配置上下文中获取数据库提供者，
    /// 无需显式传递 provider 参数。只能在 EntityTypeConfigurationBase.Configure 方法内部调用。
    /// <para>
    /// SQL Server: [IsDeleted] = 0
    /// PostgreSQL: "IsDeleted" = false
    /// MySQL: `IsDeleted` = FALSE
    /// SQLite: "IsDeleted" = 0
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">当在 EntityTypeConfigurationBase.Configure 方法外部调用时抛出</exception>
    public static string GetIsDeletedFalse()
    {
        var provider = EntityConfigurationContext.GetCurrentDatabaseProviderOrDefault();
        return GetIsDeletedFalse(provider);
    }

    /// <summary>
    /// 构建 "{列} = {false 字面量}" 的布尔判假表达式（按 provider 的标识符引用规则与布尔字面量）。
    /// </summary>
    private static string BuildIsFalseExpression(string columnName, DatabaseProvider provider)
    {
        var column = QuoteIdentifier(columnName, provider);
        return provider switch
        {
            DatabaseProvider.SqlServer => $"{column} = 0",
            DatabaseProvider.PostgreSQL => $"{column} = false",
            DatabaseProvider.MySql => $"{column} = FALSE",
            DatabaseProvider.Sqlite => $"{column} = 0",
            _ => throw new NotSupportedException(
                $"Database provider {provider} is not supported. " +
                $"Supported providers: {string.Join(", ", _filters.Keys)}.")
        };
    }

    /// <summary>
    /// 构建 "{列} = {true 字面量}" 的布尔判真表达式（按 provider 的标识符引用规则与布尔字面量）。
    /// </summary>
    private static string BuildIsTrueExpression(string columnName, DatabaseProvider provider)
    {
        var column = QuoteIdentifier(columnName, provider);
        return provider switch
        {
            DatabaseProvider.SqlServer => $"{column} = 1",
            DatabaseProvider.PostgreSQL => $"{column} = true",
            DatabaseProvider.MySql => $"{column} = TRUE",
            DatabaseProvider.Sqlite => $"{column} = 1",
            _ => throw new NotSupportedException(
                $"Database provider {provider} is not supported. " +
                $"Supported providers: {string.Join(", ", _filters.Keys)}.")
        };
    }

    /// <summary>
    /// 获取 "IsDeleted = false" 的过滤 SQL，允许自定义 IsDeleted 列名。
    /// </summary>
    /// <param name="isDeletedColumn">IsDeleted 属性对应的实际数据库列名</param>
    /// <param name="provider">数据库提供者类型</param>
    /// <returns>HasFilter 可用的 SQL 字符串</returns>
    /// <remarks>
    /// 当实体用 <c>HasColumnName</c> 或 snake_case 命名约定使 IsDeleted 列名不等于属性名时，
    /// MUST 使用此重载传入实际列名；否则无参重载生成的 SQL 会指向不存在的列（硬编码 "IsDeleted"）。
    /// </remarks>
    public static string GetIsDeletedFalse(string isDeletedColumn, DatabaseProvider provider)
    {
        Check.NotNullOrWhiteSpace(isDeletedColumn);
        return BuildIsFalseExpression(isDeletedColumn, provider);
    }

    /// <summary>
    /// 获取 "IsDeleted = false" 的过滤 SQL，允许自定义 IsDeleted 列名（自动检测数据库提供者）。
    /// 只能在 EntityTypeConfigurationBase.Configure 方法内部调用。
    /// </summary>
    /// <param name="isDeletedColumn">IsDeleted 属性对应的实际数据库列名</param>
    /// <returns>HasFilter 可用的 SQL 字符串</returns>
    public static string GetIsDeletedFalse(string isDeletedColumn)
    {
        Check.NotNullOrWhiteSpace(isDeletedColumn);
        var provider = EntityConfigurationContext.GetCurrentDatabaseProviderOrDefault();
        return BuildIsFalseExpression(isDeletedColumn, provider);
    }

    /// <summary>
    /// 获取 "Code IS NOT NULL AND IsDeleted = false" 的过滤 SQL，用于唯一索引排除空 Code 与软删除行
    /// </summary>
    /// <param name="provider">数据库提供者类型</param>
    /// <returns>HasFilter 可用的 SQL 字符串</returns>
    /// <remarks>
    /// SQL Server: [Code] IS NOT NULL AND [IsDeleted] = 0
    /// PostgreSQL: "Code" IS NOT NULL AND "IsDeleted" = false
    /// MySQL: `Code` IS NOT NULL AND `IsDeleted` = FALSE
    /// SQLite: "Code" IS NOT NULL AND "IsDeleted" = 0
    /// </remarks>
    public static string GetCodeNotNullAndIsDeletedFalse(DatabaseProvider provider)
    {
        if (!_filters.TryGetValue(provider, out var pair))
        {
            throw new NotSupportedException(
                $"Database provider {provider} is not supported. " +
                $"Supported providers: {string.Join(", ", _filters.Keys)}.");
        }

        return pair.CodeNotNullAndIsDeletedFalse;
    }

    /// <summary>
    /// 获取 "Code IS NOT NULL AND IsDeleted = false" 的过滤 SQL，用于唯一索引排除空 Code 与软删除行（自动检测数据库提供者）
    /// </summary>
    /// <returns>HasFilter 可用的 SQL 字符串</returns>
    /// <remarks>
    /// 此方法自动从 EntityTypeConfigurationBase 的配置上下文中获取数据库提供者，
    /// 无需显式传递 provider 参数。只能在 EntityTypeConfigurationBase.Configure 方法内部调用。
    /// <para>
    /// SQL Server: [Code] IS NOT NULL AND [IsDeleted] = 0
    /// PostgreSQL: "Code" IS NOT NULL AND "IsDeleted" = false
    /// MySQL: `Code` IS NOT NULL AND `IsDeleted` = FALSE
    /// SQLite: "Code" IS NOT NULL AND "IsDeleted" = 0
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">当在 EntityTypeConfigurationBase.Configure 方法外部调用时抛出</exception>
    public static string GetCodeNotNullAndIsDeletedFalse()
    {
        var provider = EntityConfigurationContext.GetCurrentDatabaseProviderOrDefault();
        return GetCodeNotNullAndIsDeletedFalse(provider);
    }

    /// <summary>
    /// 获取 "Code IS NOT NULL AND IsDeleted = false" 的过滤 SQL，允许自定义 Code 与 IsDeleted 列名。
    /// </summary>
    /// <param name="codeColumn">Code 属性对应的实际数据库列名</param>
    /// <param name="isDeletedColumn">IsDeleted 属性对应的实际数据库列名</param>
    /// <param name="provider">数据库提供者类型</param>
    /// <returns>HasFilter 可用的 SQL 字符串</returns>
    /// <remarks>
    /// 当实体用 <c>HasColumnName</c> 或 snake_case 命名约定使列名不等于属性名时，MUST 使用此重载；
    /// 否则无参重载生成的 SQL 会指向不存在的列（硬编码 "Code" / "IsDeleted"）。
    /// </remarks>
    public static string GetCodeNotNullAndIsDeletedFalse(string codeColumn, string isDeletedColumn, DatabaseProvider provider)
    {
        Check.NotNullOrWhiteSpace(codeColumn);
        Check.NotNullOrWhiteSpace(isDeletedColumn);
        return $"{QuoteIdentifier(codeColumn, provider)} IS NOT NULL AND {BuildIsFalseExpression(isDeletedColumn, provider)}";
    }

    /// <summary>
    /// 获取 "Code IS NOT NULL AND IsDeleted = false" 的过滤 SQL，允许自定义列名（自动检测数据库提供者）。
    /// 只能在 EntityTypeConfigurationBase.Configure 方法内部调用。
    /// </summary>
    /// <param name="codeColumn">Code 属性对应的实际数据库列名</param>
    /// <param name="isDeletedColumn">IsDeleted 属性对应的实际数据库列名</param>
    /// <returns>HasFilter 可用的 SQL 字符串</returns>
    public static string GetCodeNotNullAndIsDeletedFalse(string codeColumn, string isDeletedColumn)
    {
        Check.NotNullOrWhiteSpace(codeColumn);
        Check.NotNullOrWhiteSpace(isDeletedColumn);
        var provider = EntityConfigurationContext.GetCurrentDatabaseProviderOrDefault();
        return GetCodeNotNullAndIsDeletedFalse(codeColumn, isDeletedColumn, provider);
    }

    /// <summary>
    /// 按数据库提供者的引用规则引用列名标识符
    /// </summary>
    /// <param name="identifier">列名标识符</param>
    /// <param name="provider">数据库提供者类型</param>
    /// <returns>引用后的标识符</returns>
    /// <remarks>
    /// SQL Server: [ColumnName]
    /// PostgreSQL: "ColumnName"
    /// MySQL: `ColumnName`
    /// SQLite: "ColumnName"
    /// </remarks>
    public static string QuoteIdentifier(string identifier, DatabaseProvider provider) => provider switch
    {
        DatabaseProvider.SqlServer => $"[{identifier}]",
        DatabaseProvider.MySql => $"`{identifier}`",
        _ => $"\"{identifier}\""
    };

    /// <summary>
    /// 获取 "columnName IS NOT NULL" 的过滤 SQL，用于唯一索引排除 NULL 值行
    /// </summary>
    /// <param name="columnName">列名</param>
    /// <param name="provider">数据库提供者类型</param>
    /// <returns>HasFilter 可用的 SQL 字符串</returns>
    public static string GetColumnNotNull(string columnName, DatabaseProvider provider)
    {
        return $"{QuoteIdentifier(columnName, provider)} IS NOT NULL";
    }

    /// <summary>
    /// 获取 "columnName IS NOT NULL" 的过滤 SQL（自动检测数据库提供者）
    /// </summary>
    /// <param name="columnName">列名</param>
    /// <returns>HasFilter 可用的 SQL 字符串</returns>
    public static string GetColumnNotNull(string columnName)
    {
        var provider = EntityConfigurationContext.GetCurrentDatabaseProviderOrDefault();
        return GetColumnNotNull(columnName, provider);
    }

    /// <summary>
    /// 获取 "columnName IS NULL" 的过滤 SQL。
    /// </summary>
    /// <remarks>
    /// ★ 与 <see cref="GetColumnNotNull(string)"/> 配对使用，把「可空列参与的唯一约束」拆成两条索引：
    /// 一条管有值的行，一条管 NULL 的那一支。
    ///
    /// 原因是 <b>各家数据库对唯一索引里的 NULL 判定不同</b>：PostgreSQL / SQLite 认为
    /// NULL 互不相等（同一组值可以插进任意多行），SQL Server 认为 NULL 彼此相等（只许一行）。
    /// 于是「(A, B, 可空C) 唯一」这条约束在不同 provider 上表达的是两件事，而
    /// <b>"在另一个库上跑得好好的"正是这类缺陷最擅长的伪装</b>。
    ///
    /// 只有当 NULL 表示「一个共有的状态」（尚未指定 / 全部 / 全局）时才需要这样拆；
    /// 若 NULL 只是"这条记录碰巧没填"，多行 NULL 本来就合法，不该纳入唯一约束。
    /// </remarks>
    /// <param name="columnName">列名</param>
    /// <param name="provider">数据库提供者类型</param>
    /// <returns>HasFilter 可用的 SQL 字符串</returns>
    public static string GetColumnNull(string columnName, DatabaseProvider provider)
    {
        return $"{QuoteIdentifier(columnName, provider)} IS NULL";
    }

    /// <summary>
    /// 获取 "columnName IS NULL" 的过滤 SQL（自动检测数据库提供者）
    /// </summary>
    /// <param name="columnName">列名</param>
    /// <returns>HasFilter 可用的 SQL 字符串</returns>
    public static string GetColumnNull(string columnName)
    {
        var provider = EntityConfigurationContext.GetCurrentDatabaseProviderOrDefault();
        return GetColumnNull(columnName, provider);
    }

    /// <summary>
    /// 获取 "columnName IS NOT NULL AND IsDeleted = false" 的过滤 SQL，
    /// 用于可空列的唯一索引同时排除软删除行
    /// </summary>
    /// <param name="columnName">列名</param>
    /// <param name="provider">数据库提供者类型</param>
    /// <returns>HasFilter 可用的 SQL 字符串</returns>
    public static string GetColumnNotNullAndIsDeletedFalse(string columnName, DatabaseProvider provider)
    {
        return $"{QuoteIdentifier(columnName, provider)} IS NOT NULL AND {GetIsDeletedFalse(provider)}";
    }

    /// <summary>
    /// 获取 "columnName IS NOT NULL AND IsDeleted = false" 的过滤 SQL（自动检测数据库提供者）
    /// </summary>
    /// <param name="columnName">列名</param>
    /// <returns>HasFilter 可用的 SQL 字符串</returns>
    public static string GetColumnNotNullAndIsDeletedFalse(string columnName)
    {
        var provider = EntityConfigurationContext.GetCurrentDatabaseProviderOrDefault();
        return GetColumnNotNullAndIsDeletedFalse(columnName, provider);
    }

    /// <summary>
    /// 获取 "columnName IS NOT NULL AND IsDeleted = false" 的过滤 SQL，允许同时自定义业务列名与 IsDeleted 列名。
    /// </summary>
    /// <param name="columnName">可空业务列名（实际数据库列名）</param>
    /// <param name="isDeletedColumn">IsDeleted 属性对应的实际数据库列名</param>
    /// <param name="provider">数据库提供者类型</param>
    /// <returns>HasFilter 可用的 SQL 字符串</returns>
    public static string GetColumnNotNullAndIsDeletedFalse(string columnName, string isDeletedColumn, DatabaseProvider provider)
    {
        Check.NotNullOrWhiteSpace(columnName);
        Check.NotNullOrWhiteSpace(isDeletedColumn);
        return $"{QuoteIdentifier(columnName, provider)} IS NOT NULL AND {BuildIsFalseExpression(isDeletedColumn, provider)}";
    }

    /// <summary>
    /// 获取 "columnName = value AND IsDeleted = false" 的过滤 SQL，
    /// 用于按枚举/状态列限定的部分唯一索引（如"每科目至多一张草稿"）同时排除软删除行
    /// </summary>
    /// <param name="columnName">列名</param>
    /// <param name="value">整数常量值（枚举底层值）</param>
    /// <param name="provider">数据库提供者类型</param>
    /// <returns>HasFilter 可用的 SQL 字符串</returns>
    public static string GetColumnEqualsAndIsDeletedFalse(string columnName, int value, DatabaseProvider provider)
    {
        Check.NotNullOrWhiteSpace(columnName);
        return $"{QuoteIdentifier(columnName, provider)} = {value} AND {GetIsDeletedFalse(provider)}";
    }

    /// <summary>
    /// 获取 "columnName = value AND IsDeleted = false" 的过滤 SQL（自动检测数据库提供者）
    /// </summary>
    /// <param name="columnName">列名</param>
    /// <param name="value">整数常量值（枚举底层值）</param>
    /// <returns>HasFilter 可用的 SQL 字符串</returns>
    public static string GetColumnEqualsAndIsDeletedFalse(string columnName, int value)
    {
        var provider = EntityConfigurationContext.GetCurrentDatabaseProviderOrDefault();
        return GetColumnEqualsAndIsDeletedFalse(columnName, value, provider);
    }

    /// <summary>
    /// 获取 "columnName &lt;&gt; value" 的过滤 SQL，
    /// 用于**排除某一状态**的部分唯一索引（如"同一期次至多成功一次，但失败可以重试"）
    /// </summary>
    /// <remarks>
    /// 不带 IsDeleted 条件：无软删除的记录表（<c>CreationAuditedEntity</c> 之类）用它。
    /// 需要同时排除软删除行时另用 <see cref="GetColumnEqualsAndIsDeletedFalse(string, int)"/> 一族。
    /// </remarks>
    /// <param name="columnName">列名</param>
    /// <param name="value">整数常量值（枚举底层值）</param>
    /// <param name="provider">数据库提供者类型</param>
    /// <returns>HasFilter 可用的 SQL 字符串</returns>
    public static string GetColumnNotEquals(string columnName, int value, DatabaseProvider provider)
    {
        Check.NotNullOrWhiteSpace(columnName);
        return $"{QuoteIdentifier(columnName, provider)} <> {value}";
    }

    /// <summary>
    /// 获取 "columnName &lt;&gt; value" 的过滤 SQL（自动检测数据库提供者）
    /// </summary>
    /// <param name="columnName">列名</param>
    /// <param name="value">整数常量值（枚举底层值）</param>
    /// <returns>HasFilter 可用的 SQL 字符串</returns>
    public static string GetColumnNotEquals(string columnName, int value)
    {
        var provider = EntityConfigurationContext.GetCurrentDatabaseProviderOrDefault();
        return GetColumnNotEquals(columnName, value, provider);
    }

    /// <summary>
    /// 获取 "columnName = true AND IsDeleted = false" 的过滤 SQL，
    /// 用于按布尔标志列限定的部分唯一索引（如"每往来方至多一个默认账户"）同时排除软删除行
    /// </summary>
    /// <param name="columnName">布尔列名</param>
    /// <param name="provider">数据库提供者类型</param>
    /// <returns>HasFilter 可用的 SQL 字符串</returns>
    public static string GetColumnTrueAndIsDeletedFalse(string columnName, DatabaseProvider provider)
    {
        Check.NotNullOrWhiteSpace(columnName);
        return $"{BuildIsTrueExpression(columnName, provider)} AND {GetIsDeletedFalse(provider)}";
    }

    /// <summary>
    /// 获取 "columnName = true AND IsDeleted = false" 的过滤 SQL（自动检测数据库提供者）
    /// </summary>
    /// <param name="columnName">布尔列名</param>
    /// <returns>HasFilter 可用的 SQL 字符串</returns>
    public static string GetColumnTrueAndIsDeletedFalse(string columnName)
    {
        var provider = EntityConfigurationContext.GetCurrentDatabaseProviderOrDefault();
        return GetColumnTrueAndIsDeletedFalse(columnName, provider);
    }

    /// <summary>
    /// 检查是否支持指定的数据库提供者
    /// </summary>
    public static bool IsSupported(DatabaseProvider provider) => _filters.ContainsKey(provider);

    /// <summary>
    /// 获取所有支持的数据库提供者
    /// </summary>
    public static IReadOnlyCollection<DatabaseProvider> GetSupportedProviders() => _filters.Keys;
}