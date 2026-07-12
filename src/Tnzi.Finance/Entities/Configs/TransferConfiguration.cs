namespace Tnzi.Finance.Entities.Configs;

/// <summary>
/// 资金划转单配置
/// </summary>
public class TransferConfiguration : EntityTypeConfigurationBase<Transfer, Guid>
{
    public override void Configure(EntityTypeBuilder<Transfer> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.Number).HasMaxLength(64);
        builder.Property(e => e.Memo).HasMaxLength(500);
        builder.Property(e => e.Reference).HasMaxLength(128);
        builder.Property(e => e.Currency).HasMaxLength(8).IsRequired();
        builder.Property(e => e.ExchangeRate).HasExchangeRatePrecision();
        builder.Property(e => e.Amount).HasMoneyPrecision();
        builder.Property(e => e.BaseAmount).HasMoneyPrecision();

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
        builder.HasIndex(e => e.TransferDate);
        builder.HasIndex(e => e.FromAccountId);
        builder.HasIndex(e => e.ToAccountId);
    }
}
