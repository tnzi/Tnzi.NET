
namespace Tnzi.EFCore.Providers;

/// <summary>
/// 数据库提供者工厂
/// 根据 DatabaseProvider 枚举值配置 DbContextOptionsBuilder
/// </summary>
public static class DatabaseProviderFactory
{
    private static readonly ConcurrentDictionary<DatabaseProvider, IDatabaseProviderConfigurator> _configurators;
    
    static DatabaseProviderFactory()
    {
        _configurators = new ConcurrentDictionary<DatabaseProvider, IDatabaseProviderConfigurator>
        {
            [DatabaseProvider.SqlServer] = new SqlServerConfigurator(),
            [DatabaseProvider.PostgreSQL] = new PostgreSqlConfigurator(),
            [DatabaseProvider.MySql] = new MySqlConfigurator(),
            [DatabaseProvider.Sqlite] = new SqliteConfigurator()
        };
    }
    
    /// <summary>
    /// 配置 DbContextOptionsBuilder
    /// </summary>
    /// <param name="builder">DbContext 选项构建器</param>
    /// <param name="connectionString">连接字符串</param>
    /// <param name="provider">数据库提供者类型</param>
    public static void Configure(DbContextOptionsBuilder builder, string connectionString, DatabaseProvider provider)
    {
        if (!_configurators.TryGetValue(provider, out var configurator))
        {
            throw new NotSupportedException(
                $"Database provider {provider} is not supported. " +
                $"Supported providers: {string.Join(", ", _configurators.Keys)}.");
        }
        
        configurator.Configure(builder, connectionString);
    }
    
    /// <summary>
    /// 注册自定义数据库提供者配置器
    /// </summary>
    /// <param name="provider">数据库提供者类型</param>
    /// <param name="configurator">配置器实例</param>
    public static void RegisterConfigurator(DatabaseProvider provider, IDatabaseProviderConfigurator configurator)
    {
        Check.NotNull(configurator);
        _configurators[provider] = configurator;
    }
    
    /// <summary>
    /// 检查是否支持指定的数据库提供者
    /// </summary>
    /// <param name="provider">数据库提供者类型</param>
    /// <returns>是否支持</returns>
    public static bool IsSupported(DatabaseProvider provider)
    {
        return _configurators.ContainsKey(provider);
    }
    
    /// <summary>
    /// 获取所有支持的数据库提供者
    /// </summary>
    /// <returns>支持的提供者列表</returns>
    public static IReadOnlyCollection<DatabaseProvider> GetSupportedProviders()
    {
        return _configurators.Keys.ToList().AsReadOnly();
    }
}