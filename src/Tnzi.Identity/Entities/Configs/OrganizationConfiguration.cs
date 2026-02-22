namespace Tnzi.Identity.Entities.Configs;

/// <summary>
/// Organization 实体配置类
/// </summary>
public class OrganizationConfiguration : EntityTypeConfigurationBase<Organization, Guid>
{
    public override void Configure(EntityTypeBuilder<Organization> builder)
    {
        // 表名由 TableNamePrefix 属性自动处理
        builder.HasKey(o => o.Id);

        // 属性配置
        builder.Property(o => o.Name).IsRequired().HasMaxLength(200);
        builder.Property(o => o.Code).HasMaxLength(50);
        builder.Property(o => o.Remark).HasMaxLength(500);
        builder.Property(o => o.Path).HasMaxLength(2000);
        builder.Property(o => o.SortOrder).IsRequired().HasDefaultValue(0);
        builder.Property(o => o.IsEnabled).IsRequired().HasDefaultValue(true);
        builder.Property(o => o.Level).IsRequired().HasDefaultValue(0);

        // 关系配置
        builder.HasOne(o => o.Parent)
            .WithMany(o => o.Children)
            .HasForeignKey(o => o.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        // 索引配置（使用无参数方法自动检测数据库提供者，确保跨数据库兼容性）
        builder.HasIndex(o => o.Code)
            .IsUnique()
            .HasFilter(IndexFilterFactory.GetCodeNotNullAndIsDeletedFalse());
        builder.HasIndex(o => o.ParentId);
        builder.HasIndex(o => o.Path);
    }
}
