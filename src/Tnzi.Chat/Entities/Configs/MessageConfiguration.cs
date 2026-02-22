namespace Tnzi.Chat.Entities.Configs;

/// <summary>
/// Message 实体配置
/// </summary>
public class MessageConfiguration : EntityTypeConfigurationBase<Message, Guid>
{
    public override void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Title).IsRequired().HasMaxLength(200);
        builder.Property(m => m.Content).IsRequired().HasMaxLength(4000);

        builder.HasIndex(m => m.SenderId);
        builder.HasIndex(m => m.CreationTime);
        builder.HasIndex(m => m.MessageType);
        builder.HasIndex(m => m.IsSent);
        builder.HasIndex(m => new { m.IsSent, m.MessageType, m.CreationTime });
    }
}
