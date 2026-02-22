namespace Tnzi.Payment.Entities.Configs;

public class PaymentConfiguration : EntityTypeConfigurationBase<Payment, Guid>
{
    public override void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.Property(p => p.TradeNo).HasMaxLength(64).IsRequired();
        builder.Property(p => p.ExternalTradeNo).HasMaxLength(128);
        builder.Property(p => p.BusinessOrderNo).HasMaxLength(128).IsRequired();
        builder.Property(p => p.ChannelCode).HasMaxLength(32).IsRequired();
        builder.Property(p => p.Currency).HasMaxLength(8).IsRequired().HasDefaultValue("USD");
        builder.Property(p => p.Description).HasMaxLength(500);
        // ChannelResponse和ExtraData存储JSON数据，不指定类型以保持数据库兼容性
        // EF Core会根据数据库提供者自动选择合适的类型

        builder.HasMany(p => p.Refunds)
            .WithOne(r => r.Payment)
            .HasForeignKey(r => r.PaymentId)
            .HasPrincipalKey(p => p.Id);

        builder.HasOne(p => p.Invoice)
            .WithOne(i => i.Payment)
            .HasForeignKey<Invoice>(i => i.PaymentId)
            .HasPrincipalKey<Payment>(p => p.Id);

        builder.HasIndex(p => p.TradeNo).IsUnique();
        builder.HasIndex(p => p.BusinessOrderNo);
        builder.HasIndex(p => p.Status);
        builder.HasIndex(p => p.ChannelCode);
        builder.HasIndex(p => p.CreationTime);
    }
}
