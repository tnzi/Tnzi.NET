namespace Tnzi.Payment.Entities.Configs;


public class RefundConfiguration : EntityTypeConfigurationBase<Refund, Guid>
{
    public override void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.Property(r => r.RefundNo).HasMaxLength(64).IsRequired();
        builder.Property(r => r.Currency).HasMaxLength(8).IsRequired().HasDefaultValue("USD");
        builder.Property(r => r.Reason).HasMaxLength(500);
        builder.Property(r => r.ExternalRefundNo).HasMaxLength(128);
        builder.Property(r => r.ApproveRemark).HasMaxLength(500);
        builder.Property(r => r.BusinessOrderNo).HasMaxLength(128);

        builder.HasIndex(r => r.RefundNo).IsUnique();
        builder.HasIndex(r => r.Status);
        builder.HasIndex(r => r.CreationTime);
    }
}
