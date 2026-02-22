
namespace Tnzi.System.Entities.Configs;

/// <summary>
/// Setting 实体配置类
/// </summary>
public class SettingConfiguration : EntityTypeConfigurationBase<Setting, Guid>
{
    public override void Configure(EntityTypeBuilder<Setting> builder)
    {
        // 表名由 TableNamePrefix 属性自动处理
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Key)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(s => s.Value)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(s => s.Description)
            .HasMaxLength(500);

        builder.Property(s => s.Group)
            .HasMaxLength(50);

        builder.Property(s => s.ValueType)
            .HasDefaultValue(SettingValueType.String);

        // 创建唯一索引（包含 IsDeleted 过滤）
        builder.HasIndex(s => s.Key)
            .IsUnique()
            .HasFilter(IndexFilterFactory.GetIsDeletedFalse());

        // 创建分组索引（用于查询）
        builder.HasIndex(s => s.Group);
    }
}
