
namespace Tnzi.EFCore;

/// <summary>
/// 实体注册器接口，用于将实体自动注册到 DbContext
/// </summary>
public interface IEntityRegister
{
    /// <summary>
    /// 获取所属的 DbContext 类型，null 表示注册到默认/主 DbContext
    /// </summary>
    Type? DbContextType { get; }

    /// <summary>
    /// 获取实体类型
    /// </summary>
    Type EntityType { get; }

    /// <summary>
    /// 将实体注册到模型构建器
    /// </summary>
    void RegisterTo(ModelBuilder modelBuilder);

    /// <summary>
    /// 将实体注册到模型构建器（带 DbContext 参数，用于获取数据库提供者信息）
    /// </summary>
    void RegisterTo(ModelBuilder modelBuilder, DbContext? dbContext);
}