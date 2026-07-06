namespace Tnzi.Payment.Entities.Configs;

public class SubscriptionConfiguration : EntityTypeConfigurationBase<Subscription, Guid>
{
    public override void Configure(EntityTypeBuilder<Subscription> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        builder.Property(s => s.SubscriptionNo).HasMaxLength(64).IsRequired();
        builder.Property(s => s.CancelReason).HasMaxLength(500);
        builder.Property(s => s.ChannelCode).HasMaxLength(32).IsRequired();
        builder.Property(s => s.Currency).HasMaxLength(8).IsRequired().HasDefaultValue("USD");
        builder.Property(s => s.DiscountAmount).HasMoneyPrecision();
        builder.Property(s => s.OriginalPrice).HasMoneyPrecision();
        builder.Property(s => s.PaidAmount).HasMoneyPrecision();
        builder.Property(s => s.ProviderCustomerId).HasMaxLength(128);
        builder.Property(s => s.PaymentMethodToken).HasMaxLength(128);
        builder.Property(s => s.LastBillingTradeNo).HasMaxLength(64);

        if (multiTenancyEnabled)
        {
            builder.HasIndex(s => new { s.TenantId, s.SubscriptionNo }).IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
            builder.HasIndex(s => s.TenantId);
        }
        else
        {
            builder.HasIndex(s => s.SubscriptionNo).IsUnique()
                .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        }

        builder.HasIndex(s => s.UserId);
        builder.HasIndex(s => s.PlanId);
        builder.HasIndex(s => s.Status);
        builder.HasIndex(s => s.EndTime);
        // 后台续费/过期扫描：按状态 + 下次计费时间过滤
        builder.HasIndex(s => new { s.Status, s.NextBillingTime });
        // 后台试用转正扫描：按状态 + 试用结束时间过滤
        builder.HasIndex(s => new { s.Status, s.TrialEndTime });
    }
}
