namespace Tnzi.Chat.Entities.Configs;

/// <summary>
/// EF Core configuration for <see cref="ChatSession"/>.
/// </summary>
public class ChatSessionConfiguration : EntityTypeConfigurationBase<ChatSession, Guid>
{
    public override void Configure(EntityTypeBuilder<ChatSession> builder)
    {
        builder.Property(s => s.Title).IsRequired().HasMaxLength(200);
        builder.Property(s => s.Description).HasMaxLength(2000);
        builder.Property(s => s.ParticipantsJson).HasMaxLength(4000).HasDefaultValue("[]");
        builder.Property(s => s.Status).HasConversion<int>();

        builder.HasIndex(s => s.Status);
        builder.HasIndex(s => s.LastMessageAt);
        builder.HasIndex(s => new { s.Status, s.LastMessageAt });
    }
}
