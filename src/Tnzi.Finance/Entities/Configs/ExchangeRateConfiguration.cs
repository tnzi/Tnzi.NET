namespace Tnzi.Finance.Entities.Configs;

/// <summary>
/// 汇率配置
/// </summary>
public class ExchangeRateConfiguration : EntityTypeConfigurationBase<ExchangeRate, Guid>
{
    public override void Configure(EntityTypeBuilder<ExchangeRate> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(r => r.FromCurrency).HasMaxLength(8).IsRequired();
        builder.Property(r => r.ToCurrency).HasMaxLength(8).IsRequired();
        builder.Property(r => r.Rate).HasExchangeRatePrecision();
        builder.Property(r => r.Source).HasMaxLength(64);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(r => new { r.TenantId, r.FromCurrency, r.ToCurrency, r.RateDate }).IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
            builder.HasIndex(r => r.TenantId);
        }
        else
        {
            builder.HasIndex(r => new { r.FromCurrency, r.ToCurrency, r.RateDate }).IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        }
    }
}
