namespace Tnzi.Chat.Entities.Configs;

/// <summary>
/// MessageRole 实体配置
/// </summary>
public class MessageRoleConfiguration : EntityTypeConfigurationBase<MessageRole, Guid>
{
    public override void Configure(EntityTypeBuilder<MessageRole> builder)
    {
        builder.HasIndex(mr => new { mr.MessageId, mr.RoleId }).IsUnique();
        builder.HasIndex(mr => mr.RoleId);

        builder.HasOne(mr => mr.Message)
            .WithMany(m => m.Roles)
            .HasForeignKey(mr => mr.MessageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
