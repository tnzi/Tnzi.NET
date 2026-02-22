namespace Tnzi.EFCore.Services;

/// <summary>
/// DataSeeder 管理服务接口
/// 负责自动发现和注册 IDataSeeder 实现
/// </summary>
public interface IDataSeederManager
{
    /// <summary>
    /// 发现所有 IDataSeeder 实现类型
    /// </summary>
    /// <returns>IDataSeeder 实现类型列表</returns>
    IReadOnlyList<Type> DiscoverSeederTypes();
    
    /// <summary>
    /// 注册所有发现的 DataSeeder 到服务集合
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="logger">日志记录器（可选）</param>
    void RegisterSeeders(IServiceCollection services, ILogger? logger = null);
}
