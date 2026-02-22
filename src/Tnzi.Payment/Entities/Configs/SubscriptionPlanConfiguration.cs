namespace Tnzi.Payment.Entities.Configs;

public class SubscriptionPlanConfiguration : EntityTypeConfigurationBase<SubscriptionPlan, Guid>
{
    public override void Configure(EntityTypeBuilder<SubscriptionPlan> builder)
    {
        builder.Property(p => p.PlanCode).HasMaxLength(32).IsRequired();
        builder.Property(p => p.PlanName).HasMaxLength(128).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(500);
        builder.Property(p => p.Currency).HasMaxLength(8).IsRequired().HasDefaultValue("USD");

        builder.HasMany(p => p.Subscriptions)
            .WithOne(s => s.Plan)
            .HasForeignKey(s => s.PlanId)
            .HasPrincipalKey(p => p.Id);

        builder.HasIndex(p => p.PlanCode).IsUnique();
        builder.HasIndex(p => p.IsActive);
        builder.HasIndex(p => p.SortOrder);
    }
}
