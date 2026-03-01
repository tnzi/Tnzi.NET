namespace Tnzi.Payment.Entities.Configs;

public class SubscriptionConfiguration : EntityTypeConfigurationBase<Subscription, Guid>
{
    public override void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.Property(s => s.SubscriptionNo).HasMaxLength(64).IsRequired();
        builder.Property(s => s.CancelReason).HasMaxLength(500);
        builder.Property(s => s.ChannelCode).HasMaxLength(32).IsRequired();
        builder.Property(s => s.Currency).HasMaxLength(8).IsRequired().HasDefaultValue("USD");

        builder.HasIndex(s => s.SubscriptionNo).IsUnique()
            .HasFilter(IndexFilterFactory.GetIsDeletedFalse());
        builder.HasIndex(s => s.UserId);
        builder.HasIndex(s => s.PlanId);
        builder.HasIndex(s => s.Status);
        builder.HasIndex(s => s.EndTime);
    }
}
