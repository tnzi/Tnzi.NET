
namespace Tnzi.EFCore.Dapper.Providers;

/// <summary>
/// Dapper 数据库提供者工厂
/// 用于创建 Dapper 专用的数据库提供者实例
/// </summary>
public class DapperDatabaseProviderFactory
{
    private static readonly ConcurrentDictionary<string, IDatabaseProvider> _providerCache = new();

    /// <summary>
    /// 根据数据库提供者枚举创建提供者实例
    /// </summary>
    public static IDatabaseProvider Create(DatabaseProvider provider)
    {
        var key = provider.ToString();
        if (_providerCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        IDatabaseProvider instance = provider switch
        {
            DatabaseProvider.SqlServer => new SqlServerProvider(),
            DatabaseProvider.PostgreSQL => new PostgreSQLProvider(),
            DatabaseProvider.MySql => new MySqlProvider(),
            DatabaseProvider.Sqlite => throw new NotSupportedException("SQLite is not yet supported for Dapper operations"),
            _ => new SqlServerProvider() // 默认使用 SQL Server
        };

        return _providerCache.GetOrAdd(key, instance);
    }

    /// <summary>
    /// 根据连接字符串自动识别数据库类型并创建提供者实例
    /// 使用统一的 DatabaseProviderDetector 进行检测
    /// </summary>
    public static IDatabaseProvider CreateFromConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new SqlServerProvider(); // 默认
        }

        // 使用统一的检测器
        if (DatabaseProviderDetector.TryDetectFromConnectionString(connectionString, out var provider, out _))
        {
            // 如果检测到 SQLite，直接抛出（Dapper 不支持 SQLite）
            return Create(provider);
        }

        // 如果检测失败，默认使用 SQL Server
        // 注意：在配置验证阶段，检测失败会抛出异常，但这里为了兼容性，返回默认值
        return Create(DatabaseProvider.SqlServer);
    }

    /// <summary>
    /// 从 DbContext 获取数据库提供者
    /// </summary>
    /// <remarks>
    /// ★★ 先看上下文<b>实际</b>用的 EF provider（<c>Database.ProviderName</c>），配置与连接字符串只作回退。
    /// 配置里省略 <c>Provider</c> 时绑定出来的是默认值 SqlServer：启动期的自动识别只写在注册路径自己那份
    /// 绑定实例上，这里重新绑定拿不到，PostgreSQL / MySQL 应用的批量写入会生成 SQL Server 方言当场报错。
    /// SQLite 没有 Dapper 方言，仍走回退路径：普通查询不用方言，不能因此在构造时失败。
    /// </remarks>
    public static IDatabaseProvider CreateFromDbContext(DbContext dbContext, IConfiguration? configuration = null)
    {
        Check.NotNull(dbContext);

        // 方法0：上下文实际使用的 provider（精确，不需要猜）
        var actualProvider = DatabaseProviderDetector.ConvertProviderNameToDatabaseProvider(dbContext.Database.ProviderName);
        if (actualProvider is DatabaseProvider.SqlServer or DatabaseProvider.PostgreSQL or DatabaseProvider.MySql)
        {
            return Create(actualProvider.Value);
        }

        // 方法1：从配置中获取（如果可用）
        if (configuration != null)
        {
            var options = configuration.GetSection("Database").Get<DatabaseOptions>();
            if (options?.DbContexts != null)
            {
                var dbContextType = dbContext.GetType();
                var config = options.DbContexts.FirstOrDefault(c =>
                {
                    var configType = c.GetDbContextType();
                    return configType != null && configType == dbContextType;
                });

                if (config != null)
                {
                    return Create(config.Provider);
                }
            }
        }

        // 方法2：从连接字符串识别
        var connectionString = dbContext.Database.GetConnectionString();
        return CreateFromConnectionString(connectionString);
    }
}