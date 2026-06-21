namespace Tnzi.Chat.Entities.Configs;

public class ConversationConfiguration : EntityTypeConfigurationBase<Conversation, Guid>
{
    public override void Configure(EntityTypeBuilder<Conversation> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;
        if (multiTenancyEnabled) builder.HasIndex(c => c.TenantId);

        builder.Property(c => c.Title).HasMaxLength(200);
        builder.Property(c => c.AvatarFileId).HasMaxLength(256);
        builder.Property(c => c.Notice).HasMaxLength(2000);
        builder.Property(c => c.DirectKey).HasMaxLength(128);
        builder.Property(c => c.LastMessagePreview).HasMaxLength(200);

        if (multiTenancyEnabled)
            builder.HasIndex(c => new { c.TenantId, c.DirectKey }).IsUnique();
        else
            builder.HasIndex(c => c.DirectKey).IsUnique().HasFilter(null);
        builder.HasIndex(c => new { c.Type, c.LastMessageAt });
    }
}
