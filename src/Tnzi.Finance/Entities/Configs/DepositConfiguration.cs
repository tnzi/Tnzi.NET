namespace Tnzi.Finance.Entities.Configs;

/// <summary>
/// 银行存款单配置
/// </summary>
public class DepositConfiguration : EntityTypeConfigurationBase<Deposit, Guid>
{
    public override void Configure(EntityTypeBuilder<Deposit> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(e => e.Number).HasMaxLength(64);
        builder.Property(e => e.Memo).HasMaxLength(500);
        builder.Property(e => e.Reference).HasMaxLength(128);
        builder.Property(e => e.Currency).HasMaxLength(8).IsRequired();
        builder.Property(e => e.ExchangeRate).HasExchangeRatePrecision();
        builder.Property(e => e.Amount).HasMoneyPrecision();
        builder.Property(e => e.BaseAmount).HasMoneyPrecision();

        builder.HasMany(e => e.Lines)
            .WithOne()
            .HasForeignKey(l => l.DepositId)
            .OnDelete(DeleteBehavior.Cascade);

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
        builder.HasIndex(e => e.DepositDate);
        builder.HasIndex(e => e.FromAccountId);
        builder.HasIndex(e => e.ToAccountId);
    }
}
