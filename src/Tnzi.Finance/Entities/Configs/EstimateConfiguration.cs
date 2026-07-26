namespace Tnzi.Finance.Entities.Configs;

/// <summary>
/// 报价单配置
/// </summary>
public class EstimateConfiguration : EntityTypeConfigurationBase<Estimate, Guid>
{
    public override void Configure(EntityTypeBuilder<Estimate> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.Number).HasMaxLength(64);
        builder.Property(e => e.Memo).HasMaxLength(500);
        builder.Property(e => e.InternalNote).HasMaxLength(1000);
        builder.Property(e => e.ConvertedToDocType).HasMaxLength(64);
        builder.Property(e => e.Currency).HasMaxLength(8).IsRequired();
        builder.Property(e => e.SubTotal).HasMoneyPrecision();
        builder.Property(e => e.TaxTotal).HasMoneyPrecision();
        builder.Property(e => e.Total).HasMoneyPrecision();

        builder.HasMany(e => e.Lines)
            .WithOne()
            .HasForeignKey(l => l.EstimateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Customer).WithMany().HasForeignKey(e => e.CustomerId).OnDelete(DeleteBehavior.Restrict);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(e => new { e.TenantId, e.Number }).IsUnique()
                .HasFilter(IndexFilterFactory.GetColumnNotNullAndIsDeletedFalse("Number"));
            builder.HasIndex(e => e.TenantId);
        }
        else
        {
            builder.HasIndex(e => e.Number).IsUnique()
                .HasFilter(IndexFilterFactory.GetColumnNotNullAndIsDeletedFalse("Number"));
        }

        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.DocDate);
        builder.HasIndex(e => e.CustomerId);
        builder.HasIndex(e => e.ExpiryDate);
    }
}
