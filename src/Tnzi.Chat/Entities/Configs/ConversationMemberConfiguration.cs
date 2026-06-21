namespace Tnzi.Chat.Entities.Configs;

public class ConversationMemberConfiguration : EntityTypeConfigurationBase<ConversationMember, Guid>
{
    public override void Configure(EntityTypeBuilder<ConversationMember> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;
        if (multiTenancyEnabled) builder.HasIndex(m => m.TenantId);

        builder.HasIndex(m => new { m.ConversationId, m.UserId }).IsUnique();
        builder.HasIndex(m => new { m.UserId, m.RemovedAt });

        builder.HasOne(m => m.Conversation)
            .WithMany(c => c.Members)
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(m => m.Remark).HasMaxLength(100);
        builder.Property(m => m.Alias).HasMaxLength(100);
    }
}
