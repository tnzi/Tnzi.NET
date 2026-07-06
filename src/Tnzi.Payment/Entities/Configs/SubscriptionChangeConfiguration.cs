namespace Tnzi.Payment.Entities.Configs;

public class SubscriptionChangeConfiguration : EntityTypeConfigurationBase<SubscriptionChange, Guid>
{
    public override void Configure(EntityTypeBuilder<SubscriptionChange> builder)
    {
        builder.Property(c => c.ProratedAmount).HasMoneyPrecision();

        builder.HasOne(c => c.Subscription)
            .WithMany()
            .HasForeignKey(c => c.SubscriptionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.FromPlan)
            .WithMany()
            .HasForeignKey(c => c.FromPlanId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.ToPlan)
            .WithMany()
            .HasForeignKey(c => c.ToPlanId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => new { c.SubscriptionId, c.Status });
        builder.HasIndex(c => c.EffectiveDate);
    }
}
