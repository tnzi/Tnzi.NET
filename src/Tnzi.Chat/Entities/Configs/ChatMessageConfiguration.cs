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

        builder.Property(m => m.Content).IsRequired().HasMaxLength(ChatFieldLimits.MessageContent);
        builder.Property(m => m.FileId).HasMaxLength(ChatFieldLimits.FileId);
        builder.Property(m => m.FileName).HasMaxLength(ChatFieldLimits.FileName);
        builder.Property(m => m.Title).HasMaxLength(ChatFieldLimits.Title);
        builder.Property(m => m.LinkUrl).HasMaxLength(ChatFieldLimits.LinkUrl);
        builder.Property(m => m.Category).HasMaxLength(ChatFieldLimits.Category);

        builder.HasIndex(m => new { m.ConversationId, m.SentAt });

        builder.HasOne(m => m.Conversation)
            .WithMany()
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
