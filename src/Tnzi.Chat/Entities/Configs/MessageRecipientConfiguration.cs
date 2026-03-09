namespace Tnzi.Chat.Entities.Configs;

/// <summary>
/// MessageRecipient 实体配置
/// </summary>
public class MessageRecipientConfiguration : EntityTypeConfigurationBase<MessageRecipient, Guid>
{
    public override void Configure(EntityTypeBuilder<MessageRecipient> builder)
    {
        var multiTenancyEnabled = (GetDbContext() as IMultiTenancySwitchProvider)?.IsMultiTenancyEnabled ?? false;

        if (multiTenancyEnabled)
        {
            builder.HasIndex(mr => mr.TenantId);
        }

        builder.HasIndex(mr => new { mr.MessageId, mr.UserId }).IsUnique();
        builder.HasIndex(mr => mr.UserId);

        builder.HasOne(mr => mr.Message)
            .WithMany(m => m.Recipients)
            .HasForeignKey(mr => mr.MessageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
