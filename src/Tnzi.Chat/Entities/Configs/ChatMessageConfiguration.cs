namespace Tnzi.Chat.Entities.Configs;

public class ChatMessageConfiguration : EntityTypeConfigurationBase<ChatMessage, Guid>
{
    public override void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;
        if (multiTenancyEnabled) builder.HasIndex(m => m.TenantId);

        // Map to singular "Message" so the module prefix yields "Chat_Message"
        // (consistent with Chat_Conversation/Chat_ConversationMember and the table-naming convention),
        // rather than the entity-derived "Chat_ChatMessage".
        builder.ToTable("Message");

        builder.Property(m => m.Content).IsRequired().HasMaxLength(4000);
        builder.Property(m => m.FileId).HasMaxLength(256);
        builder.Property(m => m.FileName).HasMaxLength(512);
        builder.Property(m => m.Title).HasMaxLength(200);
        builder.Property(m => m.LinkUrl).HasMaxLength(2000);
        builder.Property(m => m.Category).HasMaxLength(100);

        builder.HasIndex(m => new { m.ConversationId, m.SentAt });

        builder.HasOne(m => m.Conversation)
            .WithMany()
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
