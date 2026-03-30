namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// SkillCategory EF Core 配置
/// </summary>
public class SkillCategoryConfiguration : EntityTypeConfigurationBase<SkillCategory, Guid>
{
    public override void Configure(EntityTypeBuilder<SkillCategory> builder)
    {
        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.Slug)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.Description)
            .HasMaxLength(500);

        builder.Property(e => e.Icon)
            .HasMaxLength(200);

        builder.Property(e => e.SortOrder)
            .HasDefaultValue(0);

        // 同一租户下 slug 唯一
        builder.HasIndex(e => new { e.TenantId, e.Slug })
            .IsUnique();

        // 自引用父子关系
        builder.HasOne(e => e.Parent)
            .WithMany(e => e.Children)
            .HasForeignKey(e => e.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        // 父分类查询索引
        builder.HasIndex(e => e.ParentId);
    }
}
