namespace Tnzi.EFCore.Services;

/// <summary>
/// Repository 注册器接口
/// 负责自动发现和注册 Repository
/// </summary>
public interface IRepositoryRegistrar
{
    /// <summary>
    /// 基于 DbContext 的 DbSet 属性注册 Repository
    /// </summary>
    void RegisterRepositoriesFromDbContext(
        IServiceCollection services, 
        Type dbContextType, 
        ILogger? logger = null);

    /// <summary>
    /// 基于 IEntityRegister 注册 Repository（用于没有 DbSet 的实体）
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="dbContextType">DbContext 类型</param>
    /// <param name="isPrimary">是否为主 DbContext。只有主 DbContext 才会注册 DbContextType 为 null 的实体</param>
    /// <param name="logger">日志记录器</param>
    void RegisterRepositoriesFromEntityRegisters(
        IServiceCollection services, 
        Type dbContextType, 
        bool isPrimary = false,
        ILogger? logger = null);

    /// <summary>
    /// 为实体注册 Repository
    /// </summary>
    void RegisterRepository(
        IServiceCollection services, 
        Type entityType, 
        Type dbContextType, 
        ILogger? logger = null);
}
