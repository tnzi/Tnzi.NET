namespace Tnzi.Chat.Entities.Configs;

/// <summary>
/// MessageReply 实体配置
/// </summary>
public class MessageReplyConfiguration : EntityTypeConfigurationBase<MessageReply, Guid>
{
    public override void Configure(EntityTypeBuilder<MessageReply> builder)
    {
        builder.Property(mr => mr.Content).IsRequired().HasMaxLength(4000);

        builder.HasIndex(mr => mr.BelongMessageId);
        builder.HasIndex(mr => mr.UserId);
        builder.HasIndex(mr => mr.ParentReplyId);

        builder.HasOne(mr => mr.BelongMessage)
            .WithMany(m => m.Replies)
            .HasForeignKey(mr => mr.BelongMessageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(mr => mr.ParentReply)
            .WithMany(mr => mr.Replies)
            .HasForeignKey(mr => mr.ParentReplyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
