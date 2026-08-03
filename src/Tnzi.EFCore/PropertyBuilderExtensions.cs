
namespace Tnzi.EFCore;

/// <summary>
/// PropertyBuilder 扩展方法，提供数据库无关的默认值配置
/// </summary>
/// <remarks>
/// 此扩展类提供了一系列便捷方法，用于在实体配置中设置数据库级别的默认值。
/// <para>
/// <strong>重要：框架不推荐在实体配置中使用数据库级别的 SQL 函数默认值。</strong>
/// </para>
/// <para>
/// <strong>推荐做法：</strong>
/// <list type="bullet">
/// <item><description>时间戳字段：由框架的审计系统（TnziDbContextHelper.ApplyAuditProperties）自动设置</description></item>
/// <item><description>业务字段：在应用代码中显式设置（如服务层创建实体时设置）</description></item>
/// <item><description>常量默认值：使用 HasDefaultValue() 设置常量值（数字、字符串、布尔值），这些是安全的</description></item>
/// </list>
/// </para>
/// <para>
/// <strong>何时使用数据库级别 SQL 函数默认值：</strong>
/// <list type="bullet">
/// <item><description>仅在确实需要支持直接 SQL 插入（绕过 EF Core）的场景</description></item>
/// <item><description>数据迁移脚本或批量导入场景</description></item>
/// <item><description>框架内置模块应避免使用，以保持跨数据库兼容性</description></item>
/// </list>
/// </para>
/// <para>
/// 如果确实需要使用，示例（自动检测数据库提供者）：
/// <code>
/// public class UserConfiguration : EntityTypeConfigurationBase&lt;User, Guid&gt;
/// {
///     public override void Configure(EntityTypeBuilder&lt;User&gt; builder)
///     {
///         // 仅在确实需要支持直接 SQL 插入时使用
///         builder.Property(e => e.CreatedAt)
///             .IsRequired()
///             .HasDefaultCurrentUtcTime();
///     }
/// }
/// </code>
/// </para>
/// <para>
/// 如果确实需要使用，示例（显式指定数据库提供者）：
/// <code>
/// builder.Property(e => e.CreatedAt)
///     .IsRequired()
///     .HasDefaultCurrentUtcTime(DatabaseProvider.PostgreSQL);
/// </code>
/// </para>
/// <para>
/// 请勿在实体配置中硬编码数据库特定的 SQL 函数（如 GETUTCDATE()、NOW() 等），
/// 如果确实需要使用数据库级别默认值，应始终使用这些扩展方法或 SqlFunctionFactory 来确保跨数据库兼容性。
/// </para>
/// </remarks>
public static class PropertyBuilderExtensions
{
    #region 日期时间默认值

    /// <summary>
    /// 设置属性默认值为当前 UTC 时间
    /// </summary>
    /// <typeparam name="TProperty">属性类型</typeparam>
    /// <param name="builder">属性构建器</param>
    /// <param name="provider">数据库提供者类型</param>
    /// <returns>属性构建器（用于链式调用）</returns>
    /// <remarks>
    /// SQL Server: GETUTCDATE()
    /// PostgreSQL: (NOW() AT TIME ZONE 'UTC')
    /// MySQL: UTC_TIMESTAMP()
    /// SQLite: DATETIME('now')
    /// </remarks>
    public static PropertyBuilder<TProperty> HasDefaultCurrentUtcTime<TProperty>(
        this PropertyBuilder<TProperty> builder,
        DatabaseProvider provider)
    {
        return builder.HasDefaultValueSql(SqlFunctionFactory.CurrentUtcTime(provider));
    }

    /// <summary>
    /// 设置属性默认值为当前 UTC 时间（自动检测数据库提供者）
    /// </summary>
    /// <typeparam name="TProperty">属性类型</typeparam>
    /// <param name="builder">属性构建器</param>
    /// <returns>属性构建器（用于链式调用）</returns>
    /// <remarks>
    /// 此方法自动从 EntityTypeConfigurationBase 的配置上下文中获取数据库提供者，
    /// 无需显式传递 provider 参数。只能在 EntityTypeConfigurationBase.Configure 方法内部调用。
    /// </remarks>
    public static PropertyBuilder<TProperty> HasDefaultCurrentUtcTime<TProperty>(
        this PropertyBuilder<TProperty> builder)
    {
        var provider = EntityConfigurationContext.GetCurrentDatabaseProviderOrDefault();
        return builder.HasDefaultCurrentUtcTime(provider);
    }

    /// <summary>
    /// 设置属性默认值为当前本地时间
    /// </summary>
    /// <typeparam name="TProperty">属性类型</typeparam>
    /// <param name="builder">属性构建器</param>
    /// <param name="provider">数据库提供者类型</param>
    /// <returns>属性构建器（用于链式调用）</returns>
    /// <remarks>
    /// SQL Server: GETDATE()
    /// PostgreSQL: NOW()
    /// MySQL: NOW()
    /// SQLite: DATETIME('now', 'localtime')
    /// </remarks>
    public static PropertyBuilder<TProperty> HasDefaultCurrentLocalTime<TProperty>(
        this PropertyBuilder<TProperty> builder,
        DatabaseProvider provider)
    {
        return builder.HasDefaultValueSql(SqlFunctionFactory.CurrentLocalTime(provider));
    }

    /// <summary>
    /// 设置属性默认值为当前本地时间（自动检测数据库提供者）
    /// </summary>
    /// <typeparam name="TProperty">属性类型</typeparam>
    /// <param name="builder">属性构建器</param>
    /// <returns>属性构建器（用于链式调用）</returns>
    /// <remarks>
    /// 此方法自动从 EntityTypeConfigurationBase 的配置上下文中获取数据库提供者，
    /// 无需显式传递 provider 参数。只能在 EntityTypeConfigurationBase.Configure 方法内部调用。
    /// </remarks>
    public static PropertyBuilder<TProperty> HasDefaultCurrentLocalTime<TProperty>(
        this PropertyBuilder<TProperty> builder)
    {
        var provider = EntityConfigurationContext.GetCurrentDatabaseProviderOrDefault();
        return builder.HasDefaultCurrentLocalTime(provider);
    }

    /// <summary>
    /// 设置属性默认值为当前 UTC 日期（不含时间部分）
    /// </summary>
    /// <typeparam name="TProperty">属性类型</typeparam>
    /// <param name="builder">属性构建器</param>
    /// <param name="provider">数据库提供者类型</param>
    /// <returns>属性构建器（用于链式调用）</returns>
    /// <remarks>
    /// SQL Server: CAST(GETUTCDATE() AS DATE)
    /// PostgreSQL: (CURRENT_DATE AT TIME ZONE 'UTC')::DATE
    /// MySQL: UTC_DATE()
    /// SQLite: DATE('now')
    /// </remarks>
    public static PropertyBuilder<TProperty> HasDefaultCurrentUtcDate<TProperty>(
        this PropertyBuilder<TProperty> builder,
        DatabaseProvider provider)
    {
        return builder.HasDefaultValueSql(SqlFunctionFactory.CurrentUtcDate(provider));
    }

    /// <summary>
    /// 设置属性默认值为当前 UTC 日期（不含时间部分，自动检测数据库提供者）
    /// </summary>
    /// <typeparam name="TProperty">属性类型</typeparam>
    /// <param name="builder">属性构建器</param>
    /// <returns>属性构建器（用于链式调用）</returns>
    /// <remarks>
    /// 此方法自动从 EntityTypeConfigurationBase 的配置上下文中获取数据库提供者，
    /// 无需显式传递 provider 参数。只能在 EntityTypeConfigurationBase.Configure 方法内部调用。
    /// </remarks>
    public static PropertyBuilder<TProperty> HasDefaultCurrentUtcDate<TProperty>(
        this PropertyBuilder<TProperty> builder)
    {
        var provider = EntityConfigurationContext.GetCurrentDatabaseProviderOrDefault();
        return builder.HasDefaultCurrentUtcDate(provider);
    }

    /// <summary>
    /// 设置属性默认值为当前本地日期（不含时间部分）
    /// </summary>
    /// <typeparam name="TProperty">属性类型</typeparam>
    /// <param name="builder">属性构建器</param>
    /// <param name="provider">数据库提供者类型</param>
    /// <returns>属性构建器（用于链式调用）</returns>
    /// <remarks>
    /// SQL Server: CAST(GETDATE() AS DATE)
    /// PostgreSQL: CURRENT_DATE
    /// MySQL: CURDATE()
    /// SQLite: DATE('now', 'localtime')
    /// </remarks>
    public static PropertyBuilder<TProperty> HasDefaultCurrentLocalDate<TProperty>(
        this PropertyBuilder<TProperty> builder,
        DatabaseProvider provider)
    {
        return builder.HasDefaultValueSql(SqlFunctionFactory.CurrentLocalDate(provider));
    }

    /// <summary>
    /// 设置属性默认值为当前本地日期（不含时间部分，自动检测数据库提供者）
    /// </summary>
    /// <typeparam name="TProperty">属性类型</typeparam>
    /// <param name="builder">属性构建器</param>
    /// <returns>属性构建器（用于链式调用）</returns>
    /// <remarks>
    /// 此方法自动从 EntityTypeConfigurationBase 的配置上下文中获取数据库提供者，
    /// 无需显式传递 provider 参数。只能在 EntityTypeConfigurationBase.Configure 方法内部调用。
    /// </remarks>
    public static PropertyBuilder<TProperty> HasDefaultCurrentLocalDate<TProperty>(
        this PropertyBuilder<TProperty> builder)
    {
        var provider = EntityConfigurationContext.GetCurrentDatabaseProviderOrDefault();
        return builder.HasDefaultCurrentLocalDate(provider);
    }

    /// <summary>
    /// 设置属性默认值为当前时间戳（高精度）
    /// </summary>
    /// <typeparam name="TProperty">属性类型</typeparam>
    /// <param name="builder">属性构建器</param>
    /// <param name="provider">数据库提供者类型</param>
    /// <returns>属性构建器（用于链式调用）</returns>
    /// <remarks>
    /// SQL Server: SYSDATETIME()
    /// PostgreSQL: CURRENT_TIMESTAMP
    /// MySQL: CURRENT_TIMESTAMP(6)
    /// SQLite: STRFTIME('%Y-%m-%d %H:%M:%f', 'now')
    /// </remarks>
    public static PropertyBuilder<TProperty> HasDefaultCurrentTimestamp<TProperty>(
        this PropertyBuilder<TProperty> builder,
        DatabaseProvider provider)
    {
        return builder.HasDefaultValueSql(SqlFunctionFactory.CurrentTimestamp(provider));
    }

    /// <summary>
    /// 设置属性默认值为当前时间戳（高精度，自动检测数据库提供者）
    /// </summary>
    /// <typeparam name="TProperty">属性类型</typeparam>
    /// <param name="builder">属性构建器</param>
    /// <returns>属性构建器（用于链式调用）</returns>
    /// <remarks>
    /// 此方法自动从 EntityTypeConfigurationBase 的配置上下文中获取数据库提供者，
    /// 无需显式传递 provider 参数。只能在 EntityTypeConfigurationBase.Configure 方法内部调用。
    /// </remarks>
    public static PropertyBuilder<TProperty> HasDefaultCurrentTimestamp<TProperty>(
        this PropertyBuilder<TProperty> builder)
    {
        var provider = EntityConfigurationContext.GetCurrentDatabaseProviderOrDefault();
        return builder.HasDefaultCurrentTimestamp(provider);
    }

    #endregion

    #region GUID/UUID 默认值

    /// <summary>
    /// 设置属性默认值为新生成的 GUID
    /// </summary>
    /// <typeparam name="TProperty">属性类型</typeparam>
    /// <param name="builder">属性构建器</param>
    /// <param name="provider">数据库提供者类型</param>
    /// <returns>属性构建器（用于链式调用）</returns>
    /// <remarks>
    /// SQL Server: NEWID()
    /// PostgreSQL: gen_random_uuid()
    /// MySQL: UUID()
    /// SQLite: (复杂表达式生成 UUID v4)
    /// </remarks>
    public static PropertyBuilder<TProperty> HasDefaultNewGuid<TProperty>(
        this PropertyBuilder<TProperty> builder,
        DatabaseProvider provider)
    {
        return builder.HasDefaultValueSql(SqlFunctionFactory.NewGuid(provider));
    }

    /// <summary>
    /// 设置属性默认值为新生成的 GUID（自动检测数据库提供者）
    /// </summary>
    /// <typeparam name="TProperty">属性类型</typeparam>
    /// <param name="builder">属性构建器</param>
    /// <returns>属性构建器（用于链式调用）</returns>
    /// <remarks>
    /// 此方法自动从 EntityTypeConfigurationBase 的配置上下文中获取数据库提供者，
    /// 无需显式传递 provider 参数。只能在 EntityTypeConfigurationBase.Configure 方法内部调用。
    /// </remarks>
    public static PropertyBuilder<TProperty> HasDefaultNewGuid<TProperty>(
        this PropertyBuilder<TProperty> builder)
    {
        var provider = EntityConfigurationContext.GetCurrentDatabaseProviderOrDefault();
        return builder.HasDefaultNewGuid(provider);
    }

    /// <summary>
    /// 设置属性默认值为新生成的顺序 GUID（适合作为聚集索引主键）
    /// </summary>
    /// <typeparam name="TProperty">属性类型</typeparam>
    /// <param name="builder">属性构建器</param>
    /// <param name="provider">数据库提供者类型</param>
    /// <returns>属性构建器（用于链式调用）</returns>
    /// <remarks>
    /// SQL Server: NEWSEQUENTIALID() (只能用于 DEFAULT 约束)
    /// PostgreSQL: uuid_generate_v1() (需要 uuid-ossp 扩展)
    /// MySQL: UUID() (无原生顺序 UUID)
    /// SQLite: (同 NewGuid，无原生支持)
    /// </remarks>
    public static PropertyBuilder<TProperty> HasDefaultNewSequentialGuid<TProperty>(
        this PropertyBuilder<TProperty> builder,
        DatabaseProvider provider)
    {
        return builder.HasDefaultValueSql(SqlFunctionFactory.NewSequentialGuid(provider));
    }

    #endregion

    #region 布尔值默认值

    /// <summary>
    /// 设置属性默认值为 True
    /// </summary>
    /// <typeparam name="TProperty">属性类型</typeparam>
    /// <param name="builder">属性构建器</param>
    /// <param name="provider">数据库提供者类型</param>
    /// <returns>属性构建器（用于链式调用）</returns>
    /// <remarks>
    /// SQL Server: 1
    /// PostgreSQL: TRUE
    /// MySQL: TRUE
    /// SQLite: 1
    /// </remarks>
    public static PropertyBuilder<TProperty> HasDefaultTrue<TProperty>(
        this PropertyBuilder<TProperty> builder,
        DatabaseProvider provider)
    {
        return builder.HasDefaultValueSql(SqlFunctionFactory.True(provider));
    }

    /// <summary>
    /// 设置属性默认值为 True（自动检测数据库提供者）
    /// </summary>
    /// <typeparam name="TProperty">属性类型</typeparam>
    /// <param name="builder">属性构建器</param>
    /// <returns>属性构建器（用于链式调用）</returns>
    /// <remarks>
    /// 此方法自动从 EntityTypeConfigurationBase 的配置上下文中获取数据库提供者，
    /// 无需显式传递 provider 参数。只能在 EntityTypeConfigurationBase.Configure 方法内部调用。
    /// </remarks>
    public static PropertyBuilder<TProperty> HasDefaultTrue<TProperty>(
        this PropertyBuilder<TProperty> builder)
    {
        var provider = EntityConfigurationContext.GetCurrentDatabaseProviderOrDefault();
        return builder.HasDefaultTrue(provider);
    }

    /// <summary>
    /// 设置属性默认值为 False
    /// </summary>
    /// <typeparam name="TProperty">属性类型</typeparam>
    /// <param name="builder">属性构建器</param>
    /// <param name="provider">数据库提供者类型</param>
    /// <returns>属性构建器（用于链式调用）</returns>
    /// <remarks>
    /// SQL Server: 0
    /// PostgreSQL: FALSE
    /// MySQL: FALSE
    /// SQLite: 0
    /// </remarks>
    public static PropertyBuilder<TProperty> HasDefaultFalse<TProperty>(
        this PropertyBuilder<TProperty> builder,
        DatabaseProvider provider)
    {
        return builder.HasDefaultValueSql(SqlFunctionFactory.False(provider));
    }

    /// <summary>
    /// 设置属性默认值为 False（自动检测数据库提供者）
    /// </summary>
    /// <typeparam name="TProperty">属性类型</typeparam>
    /// <param name="builder">属性构建器</param>
    /// <returns>属性构建器（用于链式调用）</returns>
    /// <remarks>
    /// 此方法自动从 EntityTypeConfigurationBase 的配置上下文中获取数据库提供者，
    /// 无需显式传递 provider 参数。只能在 EntityTypeConfigurationBase.Configure 方法内部调用。
    /// </remarks>
    public static PropertyBuilder<TProperty> HasDefaultFalse<TProperty>(
        this PropertyBuilder<TProperty> builder)
    {
        var provider = EntityConfigurationContext.GetCurrentDatabaseProviderOrDefault();
        return builder.HasDefaultFalse(provider);
    }

    #endregion

    #region Decimal 精度约定

    /// <summary>
    /// 金额字段精度约定：decimal(19, 4)
    /// </summary>
    /// <remarks>
    /// 框架统一的货币金额存储精度。19 位总长度可覆盖万亿级金额，4 位小数满足
    /// 多币种最小货币单位与中间计算精度要求（展示时按币种舍入到展示精度）。
    /// 所有货币金额字段（价格、税额、余额、借贷金额等）MUST 使用此约定，
    /// 避免各数据库提供者默认精度不一致导致的静默截断。
    /// </remarks>
    public static PropertyBuilder<decimal> HasMoneyPrecision(this PropertyBuilder<decimal> builder)
        => builder.HasPrecision(19, 4);

    /// <inheritdoc cref="HasMoneyPrecision(PropertyBuilder{decimal})"/>
    public static PropertyBuilder<decimal?> HasMoneyPrecision(this PropertyBuilder<decimal?> builder)
        => builder.HasPrecision(19, 4);

    /// <summary>
    /// 汇率字段精度约定：decimal(18, 8)
    /// </summary>
    /// <remarks>
    /// 8 位小数覆盖绝大多数货币对的报价精度（含日元/越南盾等小面值货币的反向汇率）。
    /// </remarks>
    public static PropertyBuilder<decimal> HasExchangeRatePrecision(this PropertyBuilder<decimal> builder)
        => builder.HasPrecision(18, 8);

    /// <inheritdoc cref="HasExchangeRatePrecision(PropertyBuilder{decimal})"/>
    public static PropertyBuilder<decimal?> HasExchangeRatePrecision(this PropertyBuilder<decimal?> builder)
        => builder.HasPrecision(18, 8);

    /// <summary>
    /// 数量字段精度约定：decimal(18, 4)
    /// </summary>
    /// <remarks>
    /// 用于单据行数量、工时等非货币计量字段。
    /// </remarks>
    public static PropertyBuilder<decimal> HasQuantityPrecision(this PropertyBuilder<decimal> builder)
        => builder.HasPrecision(18, 4);

    /// <inheritdoc cref="HasQuantityPrecision(PropertyBuilder{decimal})"/>
    public static PropertyBuilder<decimal?> HasQuantityPrecision(this PropertyBuilder<decimal?> builder)
        => builder.HasPrecision(18, 4);

    /// <summary>
    /// 比率/百分比字段精度约定：decimal(9, 6)
    /// </summary>
    /// <remarks>
    /// 用于税率、折扣率等比率字段（无论存储为百分数 8.875 还是小数 0.088750 均可覆盖）。
    /// </remarks>
    public static PropertyBuilder<decimal> HasRatePrecision(this PropertyBuilder<decimal> builder)
        => builder.HasPrecision(9, 6);

    /// <inheritdoc cref="HasRatePrecision(PropertyBuilder{decimal})"/>
    public static PropertyBuilder<decimal?> HasRatePrecision(this PropertyBuilder<decimal?> builder)
        => builder.HasPrecision(9, 6);

    #endregion

    #region 数学函数默认值

    /// <summary>
    /// 设置属性默认值为随机数（0 到 1 之间）
    /// </summary>
    /// <typeparam name="TProperty">属性类型</typeparam>
    /// <param name="builder">属性构建器</param>
    /// <param name="provider">数据库提供者类型</param>
    /// <returns>属性构建器（用于链式调用）</returns>
    /// <remarks>
    /// SQL Server: RAND()
    /// PostgreSQL: RANDOM()
    /// MySQL: RAND()
    /// SQLite: (ABS(RANDOM()) / CAST(9223372036854775807 AS REAL))
    /// </remarks>
    public static PropertyBuilder<TProperty> HasDefaultRandom<TProperty>(
        this PropertyBuilder<TProperty> builder,
        DatabaseProvider provider)
    {
        return builder.HasDefaultValueSql(SqlFunctionFactory.Random(provider));
    }

    /// <summary>
    /// 设置属性默认值为随机数（0 到 1 之间，自动检测数据库提供者）
    /// </summary>
    /// <typeparam name="TProperty">属性类型</typeparam>
    /// <param name="builder">属性构建器</param>
    /// <returns>属性构建器（用于链式调用）</returns>
    /// <remarks>
    /// 此方法自动从 EntityTypeConfigurationBase 的配置上下文中获取数据库提供者，
    /// 无需显式传递 provider 参数。只能在 EntityTypeConfigurationBase.Configure 方法内部调用。
    /// </remarks>
    public static PropertyBuilder<TProperty> HasDefaultRandom<TProperty>(
        this PropertyBuilder<TProperty> builder)
    {
        var provider = EntityConfigurationContext.GetCurrentDatabaseProviderOrDefault();
        return builder.HasDefaultRandom(provider);
    }

    #endregion
}