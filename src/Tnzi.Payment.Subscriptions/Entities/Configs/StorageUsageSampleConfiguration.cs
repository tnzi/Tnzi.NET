namespace Tnzi.Payment.Subscriptions.Entities;

public class StorageUsageSampleConfiguration : EntityTypeConfigurationBase<StorageUsageSample, Guid>
{
    public override void Configure(EntityTypeBuilder<StorageUsageSample> builder)
    {
        builder.Property(s => s.AccountType).HasMaxLength(BillingAccountRef.TypeMaxLength).IsRequired();
        builder.Property(s => s.AccountId).HasMaxLength(BillingAccountRef.IdMaxLength).IsRequired();

        // 一个账户一天一条：采样的幂等靠它，不靠「先查有没有」
        builder.HasIndex(s => new { s.AccountType, s.AccountId, s.SampleDate })
            .IsUnique()
            .HasDatabaseName("IX_Payment_StorageUsageSample_Account_Date");
    }
}
