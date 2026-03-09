namespace Tnzi.Template.Entities.Configs;

/// <summary>
/// Template 实体配置类
/// </summary>
public class TemplateConfiguration : EntityTypeConfigurationBase<Template, Guid>
{
    public override void Configure(EntityTypeBuilder<Template> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        // 属性配置
        builder.Property(t => t.TemplateName).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Module).IsRequired().HasMaxLength(100);
        builder.Property(t => t.Category).IsRequired().HasMaxLength(100);
        builder.Property(t => t.SubjectTemplate).IsRequired();
        builder.Property(t => t.ContentTemplate).IsRequired();
        builder.Property(t => t.DefaultLayoutName).HasMaxLength(200);
        builder.Property(t => t.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(t => t.Description).HasMaxLength(500);
        builder.Property(t => t.Metadata).HasMaxLength(4000);

        // 唯一索引：Module + Category + TemplateName（多租户下按 TenantId 分区）
        if (multiTenancyEnabled)
        {
            builder.HasIndex(t => new { t.TenantId, t.Module, t.Category, t.TemplateName })
                .IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
            builder.HasIndex(t => t.TenantId)
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        }
        else
        {
            builder.HasIndex(t => new { t.Module, t.Category, t.TemplateName })
                .IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        }

        builder.HasIndex(t => t.Module);
        builder.HasIndex(t => t.Category);
        builder.HasIndex(t => t.IsActive);
    }
}
