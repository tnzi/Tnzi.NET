
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
    /// <param name="options">provider 连接级选项（重试策略、命令超时）；为 null 时保持 provider 默认行为</param>
    void Configure(DbContextOptionsBuilder builder, string connectionString, DbProviderConfigureOptions? options = null);
}