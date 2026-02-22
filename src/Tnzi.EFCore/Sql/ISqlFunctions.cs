namespace Tnzi.EFCore.Sql;

/// <summary>
/// SQL 函数提供者接口，用于抽象不同数据库的 SQL 函数差异
/// </summary>
/// <remarks>
/// 不同数据库（SQL Server、PostgreSQL、MySQL、SQLite）对于相同功能的 SQL 函数有不同的语法。
/// 此接口提供统一的抽象，使实体配置可以编写数据库无关的代码。
/// <para>
/// 使用示例：
/// <code>
/// // 通过工厂获取函数提供者
/// var sql = SqlFunctionFactory.GetFunctions(DatabaseProvider.PostgreSQL);
/// builder.Property(e => e.CreatedAt).HasDefaultValueSql(sql.CurrentUtcTime);
/// 
/// // 或使用扩展方法（推荐）
/// builder.Property(e => e.CreatedAt).HasDefaultCurrentUtcTime(DatabaseProvider.PostgreSQL);
/// </code>
/// </para>
/// <para>
/// 重要：请勿在实体配置中硬编码数据库特定的 SQL 函数（如 GETUTCDATE()、NOW() 等），
/// 应始终使用此接口或 PropertyBuilder 扩展方法来确保跨数据库兼容性。
/// </para>
/// </remarks>
public interface ISqlFunctions
{
    /// <summary>
    /// 数据库提供者类型
    /// </summary>
    DatabaseProvider Provider { get; }

    #region 日期时间函数

    /// <summary>
    /// 当前 UTC 时间
    /// </summary>
    /// <remarks>
    /// SQL Server: GETUTCDATE()
    /// PostgreSQL: (NOW() AT TIME ZONE 'UTC')
    /// MySQL: UTC_TIMESTAMP()
    /// SQLite: DATETIME('now')
    /// </remarks>
    string CurrentUtcTime { get; }

    /// <summary>
    /// 当前本地时间
    /// </summary>
    /// <remarks>
    /// SQL Server: GETDATE()
    /// PostgreSQL: NOW()
    /// MySQL: NOW()
    /// SQLite: DATETIME('now', 'localtime')
    /// </remarks>
    string CurrentLocalTime { get; }

    /// <summary>
    /// 当前 UTC 日期（不含时间部分）
    /// </summary>
    /// <remarks>
    /// SQL Server: CAST(GETUTCDATE() AS DATE)
    /// PostgreSQL: (CURRENT_DATE AT TIME ZONE 'UTC')
    /// MySQL: UTC_DATE()
    /// SQLite: DATE('now')
    /// </remarks>
    string CurrentUtcDate { get; }

    /// <summary>
    /// 当前本地日期（不含时间部分）
    /// </summary>
    /// <remarks>
    /// SQL Server: CAST(GETDATE() AS DATE)
    /// PostgreSQL: CURRENT_DATE
    /// MySQL: CURDATE()
    /// SQLite: DATE('now', 'localtime')
    /// </remarks>
    string CurrentLocalDate { get; }

    /// <summary>
    /// 当前时间戳（高精度）
    /// </summary>
    /// <remarks>
    /// SQL Server: SYSDATETIME()
    /// PostgreSQL: CURRENT_TIMESTAMP
    /// MySQL: CURRENT_TIMESTAMP(6)
    /// SQLite: STRFTIME('%Y-%m-%d %H:%M:%f', 'now')
    /// </remarks>
    string CurrentTimestamp { get; }

    #endregion

    #region GUID/UUID 函数

    /// <summary>
    /// 生成新的随机 GUID/UUID
    /// </summary>
    /// <remarks>
    /// SQL Server: NEWID()
    /// PostgreSQL: gen_random_uuid()
    /// MySQL: UUID()
    /// SQLite: (复杂表达式生成 UUID v4)
    /// </remarks>
    string NewGuid { get; }

    /// <summary>
    /// 生成顺序 GUID（适合作为聚集索引主键）
    /// </summary>
    /// <remarks>
    /// SQL Server: NEWSEQUENTIALID()
    /// PostgreSQL: uuid_generate_v1() (需要 uuid-ossp 扩展)
    /// MySQL: UUID() (MySQL 无原生顺序 UUID)
    /// SQLite: (同 NewGuid，SQLite 无原生支持)
    /// 
    /// 注意：NEWSEQUENTIALID() 只能用于 DEFAULT 约束，不能在 INSERT 语句中直接调用。
    /// PostgreSQL 的 uuid_generate_v1() 需要先执行 CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
    /// </remarks>
    string NewSequentialGuid { get; }

    #endregion

    #region 布尔值常量

    /// <summary>
    /// 布尔值 True 的 SQL 表示
    /// </summary>
    /// <remarks>
    /// SQL Server: 1 (SQL Server 使用 BIT 类型)
    /// PostgreSQL: TRUE
    /// MySQL: TRUE 或 1
    /// SQLite: 1
    /// </remarks>
    string True { get; }

    /// <summary>
    /// 布尔值 False 的 SQL 表示
    /// </summary>
    /// <remarks>
    /// SQL Server: 0 (SQL Server 使用 BIT 类型)
    /// PostgreSQL: FALSE
    /// MySQL: FALSE 或 0
    /// SQLite: 0
    /// </remarks>
    string False { get; }

    #endregion

    #region 数学函数

    /// <summary>
    /// 随机数函数（返回 0 到 1 之间的浮点数）
    /// </summary>
    /// <remarks>
    /// SQL Server: RAND()
    /// PostgreSQL: RANDOM() (返回 -1 到 1，需要转换)
    /// MySQL: RAND()
    /// SQLite: (ABS(RANDOM()) / CAST(9223372036854775807 AS REAL))
    /// </remarks>
    string Random { get; }

    #endregion
}
