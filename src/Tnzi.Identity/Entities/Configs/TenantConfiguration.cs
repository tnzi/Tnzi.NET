namespace Tnzi.Identity.Entities.Configs;

/// <summary>
/// Tenant 实体配置类
/// </summary>
public class TenantConfiguration : EntityTypeConfigurationBase<Tenant, Guid>
{
    public override void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.Property(t => t.Name).IsRequired().HasMaxLength(128);
        builder.Property(t => t.Code).HasMaxLength(64);
        builder.Property(t => t.Remark).HasMaxLength(500);
        builder.Property(t => t.IsEnabled).IsRequired().HasDefaultValue(true);

        builder.HasIndex(t => t.Name);
        builder.HasIndex(t => t.IsEnabled);
        builder.HasIndex(t => t.ExpiredAt);
        builder.HasIndex(t => t.Code)
            .IsUnique()
            .HasFilter(IndexFilterFactory.GetCodeNotNullAndIsDeletedFalse());
    }
}
