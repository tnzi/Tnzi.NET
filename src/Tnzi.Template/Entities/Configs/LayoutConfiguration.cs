namespace Tnzi.Template.Entities.Configs;

/// <summary>
/// Layout 实体配置类
/// </summary>
public class LayoutConfiguration : EntityTypeConfigurationBase<Layout, Guid>
{
    public override void Configure(EntityTypeBuilder<Layout> builder)
    {
        // 表名由 TableNamePrefix 属性自动处理
        builder.HasKey(l => l.Id);

        // 属性配置
        builder.Property(l => l.LayoutName).IsRequired().HasMaxLength(200);
        builder.Property(l => l.Module).IsRequired().HasMaxLength(100);
        builder.Property(l => l.Category).IsRequired().HasMaxLength(100);
        builder.Property(l => l.LayoutContent).IsRequired();
        builder.Property(l => l.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(l => l.IsDefault).IsRequired().HasDefaultValue(false);
        builder.Property(l => l.Description).HasMaxLength(500);
        builder.Property(l => l.Metadata).HasMaxLength(4000);

        // 唯一索引：Module + Category + LayoutName
        builder.HasIndex(l => new { l.Module, l.Category, l.LayoutName })
            .IsUnique()
            .HasFilter(IndexFilterFactory.GetIsDeletedFalse());

        builder.HasIndex(l => l.Module);
        builder.HasIndex(l => l.Category);
        builder.HasIndex(l => l.IsActive);
        builder.HasIndex(l => l.IsDefault);
    }
}
