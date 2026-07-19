namespace Tnzi.Chat.Entities.Configs;

public class MessageBlockConfiguration : EntityTypeConfigurationBase<MessageBlock, Guid>
{
    public override void Configure(EntityTypeBuilder<MessageBlock> builder)
    {
        builder.ToTable("MessageBlock");

        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;
        if (multiTenancyEnabled) builder.HasIndex(b => b.TenantId);

        // One isolation row per (message, user); the read-path filter probes by user.
        builder.HasIndex(b => new { b.MessageId, b.UserId }).IsUnique();
        builder.HasIndex(b => b.UserId);

        builder.HasOne(b => b.Message)
            .WithMany()
            .HasForeignKey(b => b.MessageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
