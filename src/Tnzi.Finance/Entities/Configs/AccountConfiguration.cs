namespace Tnzi.Finance.Entities.Configs;

/// <summary>
/// 会计科目配置
/// </summary>
public class AccountConfiguration : EntityTypeConfigurationBase<Account, Guid>
{
    public override void Configure(EntityTypeBuilder<Account> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(a => a.Code).HasMaxLength(32).IsRequired();
        builder.Property(a => a.Name).HasMaxLength(128).IsRequired();
        builder.Property(a => a.Description).HasMaxLength(500);
        builder.Property(a => a.SubType).HasMaxLength(64);
        builder.Property(a => a.Currency).HasMaxLength(8);

        builder.HasOne(a => a.Parent)
            .WithMany(a => a.Children)
            .HasForeignKey(a => a.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(a => new { a.TenantId, a.Code }).IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
            builder.HasIndex(a => new { a.TenantId, a.SystemRole }).IsUnique()
                .HasFilter(IndexFilterFactory.GetColumnNotNullAndIsDeletedFalse("SystemRole"));
            builder.HasIndex(a => a.TenantId);
        }
        else
        {
            builder.HasIndex(a => a.Code).IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
            builder.HasIndex(a => a.SystemRole).IsUnique()
                .HasFilter(IndexFilterFactory.GetColumnNotNullAndIsDeletedFalse("SystemRole"));
        }

        builder.HasIndex(a => a.ParentId);
        builder.HasIndex(a => a.RootType);
        builder.HasIndex(a => a.IsActive);
    }
}
