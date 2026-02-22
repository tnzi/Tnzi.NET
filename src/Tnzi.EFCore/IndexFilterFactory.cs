
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
/// 重要：请勿在实体配置中硬编码 HasFilter 的 SQL（如 \"IsDeleted\" = false），
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
    /// 检查是否支持指定的数据库提供者
    /// </summary>
    public static bool IsSupported(DatabaseProvider provider) => _filters.ContainsKey(provider);

    /// <summary>
    /// 获取所有支持的数据库提供者
    /// </summary>
    public static IReadOnlyCollection<DatabaseProvider> GetSupportedProviders() => _filters.Keys;
}