
namespace Tnzi.EFCore.Services;

/// <summary>
/// DbContext 注册服务接口
/// 负责将发现的 DbContext 注册到 DI 容器
/// </summary>
public interface IDbContextRegistrar
{
    /// <summary>
    /// 注册 DbContext 到服务集合
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="item">DbContext 发现项</param>
    /// <param name="isPrimary">是否为主 DbContext</param>
    /// <param name="configuration">配置对象（可选，用于环境变量展开）</param>
    /// <param name="logger">日志记录器（可选）</param>
    void RegisterDbContext(
        IServiceCollection services,
        DbContextDiscoveryItem item,
        bool isPrimary,
        IConfiguration? configuration = null,
        ILogger? logger = null);

    /// <summary>
    /// 批量注册 DbContext
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="result">发现结果</param>
    /// <param name="configuration">配置对象（可选，用于环境变量展开）</param>
    /// <param name="logger">日志记录器（可选）</param>
    void RegisterDbContexts(
        IServiceCollection services,
        DbContextDiscoveryResult result,
        IConfiguration? configuration = null,
        ILogger? logger = null);
}