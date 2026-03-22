namespace Tnzi.AI.Entities.Configs;

/// <summary>
/// SkillEntity EF Core 配置
/// </summary>
public class SkillEntityConfiguration : EntityTypeConfigurationBase<SkillEntity, Guid>
{
    public override void Configure(EntityTypeBuilder<SkillEntity> builder)
    {
        builder.Property(e => e.Slug)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(e => e.Scope)
            .IsRequired();

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.Description)
            .HasMaxLength(1000);

        builder.Property(e => e.WhenToUse)
            .HasMaxLength(2000);

        builder.Property(e => e.Version)
            .HasMaxLength(50);

        builder.Property(e => e.Author)
            .HasMaxLength(200);

        builder.Property(e => e.Enabled)
            .HasDefaultValue(true);

        builder.Property(e => e.Priority)
            .HasDefaultValue(0);

        // 唯一索引：同一租户/用户下同一作用域的 slug 唯一
        builder.HasIndex(e => new { e.Slug, e.Scope, e.TenantId, e.OwnerUserId })
            .IsUnique()
            .HasFilter(IndexFilterFactory.GetIsDeletedFalse());

        // 租户查询索引
        builder.HasIndex(e => new { e.Scope, e.TenantId })
            .HasFilter(IndexFilterFactory.GetIsDeletedFalse());

        // 用户查询索引
        builder.HasIndex(e => e.OwnerUserId)
            .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
    }
}
