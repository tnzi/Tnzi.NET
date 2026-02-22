
namespace Tnzi.System.Entities.Configs;

/// <summary>
/// AccessLog 实体配置类
/// </summary>
public class AccessLogConfiguration : EntityTypeConfigurationBase<AccessLog, Guid>
{
    public override void Configure(EntityTypeBuilder<AccessLog> builder)
    {
        // 表名由 TableNamePrefix 属性自动处理
        builder.HasKey(a => a.Id);
        builder.HasIndex(a => a.UserId);
        builder.HasIndex(a => a.CreationTime);
        builder.HasIndex(a => new { a.Path, a.CreationTime });
    }
}
