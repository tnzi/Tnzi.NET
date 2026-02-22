
namespace Tnzi.EFCore.Services;

/// <summary>
/// DataSeeder 管理服务实现
/// </summary>
public class DataSeederManager : IDataSeederManager
{
    // 缓存扫描结果，避免重复扫描程序集
    private static List<Type>? _cachedSeederTypes;
    private static readonly object _lock = new();
    
    /// <summary>
    /// 发现所有 IDataSeeder 实现类型
    /// </summary>
    public IReadOnlyList<Type> DiscoverSeederTypes()
    {
        return DiscoverSeederTypes(null);
    }

    /// <summary>
    /// 发现所有 IDataSeeder 实现类型
    /// </summary>
    /// <param name="logger">日志记录器</param>
    public IReadOnlyList<Type> DiscoverSeederTypes(ILogger? logger)
    {
        if (_cachedSeederTypes != null)
        {
            return _cachedSeederTypes;
        }

        lock (_lock)
        {
            if (_cachedSeederTypes != null)
            {
                return _cachedSeederTypes;
            }

            // 使用统一的程序集扫描工具
            var seederTypes = AssemblyScanner.FindTypesImplementing<IDataSeeder>(null, logger);
            _cachedSeederTypes = seederTypes.ToList();
            
            return _cachedSeederTypes;
        }
    }

    /// <summary>
    /// 注册所有发现的 DataSeeder 到服务集合
    /// </summary>
    public void RegisterSeeders(IServiceCollection services, ILogger? logger = null)
    {
        var seederTypes = DiscoverSeederTypes();
        
        if (seederTypes.Count == 0)
        {
            logger?.LogDebug("No IDataSeeder implementations found.");
            return;
        }

        foreach (var seederType in seederTypes)
        {
            // 检查是否已注册
            if (services.Any(s => s.ServiceType == typeof(IDataSeeder) && 
                                 s.ImplementationType == seederType))
            {
                continue;
            }

            services.AddTransient(typeof(IDataSeeder), seederType);
            logger?.LogDebug("Registered DataSeeder: {SeederType}", seederType.FullName);
        }

        logger?.LogInformation("Auto-registered {Count} DataSeeders.", seederTypes.Count);
    }

    /// <summary>
    /// 清除类型缓存（用于测试）
    /// </summary>
    internal static void ClearCache()
    {
        lock (_lock)
        {
            _cachedSeederTypes = null;
        }
    }
}