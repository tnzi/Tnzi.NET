namespace Tnzi.AI.Skills.Entities.Configs;

/// <summary>
/// SkillCategory EF Core 配置
/// </summary>
public class SkillCategoryConfiguration : EntityTypeConfigurationBase<SkillCategory, Guid>
{
    public override void Configure(EntityTypeBuilder<SkillCategory> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

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

        // 同一租户下 slug 唯一；不开多租户时全局唯一。
        // ★ 不开多租户时 TenantId 被忽略，建在它上面的索引随之被 EF 丢掉：此前单租户部署上
        //   这条唯一约束根本不存在，服务层「先查后插」的查重在并发下兜不住。
        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.Slug })
                .IsUnique();
        }
        else
        {
            builder.HasIndex(e => e.Slug)
                .IsUnique();
        }

        // 自引用父子关系
        builder.HasOne(e => e.Parent)
            .WithMany(e => e.Children)
            .HasForeignKey(e => e.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        // 父分类查询索引
        builder.HasIndex(e => e.ParentId);
    }
}
