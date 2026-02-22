
namespace Tnzi.EFCore;

/// <summary>
/// 定义实体的批量配置功能
/// </summary>
public interface IEntityBatchConfiguration
{
    /// <summary>
    /// 配置指定的<see cref="IMutableEntityType"/>
    /// </summary>
    /// <param name="modelBuilder">模型构建器</param>
    /// <param name="mutableEntityType">实体的<see cref="IMutableEntityType"/>类型</param>
    void Configure(ModelBuilder modelBuilder, IMutableEntityType mutableEntityType);
}