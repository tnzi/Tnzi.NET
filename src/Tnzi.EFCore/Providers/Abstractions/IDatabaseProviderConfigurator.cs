
namespace Tnzi.EFCore.Providers;

/// <summary>
/// 数据库提供者配置器接口
/// 用于抽象不同数据库提供者的配置逻辑
/// </summary>
public interface IDatabaseProviderConfigurator
{
    /// <summary>
    /// 支持的提供者类型
    /// </summary>
    DatabaseProvider Provider { get; }
    
    /// <summary>
    /// 配置 DbContextOptionsBuilder
    /// </summary>
    /// <param name="builder">DbContext 选项构建器</param>
    /// <param name="connectionString">连接字符串</param>
    void Configure(DbContextOptionsBuilder builder, string connectionString);
}