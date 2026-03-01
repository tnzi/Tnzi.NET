namespace Tnzi.Chat.Entities.Configs;

/// <summary>
/// MessageReceive 实体配置
/// </summary>
public class MessageReceiveConfiguration : EntityTypeConfigurationBase<MessageReceive, Guid>
{
    public override void Configure(EntityTypeBuilder<MessageReceive> builder)
    {
        builder.HasIndex(mr => new { mr.MessageId, mr.UserId }).IsUnique();
        builder.HasIndex(mr => new { mr.UserId, mr.ReadTime });

        builder.HasOne(mr => mr.Message)
            .WithMany(m => m.Receives)
            .HasForeignKey(mr => mr.MessageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
